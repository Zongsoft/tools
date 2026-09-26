using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.Runtime.InteropServices;

using Zongsoft.Tools.Packager.Web;

using Xunit;

namespace Zongsoft.Tools.Packager.Tests.Web;

public class WebScriptTest
{
	[Theory]
	[InlineData(null, true)]
	[InlineData("1", true)]
	[InlineData("TrUe", true)]
	[InlineData("0", false)]
	[InlineData("FaLsE", false)]
	public async Task ActivationInstallsOnlyWhenEnabledAsync(string activation, bool enabled)
	{
		using var fixture = new ScriptFixture();
		var result = await fixture.RunAsync(fixture.Scripts.Delivered + "\n" + fixture.Scripts.Activate, activation);
		Assert.Equal(0, result.Code);
		Assert.Equal(enabled, File.Exists(fixture.Link));
		var calls = fixture.Calls;
		Assert.Equal(enabled, calls.Contains("nginx -t -c"));
		Assert.Equal(enabled, calls.Contains("reload nginx.service"));
		Assert.DoesNotContain("start nginx", calls);
		Assert.Contains("$remote_addr", File.ReadAllText(fixture.Configuration));
		Assert.Contains(ScriptFixture.ShellPath(fixture.Root) + "/.certificates/example.pem", File.ReadAllText(fixture.Configuration));
		Assert.Contains("\r\n", File.ReadAllText(fixture.Configuration));
	}

	[Theory]
	[InlineData("")]
	[InlineData("yes")]
	[InlineData("2")]
	public async Task InvalidActivationFailsAsync(string activation)
	{
		using var fixture = new ScriptFixture();
		var result = await fixture.RunAsync(fixture.Scripts.Activate, activation);
		Assert.NotEqual(0, result.Code);
		Assert.Empty(fixture.Calls);
	}

	[Fact]
	public async Task StoppedServiceIsCheckedWithoutStartingOrReloadingAsync()
	{
		using var fixture = new ScriptFixture();
		var result = await fixture.RunAsync(fixture.Scripts.Activate, "true", state: "inactive");
		Assert.Equal(0, result.Code);
		Assert.Contains("nginx -t -c", fixture.Calls);
		Assert.DoesNotContain("reload", fixture.Calls);
		Assert.DoesNotContain("start", fixture.Calls);
	}

	[Theory]
	[InlineData("test")]
	[InlineData("include")]
	[InlineData("state")]
	[InlineData("reload")]
	public async Task ActivationFailureStopsLaterHooksAsync(string failure)
	{
		using var fixture = new ScriptFixture();
		var result = await fixture.RunAsync(fixture.Scripts.Activate + "\nprintf '%s' later", "1", failure: failure);
		Assert.NotEqual(0, result.Code);
		Assert.DoesNotContain("later", result.Output);
		Assert.True(File.Exists(fixture.Configuration));
		if(failure != "reload")
			Assert.DoesNotContain("reload", fixture.Calls);
	}

	[Theory]
	[InlineData("file")]
	[InlineData("link")]
	[InlineData("directory")]
	public async Task ActivationReplacesFilesAndLinksButRejectsDirectoriesAsync(string kind)
	{
		using var fixture = new ScriptFixture();
		File.WriteAllText(fixture.Other, "keep");
		var prefix = kind switch
		{
			"file" => $"printf '%s' old > {ScriptFixture.Quote(fixture.Link)}\n",
			"link" => $"ln -s -- {ScriptFixture.Quote(fixture.Other)} {ScriptFixture.Quote(fixture.Link)}\n",
			_ => $"mkdir {ScriptFixture.Quote(fixture.Link)}\n",
		};
		var result = await fixture.RunAsync(prefix + fixture.Scripts.Activate, "1");
		Assert.Equal(kind == "directory", result.Code != 0);
		Assert.Equal("keep", File.ReadAllText(fixture.Other));
	}

	[Fact]
	public async Task DisabledUpgradeKeepsCurrentLinkAndPrunesOnlyOwnedObsoleteLinksAsync()
	{
		using var fixture = new ScriptFixture();
		var obsolete = Path.Combine(fixture.Root, ".web/nginx/old.conf");
		var oldLink = Path.Combine(fixture.SystemDirectory, "conf.d/old.conf");
		File.WriteAllText(obsolete, "old");
		var prefix = $"ln -s -- {ScriptFixture.Quote(fixture.Configuration)} {ScriptFixture.Quote(fixture.Link)}\nln -s -- {ScriptFixture.Quote(obsolete)} {ScriptFixture.Quote(oldLink)}\n";
		var result = await fixture.RunAsync(prefix + fixture.Scripts.Delivered + "\n" + fixture.Scripts.Activate, "false");
		Assert.Equal(0, result.Code);
		Assert.True(File.Exists(fixture.Link));
		Assert.False(File.Exists(obsolete));
		Assert.False(File.Exists(oldLink));
		Assert.Empty(fixture.Calls);
	}

	[Fact]
	public async Task RemovingWebFromNewPackagePrunesTheBrokenOwnedLinkAsync()
	{
		using var fixture = new ScriptFixture();
		var scripts = Installation.CreateScripts(fixture.CreatePackage(false));
		var prefix = $"ln -s -- {ScriptFixture.Quote(fixture.Configuration)} {ScriptFixture.Quote(fixture.Link)}\nrm -f -- {ScriptFixture.Quote(fixture.Configuration)}\n";
		var result = await fixture.RunAsync(prefix + scripts.Delivered + "\n" + scripts.Activate, "1");
		Assert.Equal(0, result.Code);
		Assert.False(File.Exists(fixture.Link));
		Assert.Contains("reload nginx.service", fixture.Calls);
	}

	[Theory]
	[InlineData("0", "")]
	[InlineData("1", "test")]
	[InlineData("1", "reload")]
	[InlineData("1", "state")]
	public async Task UninstallAlwaysCleansOwnedFilesAndContinuesAfterNginxFailuresAsync(string activation, string failure)
	{
		using var fixture = new ScriptFixture();
		var prefix = $"ln -s -- {ScriptFixture.Quote(fixture.Configuration)} {ScriptFixture.Quote(fixture.Link)}\n";
		var result = await fixture.RunAsync(prefix + fixture.Scripts.Deactivate + "\n" + fixture.Scripts.Cleanup, activation, failure: failure);
		Assert.Equal(0, result.Code);
		Assert.False(File.Exists(fixture.Link));
		Assert.False(Directory.Exists(Path.Combine(fixture.Root, ".web")));
		if(activation == "0")
			Assert.Empty(fixture.Calls);
	}

	[Fact]
	public async Task StagedDeliveryRelocatesWithoutSystemActionsAsync()
	{
		using var fixture = new ScriptFixture();
		var result = await fixture.RunAsync(fixture.Scripts.Delivered, "1", staged: true);
		Assert.Equal(0, result.Code);
		Assert.Empty(fixture.Calls);
		var text = File.ReadAllText(fixture.Configuration);
		Assert.Contains("\"/opt/staged application/.certificates/example.pem\"", text);
		Assert.DoesNotContain(fixture.Root, text);
		Assert.False(File.Exists(fixture.Link));
	}

	[Theory]
	[InlineData(false)]
	[InlineData(true)]
	public async Task CustomHooksKeepWebActivationBetweenMainAndPostAsync(bool failMain)
	{
		using var fixture = new ScriptFixture();
		var package = fixture.CreatePackage(true);
		package.Variables["preinstalled"] = fixture.Write("pre.sh", "printf 'pre\\n' >> \"$MOCK_LOG\"");
		package.Variables["installed"] = "text:printf 'main\\n' >> \"$MOCK_LOG\"" + (failMain ? "; exit 19" : "");
		package.Variables["postinstalled"] = fixture.Write("post.sh", "printf 'post\\n' >> \"$MOCK_LOG\"");
		package.Scriptor.Script();
		var result = await fixture.RunAsync(package.Scripts.Delivered + "\n" + package.Scripts.Installed, "1");
		var calls = fixture.Calls;
		Assert.True(calls.IndexOf("pre", StringComparison.Ordinal) < calls.IndexOf("main", StringComparison.Ordinal));
		if(failMain)
		{
			Assert.Equal(19, result.Code);
			Assert.DoesNotContain("nginx", calls);
			Assert.DoesNotContain("post", calls);
		}
		else
		{
			Assert.Equal(0, result.Code);
			Assert.True(calls.IndexOf("main", StringComparison.Ordinal) < calls.IndexOf("nginx -t", StringComparison.Ordinal));
			Assert.True(calls.IndexOf("reload", StringComparison.Ordinal) < calls.IndexOf("post", StringComparison.Ordinal));
		}
	}

	[Fact]
	public async Task DisabledActivationPreservesAnExistingOrdinaryLoadingFileAsync()
	{
		using var fixture = new ScriptFixture();
		File.WriteAllText(fixture.Link, "external");
		var result = await fixture.RunAsync(fixture.Scripts.Delivered + "\n" + fixture.Scripts.Activate, "0");
		Assert.Equal(0, result.Code);
		Assert.Equal("external", File.ReadAllText(fixture.Link));
		Assert.Empty(fixture.Calls);
	}

	[Theory]
	[InlineData("activating")]
	[InlineData("deactivating")]
	[InlineData("")]
	public async Task UncertainServiceStateFailsWithoutReloadingAsync(string state)
	{
		using var fixture = new ScriptFixture();
		var result = await fixture.RunAsync(fixture.Scripts.Activate, "1", state: state);
		Assert.NotEqual(0, result.Code);
		Assert.DoesNotContain("reload", fixture.Calls);
	}


	[Theory]
	[InlineData(false)]
	[InlineData(true)]
	public async Task MissingNginxFailsInstallationButOnlyWarnsDuringRemovalAsync(bool removing)
	{
		using var fixture = new ScriptFixture();
		var script = "command() { [ \"$2\" != nginx ]; }\n" +
			(removing ? fixture.Scripts.Deactivate + "\n" + fixture.Scripts.Cleanup : fixture.Scripts.Activate);
		var result = await fixture.RunAsync(script, "1");
		Assert.Equal(removing, result.Code == 0);
		Assert.NotEmpty(result.Output);
		Assert.Empty(fixture.Calls);
		Assert.Equal(!removing, File.Exists(fixture.Configuration));
	}

	[Fact]
	public async Task UnlinkFailureIsNotTreatedAsANginxWarningAsync()
	{
		using var fixture = new ScriptFixture();
		var script = $"ln -s -- {ScriptFixture.Quote(fixture.Configuration)} {ScriptFixture.Quote(fixture.Link)}\n" +
			"rm() { return 29; }\n" + fixture.Scripts.Deactivate + "\n" + fixture.Scripts.Cleanup;
		var result = await fixture.RunAsync(script, "1");
		Assert.Equal(29, result.Code);
		Assert.True(File.Exists(fixture.Configuration));
		Assert.Empty(fixture.Calls);
	}


	private sealed class ScriptFixture : IDisposable
	{
		private readonly MigrationTestDirectory _files = new();

		internal ScriptFixture()
		{
			this.Root = Directory.CreateDirectory(Path.Combine(_files.Path, "installed application")).FullName;
			this.SystemDirectory = Directory.CreateDirectory(Path.Combine(_files.Path, "mock-nginx")).FullName;
			Directory.CreateDirectory(Path.Combine(this.SystemDirectory, "conf.d"));
			Directory.CreateDirectory(Path.Combine(this.Root, ".web/nginx"));
			this.Result = NginxConfiguratorTest.Configure(_files, "[api]\nbind!secure=https://*\nserver=http://app\nnginx:proxy_set_header!X-Remote=$remote_addr");
			File.WriteAllText(this.Configuration, this.Result.Files[0].Content.Render("/opt/default"), new UTF8Encoding(false));
			this.Scripts = Installation.CreateScripts(this.CreatePackage(true));
		}

		internal string Write(string name, string value) => _files.Write(name, value);

		internal string Root { get; }
		internal string SystemDirectory { get; }
		internal string Configuration => Path.Combine(this.Root, ".web/nginx/example.conf");
		internal string Link => Path.Combine(this.SystemDirectory, "conf.d/example.conf");
		internal string Other => Path.Combine(_files.Path, "other.conf");
		internal string Calls => File.Exists(Path.Combine(_files.Path, "calls")) ? File.ReadAllText(Path.Combine(_files.Path, "calls")) : "";
		internal Configurator.Result Result { get; }
		internal Installation.Scripts Scripts { get; }

		internal Package CreatePackage(bool web)
		{
			var package = new Package.Tar("example", null, new Version(1, 0), Platform.Linux, Architecture.X64, new Variables(new Dictionary<string, string> { ["source"] = _files.Path, ["daemon"] = "none" }))
			{
				InstallPath = "/opt/default",
				Web = web ? this.Result : null,
			};
			return package;
		}

		internal async Task<(int Code, string Output)> RunAsync(string script, string activation, string state = "active", string failure = "", bool staged = false)
		{
			var bin = Directory.CreateDirectory(Path.Combine(_files.Path, "bin")).FullName;
			File.WriteAllText(Path.Combine(bin, "nginx"), """
				#!/bin/sh
				printf 'nginx %s\n' "$*" >> "$MOCK_LOG"
				if [ "$1" = -t ] && [ "$MOCK_FAILURE" = test ]; then exit 7; fi
				if [ "$1" = -T ] && [ "$MOCK_FAILURE" != include ]; then printf '# configuration file %s:\n' "$MOCK_LINK"; fi
				""".ReplaceLineEndings("\n") + "\n");
			File.WriteAllText(Path.Combine(bin, "systemctl"), """
				#!/bin/sh
				printf 'systemctl %s\n' "$*" >> "$MOCK_LOG"
				case "$*" in
					*LoadState*) printf '%s\n' loaded;;
					*ActiveState*) if [ "$MOCK_FAILURE" = state ]; then exit 9; fi; printf '%s\n' "$MOCK_STATE";;
					reload*) if [ "$MOCK_FAILURE" = reload ]; then exit 11; fi;;
				esac
				""".ReplaceLineEndings("\n") + "\n");
			script = script.Replace("/etc/nginx", ShellPath(this.SystemDirectory), StringComparison.Ordinal);
			Assert.DoesNotContain("/etc/nginx", script);
			var path = Path.Combine(_files.Path, "execute.sh");
			File.WriteAllText(path, "set -e\nexport PATH=" + Quote(bin) + ":$PATH\nchmod +x " + Quote(Path.Combine(bin, "nginx")) + " " + Quote(Path.Combine(bin, "systemctl")) + "\n" + script.ReplaceLineEndings("\n") + "\n", new UTF8Encoding(false));
			var bash = OperatingSystem.IsWindows() ? @"C:\Program Files\Git\bin\bash.exe" : "/bin/sh";
			Assert.True(File.Exists(bash));
			var start = new ProcessStartInfo(bash) { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true };
			start.ArgumentList.Add(ShellPath(path));
			start.Environment["INSTALL_PATH"] = staged ? "/opt/staged application" : ShellPath(this.Root);
			start.Environment["TARGET"] = ShellPath(this.Root);
			start.Environment["DESTDIR"] = staged ? ShellPath(_files.Path) : "";
			start.Environment["MOCK_LOG"] = ShellPath(Path.Combine(_files.Path, "calls"));
			start.Environment["MOCK_LINK"] = ShellPath(this.Link);
			start.Environment["MOCK_STATE"] = state;
			start.Environment["MOCK_FAILURE"] = failure;
			start.Environment["MSYS"] = "winsymlinks:nativestrict";

			if(activation == null)
				start.Environment.Remove("HOSTER_WEB_ACTIVATION");
			else
				start.Environment["HOSTER_WEB_ACTIVATION"] = activation;

			using var process = Process.Start(start);
			var output = process.StandardOutput.ReadToEndAsync();
			var error = process.StandardError.ReadToEndAsync();
			using var timeout = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
			timeout.CancelAfter(TimeSpan.FromSeconds(15));
			await process.WaitForExitAsync(timeout.Token);
			return (process.ExitCode, await output + await error);
		}

		internal static string ShellPath(string path) => OperatingSystem.IsWindows() ? "/" + char.ToLowerInvariant(path[0]) + path[2..].Replace('\\', '/') : path;
		internal static string Quote(string path) => "'" + ShellPath(path).Replace("'", "'\"'\"'") + "'";
		public void Dispose() => _files.Dispose();
	}
}
