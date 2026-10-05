using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Collections.Generic;

using Xunit;

using Zongsoft.Tools.Containerizer.Protocol;
using Zongsoft.Tools.Containerizer.Execution;

namespace Zongsoft.Tools.Containerizer.Executor.Tests;

public sealed class StoragePathsTests : IDisposable
{
	private readonly string _root = Path.Combine(Path.GetTempPath(), "containerizer-storage-" + Guid.NewGuid().ToString("N"));
	private readonly InstallationStore _store;
	private readonly Runner _runner = new();
	private readonly DockerHost _host;

	public StoragePathsTests()
	{
		_store = new(Path.Combine(_root, "lib"), Path.Combine(_root, "log"), Path.Combine(_root, "cache"), Path.Combine(_root, "run"));
		_host = new(_store, _runner);
	}

	public void Dispose()
	{
		if(Directory.Exists(_root))
			Directory.Delete(_root, true);
	}

	[Theory]
	[InlineData("/var/lib/containerizer/data/example")]
	[InlineData("/var/lib/containerizer/data/example/redis/data")]
	[InlineData("/mnt/application-data")]
	[InlineData("/srv/example")]
	public void DataPathsAllowOwnNamespaceAndExplicitExternalDirectories(string path) => _host.ValidateDataPath(path, "example");

	[Theory]
	[InlineData("/var")]
	[InlineData("/var/lib")]
	[InlineData("/var/lib/containerizer")]
	[InlineData("/var/lib/containerizer/apps/example")]
	[InlineData("//var/lib/containerizer/apps/example")]
	[InlineData("/var/lib/containerizer//data/other")]
	[InlineData("/var/lib/containerizer/data/")]
	[InlineData("/var/lib/containerizer/data")]
	[InlineData("/var/lib/containerizer/data/other")]
	[InlineData("/var/lib/containerizer/data/example-other")]
	[InlineData("/var/lib/containerizer/data/example/../other")]
	[InlineData("/var/log")]
	[InlineData("/var/log/containerizer/example")]
	[InlineData("/var/cache/containerizer/example")]
	[InlineData("/run/containerizer")]
	[InlineData("/etc/application")]
	public void DataPathsRejectSystemAndOtherApplicationNamespaces(string path) => Assert.Throws<ContainerizationException>(() => _host.ValidateDataPath(path, "example"));

	[Fact]
	public async Task OrdinaryUninstallPreservesOwnedLogsAndCacheAndPurgeRemovesOnlyItsOwnAssets()
	{
		var installation = this.Register("example");
		var other = this.Register("other");
		var log = Path.Combine(_store.GetLogPath(installation.Name), "migration.log");
		var cache = Path.Combine(_store.GetCachePath(installation.Name), "package");
		File.WriteAllText(log, "attempt");
		File.WriteAllText(cache, "package");
		var manager = new InstallationManager(_store, _host);

		await manager.ExecuteAsync(ExecutorArguments.Parse(["uninstall", "--name", installation.Name]), CancellationToken.None);
		Assert.True(File.Exists(log));
		Assert.True(File.Exists(cache));
		Assert.Equal("Uninstalled", _store.Load(installation.Name).Status);

		await manager.ExecuteAsync(ExecutorArguments.Parse(["uninstall", "--name", installation.Name, "--purge"]), CancellationToken.None);
		Assert.False(Directory.Exists(_store.GetLogPath(installation.Name)));
		Assert.False(Directory.Exists(_store.GetCachePath(installation.Name)));
		Assert.False(Directory.Exists(_store.GetApplicationPath(installation.Name)));
		Assert.True(Directory.Exists(_store.GetLogPath(other.Name)));
		Assert.NotNull(_store.Load(other.Name));
		Assert.True(File.Exists(Path.Combine(_store.RuntimeRoot, installation.Name + ".lock")));
	}

	[Fact]
	public async Task InvalidOwnershipRetainsRegistrationAndPurgeCanBeRetried()
	{
		var installation = this.Register("example");
		var cache = _store.GetCachePath(installation.Name);
		var marker = Path.Combine(cache, ".containerizer-owner");
		var identity = File.ReadAllText(marker);
		File.WriteAllText(marker, "someone-else");
		var manager = new InstallationManager(_store, _host);

		await Assert.ThrowsAsync<ContainerizationException>(() => manager.ExecuteAsync(ExecutorArguments.Parse(["uninstall", "--name", installation.Name, "--purge"]), CancellationToken.None));
		Assert.Equal("CleanupFailed", _store.Load(installation.Name).Status);
		Assert.True(Directory.Exists(cache));

		File.WriteAllText(marker, identity);
		await manager.ExecuteAsync(ExecutorArguments.Parse(["uninstall", "--name", installation.Name, "--purge"]), CancellationToken.None);
		Assert.False(Directory.Exists(cache));
		Assert.Null(_store.Load(installation.Name, false));
	}

	[Fact]
	public async Task PurgeRejectsMountedCacheAndRetainsOwnershipMarker()
	{
		var installation = this.Register("example");
		var cache = _store.GetCachePath(installation.Name);
		_runner.Mount = cache;
		await Assert.ThrowsAsync<ContainerizationException>(() => _host.UninstallAsync(installation, true, CancellationToken.None));
		Assert.True(File.Exists(Path.Combine(cache, ".containerizer-owner")));
		Assert.NotNull(_store.Load(installation.Name));
	}

	[Fact]
	public void DeletedCacheCanBeRecreatedWithoutLosingItsOwnership()
	{
		var installation = this.Register("example");
		var cache = _store.GetCachePath(installation.Name);
		var token = Assert.Single(installation.Directories, item => item.Path == cache).Token;
		Directory.Delete(cache, true);

		using(var hostLock = _store.HostLock())
			_host.PrepareOwnedDirectory(installation, cache);

		Assert.Equal(installation.Name + "/" + token, File.ReadAllText(Path.Combine(cache, ".containerizer-owner")));
		Assert.Equal(token, Assert.Single(_store.Load(installation.Name).Directories, item => item.Path == cache).Token);
	}

	[Fact]
	public async Task PurgeRejectsLinksInsideCache()
	{
		if(OperatingSystem.IsWindows())
			return;

		var installation = this.Register("example");
		var cache = _store.GetCachePath(installation.Name);
		var target = Path.Combine(_root, "outside");
		Directory.CreateDirectory(target);
		var link = Path.Combine(cache, "linked");
		Directory.CreateSymbolicLink(link, target);

		try
		{
			await Assert.ThrowsAsync<ContainerizationException>(() => _host.UninstallAsync(installation, true, CancellationToken.None));
			Assert.True(Directory.Exists(target));
			Assert.True(File.Exists(Path.Combine(cache, ".containerizer-owner")));
		}
		finally
		{
			Directory.Delete(link);
		}
	}

	private Installation Register(string name)
	{
		var installation = new Installation { Name = name, DataRoot = Path.Combine(_root, "absent-data", name) };
		_store.Save(installation);

		using(var hostLock = _store.HostLock())
		{
			_host.PrepareOwnedDirectory(installation, _store.GetLogPath(name));
			_host.PrepareOwnedDirectory(installation, _store.GetCachePath(name));
		}

		return installation;
	}

	private sealed class Runner : IProcessRunner
	{
		public string Mount { get; set; }
		public Task<ProcessResult> RunAsync(string executable, IReadOnlyList<string> arguments, string directory, CancellationToken cancellation, int timeoutSeconds = 900)
		{
			var output = executable == "findmnt" ? JsonSerializer.Serialize(new { filesystems = this.Mount == null ? Array.Empty<object>() : [new { target = this.Mount }] }) : "";
			return Task.FromResult(new ProcessResult(0, output, ""));
		}
	}
}
