using System;
using System.IO;
using System.Text;
using System.Diagnostics;
using System.ComponentModel;
using System.Collections.Generic;

using Microsoft.Win32;
using Xunit;

namespace Zongsoft.Tools.Packager.Tests.Web;

internal sealed record ShellEnvironment(string Bash, string Cygpath)
{
	private static readonly Lazy<ShellEnvironment> _current = new(Find);
	private static readonly Lazy<string> _unavailable = new(() => CheckSupport(_current.Value));

	internal static ShellEnvironment GetRequired()
	{
		var reason = _unavailable.Value;

		if(reason != null)
			Assert.Skip(reason);

		return _current.Value;
	}

	internal static ShellEnvironment FindWindows(IEnumerable<string> directories)
	{
		foreach(var directory in directories)
		{
			if(string.IsNullOrWhiteSpace(directory) || !Directory.Exists(directory.Trim().Trim('"')))
				continue;

			for(var root = new DirectoryInfo(directory.Trim().Trim('"')); root != null; root = root.Parent)
			{
				var bash = Path.Combine(root.FullName, "bin", "bash.exe");
				var cygpath = Path.Combine(root.FullName, "usr", "bin", "cygpath.exe");

				if(File.Exists(bash) && File.Exists(cygpath))
					return new(bash, cygpath);
			}
		}

		return null;
	}

	private static ShellEnvironment Find()
	{
		if(!OperatingSystem.IsWindows())
			return File.Exists("/bin/sh") ? new("/bin/sh", null) : null;

		var configured = Environment.GetEnvironmentVariable("PACKAGER_TEST_GIT");
		if(!string.IsNullOrWhiteSpace(configured))
			return FindWindows([configured]);

		var tools = FindWindows((Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries));
		if(tools != null)
			return tools;

		var directories = new List<string>();
		foreach(var key in new[] { @"HKEY_CURRENT_USER\Software\GitForWindows", @"HKEY_LOCAL_MACHINE\Software\GitForWindows" })
		{
			try
			{
				directories.Add(Registry.GetValue(key, "InstallPath", null) as string);
			}
			catch(Exception error) when(error is IOException or UnauthorizedAccessException or System.Security.SecurityException)
			{
				// 注册信息不可访问时继续检查常见安装目录。
			}
		}

		directories.Add(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Git"));
		directories.Add(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Git"));
		directories.Add(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "Git"));

		return FindWindows(directories);
	}

	internal static string CheckSupport(ShellEnvironment tools)
	{
		if(tools == null)
			return OperatingSystem.IsWindows() ?
				"Web shell tests require Git for Windows with Bash and cygpath. Set PACKAGER_TEST_GIT to its directory or add Git to PATH." :
				"Web shell tests require /bin/sh.";

		try
		{
			using var files = new MigrationTestDirectory();
			var start = CreateStart(tools.Bash);

			start.ArgumentList.Add("-c");
			start.ArgumentList.Add("""
				set -e
				for utility in chmod ln readlink mktemp sed rm mv; do
					command -v "$utility" >/dev/null 2>&1
				done
				readlink -m -- "$PACKAGER_SHELL_PROBE" >/dev/null
				printf probe > "$PACKAGER_SHELL_PROBE/target"
				ln -s -- "$PACKAGER_SHELL_PROBE/target" "$PACKAGER_SHELL_PROBE/link"
				[ -L "$PACKAGER_SHELL_PROBE/link" ]
				readlink -m -- "$PACKAGER_SHELL_PROBE/link" >/dev/null
				chmod 0644 "$PACKAGER_SHELL_PROBE/target"
				""".ReplaceLineEndings("\n"));

			start.Environment["MSYS"] = "winsymlinks:nativestrict";
			start.Environment["PACKAGER_SHELL_PROBE"] = OperatingSystem.IsWindows() ? tools.ConvertPath(files.Path, "-u") : files.Path;

			var result = Run(start);
			return result.Code == 0 ? null : "Web shell tests require GNU-compatible utilities and symbolic-link support: " + result.Error;
		}
		catch(Exception error) when(error is Win32Exception or IOException or UnauthorizedAccessException or TimeoutException)
		{
			return "Web shell test environment is unavailable: " + error.Message;
		}
	}

	internal string ConvertPath(string path, string format)
	{
		var start = CreateStart(this.Cygpath);
		start.ArgumentList.Add(format);
		start.ArgumentList.Add("--");
		start.ArgumentList.Add(path);
		var result = Run(start);

		if(result.Code != 0)
			throw new IOException("cygpath could not convert the test path: " + result.Error);

		return result.Output.TrimEnd('\r', '\n');
	}

	private static ProcessStartInfo CreateStart(string executable) => new(executable)
	{
		UseShellExecute = false,
		RedirectStandardOutput = true,
		RedirectStandardError = true,
		StandardOutputEncoding = Encoding.UTF8,
		StandardErrorEncoding = Encoding.UTF8,
	};

	private static (int Code, string Output, string Error) Run(ProcessStartInfo start)
	{
		using var process = Process.Start(start);
		var output = process.StandardOutput.ReadToEndAsync();
		var error = process.StandardError.ReadToEndAsync();

		if(!process.WaitForExit(15000))
		{
			process.Kill(true);
			process.WaitForExit();
			throw new TimeoutException("The shell utility did not finish within 15 seconds.");
		}

		return (process.ExitCode, output.GetAwaiter().GetResult(), error.GetAwaiter().GetResult());
	}
}
