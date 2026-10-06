using System;
using System.IO;
using System.Linq;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using System.Collections.Generic;

using Xunit;

using Zongsoft.Tools.Containerizer.Protocol;
using Zongsoft.Tools.Containerizer.Execution;

namespace Zongsoft.Tools.Containerizer.Executor.Tests;

public sealed class InstallationManagerTests : IDisposable
{
	private readonly string _root = Path.Combine(Path.GetTempPath(), "containerizer-lifecycle-" + Guid.NewGuid().ToString("N"));
	private readonly InstallationStore _store;
	private readonly FakeHost _host = new();
	private readonly InstallationManager _lifecycle;
	public InstallationManagerTests()
	{
		Directory.CreateDirectory(_root);
		_store = new InstallationStore(Path.Combine(_root, "registry"), Path.Combine(_root, "logs"), Path.Combine(_root, "cache"), Path.Combine(_root, "run"));
		_lifecycle = new InstallationManager(_store, _host);
	}
	public void Dispose() => Directory.Delete(_root, true);

	[Theory]
	[InlineData("zh-CN", "PrepareBootstrap", "准备容器引擎及运行依赖")]
	[InlineData("zh-CN", "ApplyMigration", "执行数据升迁")]
	[InlineData("zh-CN", "CheckHealth", "检查服务健康状态")]
	[InlineData("en-US", "PrepareBootstrap", "Prepare container engine and runtime dependencies")]
	[InlineData("en-US", "ApplyMigration", "Apply data migrations")]
	[InlineData("en-US", "CheckHealth", "Check service health")]
	[InlineData("zh-CN", "UnknownPhase", "UnknownPhase")]
	public void PhaseDisplayUsesTheInterfaceLanguage(string culture, string phase, string expected)
	{
		var original = CultureInfo.CurrentUICulture;

		try
		{
			CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo(culture);
			Assert.Equal(expected, InstallationManager.GetPhaseText(phase));
		}
		finally
		{
			CultureInfo.CurrentUICulture = original;
		}
	}

	[Theory]
	[InlineData("upgrade", "--bundle", "input.tar.gz", "--name", "example")]
	[InlineData("install")]
	[InlineData("install", "one", "two")]
	[InlineData("recover", "--name", "example", "--retry-migration", "")]
	[InlineData("recover", "--name", "example", "--retry-migration", "1.0", "--retry-migration", "1.1")]
	[InlineData("start")]
	[InlineData("list", "--name", "example")]
	public void InvalidArgumentsFailBeforeAnyHostAction(params string[] arguments) => Assert.Equal(2, Assert.Throws<ContainerizationException>(() => ExecutorArguments.Parse(arguments)).Code);

	[Theory]
	[InlineData("missing")]
	[InlineData("no-health")]
	[InlineData("unhealthy")]
	public async Task HealthFailuresIdentifyTheServiceAndRetainTheHealthExitCode(string failure)
	{
		using var bundle = DeliveryBundle.Open(this.CreateBundle("1.0", false));
		var service = bundle.Plan.Services[0];
		service.Health.StartSeconds = service.Health.Retries = service.Health.TimeoutSeconds = 0;
		var host = new DockerHost(_store, new HealthRunner(failure));

		var exception = await Assert.ThrowsAsync<ContainerizationException>(() => host.HealthyAsync(bundle, service, TestContext.Current.CancellationToken));

		Assert.Equal(6, exception.Code);
		Assert.Contains(service.Id, exception.Message);
		if(failure == "unhealthy")
			Assert.Contains("redis-container-id", exception.Message);
	}

	[Fact]
	public async Task BootstrapCacheOwnershipSurvivesLaterTransactionSaves()
	{
		using var bundle = DeliveryBundle.Open(this.CreateBundle("1.0", false));
		var package = Path.Combine(bundle.Directory, "images", "redis.tar");
		bundle.Plan.Bootstrap.Packages.Add(new() { Path = "images/redis.tar", Hash = Files.Hash(package), Length = new FileInfo(package).Length });
		var installation = new Installation { Name = bundle.Plan.Name, DataRoot = bundle.Plan.DataRoot };
		_store.Save(installation);
		await new DockerHost(_store, new BootstrapRunner()).BootstrapAsync(bundle, installation, CancellationToken.None);
		installation.Status = "Preparing";
		_store.Save(installation);

		var cache = _store.GetCachePath(installation.Name);
		Assert.True(Assert.Single(_store.Load(installation.Name).Directories, item => item.Path == cache).Created);
		Assert.True(File.Exists(Path.Combine(cache, bundle.Id, "bootstrap", "apt", "redis.tar")));
		Assert.True(File.Exists(Path.Combine(cache, ".containerizer-owner")));
	}

	[Fact]
	public async Task PrepareDoesNotStopMigrateOrCommit()
	{
		var path = this.CreateBundle("1.0", true);
		await this.Run("prepare", path, "--name", "example");

		var state = _store.Load("example");
		Assert.Equal("Prepared", state.Status);
		Assert.Null(state.Current);
		Assert.DoesNotContain("stop", _host.Calls);
		Assert.Equal(0, _host.Applies);
		Assert.Equal(7, (await Assert.ThrowsAsync<ContainerizationException>(() => this.Run("start", "--name", "example"))).Code);
		Assert.Equal(7, (await Assert.ThrowsAsync<ContainerizationException>(() => this.Run("recover", "--name", "example"))).Code);
	}

	[Theory]
	[InlineData("zh-CN")]
	[InlineData("en-US")]
	public async Task NoStartCommitsOnlyAfterExplicitStartAndReusesMigrationSuccess(string culture)
	{
		var original = CultureInfo.CurrentUICulture;

		try
		{
			CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo(culture);
			var path = this.CreateBundle("1.0", true);
			await this.Run("install", path, "--no-start");

			var state = _store.Load("example");
			Assert.Equal("ReadyToStart", state.Status);
			Assert.Equal("ReadyToStart", state.Pending.Phase);
			Assert.Contains("PrepareBootstrap", state.Pending.Completed);
			Assert.Contains(state.Pending.Events, item => item.Phase == "ApplyMigration" && item.Result == "Succeeded");
			Assert.True(state.Maintenance);
			Assert.Null(state.Current);
			Assert.Equal(1, _host.Applies);
			Assert.DoesNotContain("start:web", _host.Calls);

			Directory.Delete(path, true);
			await this.Run("start", "--name", "example");

			state = _store.Load("example");
			Assert.Equal("Installed", state.Status);
			Assert.False(state.Maintenance);
			Assert.Equal("1.0", state.CurrentVersion);
			Assert.Equal(1, _host.Applies);
			Assert.Contains("start:web", _host.Calls);
		}
		finally
		{
			CultureInfo.CurrentUICulture = original;
		}
	}

	[Fact]
	public async Task FailedMigrationRequiresExplicitRetryAndRetainsAttempts()
	{
		_host.FailMigration = true;
		var path = this.CreateBundle("1.0", true);
		await Assert.ThrowsAsync<ContainerizationException>(() => this.Run("install", path));
		Assert.Equal(1, _host.Applies);
		await Assert.ThrowsAsync<ContainerizationException>(() => this.Run("install", path));
		await Assert.ThrowsAsync<ContainerizationException>(() => this.Run("recover", "--name", "example"));
		Assert.Equal(1, _host.Applies);
		_host.FailMigration = false;
		await this.Run("recover", "--name", "example", "--retry-migration", "1.0");
		var state = _store.Load("example");
		Assert.Equal("ReadyToStart", state.Status);
		Assert.True(state.Maintenance);
		Assert.Equal(2, Assert.Single(state.Migrations.Values).Attempts.Count);
		Assert.DoesNotContain("start:web", _host.Calls);
		Assert.Equal(2, _host.Applies);
		Assert.All(_host.MigrationPaths, path => Assert.Equal(Path.Combine(_store.GetApplicationPath("example"), "migrations", "1.0"), path));
		var attempts = Assert.Single(state.Migrations.Values).Attempts;
		Assert.All(attempts, attempt => Assert.Equal(_store.GetLogPath("example"), Path.GetDirectoryName(attempt.Log)));
		Assert.Equal(2, attempts.Select(attempt => attempt.Log).Distinct().Count());
	}

	[Fact]
	public async Task InterruptedStartedMigrationCannotAutomaticallyRunAgain()
	{
		var path = this.CreateBundle("1.0", true);
		await this.Run("install", path, "--no-start");
		var state = _store.Load("example");
		state.Pending.Failed = true;
		Assert.Single(state.Migrations.Values).Status = "Started";
		_store.Save(state);
		await Assert.ThrowsAsync<ContainerizationException>(() => this.Run("recover", "--name", "example"));
		Assert.Equal(1, _host.Applies);
	}

	[Fact]
	public async Task RestartDefaultsToApplicationButAllowsExplicitInfrastructure()
	{
		await this.Run("install", this.CreateBundle("1.0", false));
		_host.Calls.Clear();
		await this.Run("restart", "--name", "example");
		Assert.Equal(["restart:web"], _host.Calls);
		await this.Run("restart", "redis", "--name", "example");
		Assert.Contains("restart:redis", _host.Calls);
		await this.Run("stop", "--name", "example");
		_host.Calls.Clear();
		await Assert.ThrowsAsync<ContainerizationException>(() => this.Run("restart", "redis", "--name", "example"));
		Assert.Empty(_host.Calls);
	}

	[Fact]
	public async Task UpgradeConflictDoesNotStopOldDeployment()
	{
		await this.Run("install", this.CreateBundle("1.0", false));
		_host.Calls.Clear();
		var next = this.CreateBundle("2.0", false, true);
		await Assert.ThrowsAsync<ContainerizationException>(() => this.Run("upgrade", next, "--name", "example"));
		Assert.DoesNotContain("stop", _host.Calls);
		Assert.Equal("1.0", _store.Load("example").CurrentVersion);
	}

	[Fact]
	public async Task OrdinaryUninstallRetainsHistoryAndPurgeRemovesRegistration()
	{
		await this.Run("install", this.CreateBundle("1.0", true));
		await this.Run("uninstall", "--name", "example");
		Assert.Equal("Uninstalled", _store.Load("example").Status);
		Assert.Single(_store.Load("example").Migrations);
		await this.Run("uninstall", "--name", "example", "--purge");
		Assert.Empty(_store.List());
		Assert.True(File.Exists(Path.Combine(_store.RuntimeRoot, "example.lock")));
	}

	[Fact]
	public async Task ImagePreparationFailureKeepsCurrentServicesRunning()
	{
		await this.Run("install", this.CreateBundle("1.0", false));
		_host.Calls.Clear();
		_host.FailImages = true;
		await Assert.ThrowsAsync<ContainerizationException>(() => this.Run("upgrade", this.CreateBundle("2.0", false), "--name", "example"));
		var state = _store.Load("example");
		Assert.Equal("1.0", state.CurrentVersion);
		Assert.False(state.Maintenance);
		Assert.True(state.Pending.Failed);
		Assert.DoesNotContain("stop", _host.Calls);
		_host.FailVerify = true;
		await Assert.ThrowsAsync<ContainerizationException>(() => this.Run("recover", "--name", "example"));
		Assert.True(_store.Load("example").Pending.Failed);
	}

	[Fact]
	public async Task SameCompletedBundleDoesNotStopOrRecreateServices()
	{
		var input = this.CreateBundle("1.0", true);
		await this.Run("install", input);
		_host.Calls.Clear();
		await this.Run("install", input);
		Assert.Empty(_host.Calls);
		Assert.Equal(1, _host.Applies);
	}

	[Fact]
	public async Task PurgeCanResumeAfterReleaseAssetsHaveBeenRemoved()
	{
		await this.Run("install", this.CreateBundle("1.0", true));
		_host.FailFinalPurge = true;
		await Assert.ThrowsAsync<ContainerizationException>(() => this.Run("uninstall", "--name", "example", "--purge"));
		Assert.True(_store.Load("example").PurgeResourcesCompleted);
		Assert.Equal(1, _host.Uninstalls);
		_host.FailFinalPurge = false;
		await this.Run("uninstall", "--name", "example", "--purge");
		Assert.Equal(1, _host.Uninstalls);
		Assert.Empty(_store.List());
	}

	[Fact]
	public async Task ConfigurationAssetsStayInTheVerifiedReleaseAndUpgradeWithoutRuntimeCopies()
	{
		await this.Run("install", this.CreateBundle("1.0", false, configuration: "original"));
		var state = _store.Load("example");
		var current = Path.Combine(_store.GetApplicationPath("example"), "releases", state.Current, "assets");
		await this.Run("upgrade", this.CreateBundle("2.0", false, configuration: "incoming"), "--name", "example");
		state = _store.Load("example");
		var next = Path.Combine(_store.GetApplicationPath("example"), "releases", state.Current, "assets");
		Assert.Equal("2.0", state.CurrentVersion);
		Assert.Equal("original", File.ReadAllText(Path.Combine(current, "config/nginx/site.conf")));
		Assert.Equal("incoming", File.ReadAllText(Path.Combine(next, "config/nginx/site.conf")));
		Assert.False(Directory.Exists(Path.Combine(Path.GetDirectoryName(next), "runtime")));
		Assert.False(File.Exists(Path.Combine(next, "compose.env")));

		if(!OperatingSystem.IsWindows())
			Assert.Equal((UnixFileMode)292, File.GetUnixFileMode(Path.Combine(next, "config/nginx/site.conf")));

		using var verified = DeliveryBundle.Open(next);
		if(!OperatingSystem.IsWindows())
			File.SetUnixFileMode(Path.Combine(next, "config/nginx/site.conf"), (UnixFileMode)420);

		File.AppendAllText(Path.Combine(next, "config/nginx/site.conf"), "tampered");
		Assert.Equal(4, Assert.Throws<ContainerizationException>(() => DeliveryBundle.Open(next)).Code);
	}

	[Fact]
	public void ApplicationLockSurvivesRegistryRemoval()
	{
		using var held = _store.Lock("example");
		Directory.CreateDirectory(_store.GetApplicationPath("example"));
		Directory.Delete(_store.GetApplicationPath("example"));
		Assert.Equal(8, Assert.Throws<ContainerizationException>(() => _store.Lock("example")).Code);
	}

	[Fact]
	public void TamperedBundleIsRejectedBeforeStaging()
	{
		var path = this.CreateBundle("1.0", false);
		File.AppendAllText(Path.Combine(path, "compose.yaml"), "changed");
		Assert.Equal(4, Assert.Throws<ContainerizationException>(() => DeliveryBundle.Open(path)).Code);
		Assert.Empty(_store.List());
	}

	private Task Run(params string[] arguments) => _lifecycle.ExecuteAsync(ExecutorArguments.Parse(arguments), CancellationToken.None);

	[Theory]
	[InlineData(false, false)]
	[InlineData(true, false)]
	[InlineData(false, true)]
	[InlineData(true, true)]
	public async Task OfflineImportValidatesImageIdentityBeforePublishingDeliveryTags(bool wrongArchitecture, bool cached)
	{
		using var bundle = DeliveryBundle.Open(this.CreateBundle("1.0", false));
		var runner = new ImportedImageRunner(wrongArchitecture, cached);
		var host = new DockerHost(_store, runner);

		if(wrongArchitecture)
		{
			Assert.Equal(4, (await Assert.ThrowsAsync<ContainerizationException>(() => host.ImagesAsync(bundle, CancellationToken.None))).Code);
			Assert.Empty(runner.PublishedTags);
		}
		else
		{
			await host.ImagesAsync(bundle, CancellationToken.None);
			Assert.Equal(bundle.Plan.Services.Select(service => service.Image.Tag), runner.PublishedTags);
		}

		Assert.Equal(cached ? 0 : 1, runner.Loads);
	}

	[Fact]
	public async Task InfrastructureRestartPreservesContainerAndUninstallRemovesAnonymousVolumes()
	{
		using var bundle = DeliveryBundle.Open(this.CreateBundle("1.0", false));
		var runner = new StorageRunner();
		var host = new DockerHost(_store, runner);
		await host.StartAsync(bundle, bundle.Plan.Services.Single(service => service.Id == "redis"), CancellationToken.None);
		Assert.Contains("--no-recreate", Assert.Single(runner.Calls, arguments => arguments[0] == "compose"));
		Assert.DoesNotContain("--env-file", Assert.Single(runner.Calls, arguments => arguments[0] == "compose"));
		var installation = new Installation { Name = "example", DataRoot = Path.Combine(_root, "data") };
		Directory.CreateDirectory(installation.DataRoot);
		File.WriteAllText(Path.Combine(installation.DataRoot, "keep"), "persistent");
		await host.UninstallAsync(installation, false, CancellationToken.None);
		Assert.Equal(new[] { "rm", "-f", "-v", "owned-container" }, Assert.Single(runner.Calls, arguments => arguments[0] == "rm"));
		Assert.Equal("persistent", File.ReadAllText(Path.Combine(installation.DataRoot, "keep")));
		Assert.All(runner.Calls.Where(arguments => arguments[0] == "ps"), arguments => Assert.Contains("label=org.zongsoft.containerizer.name=example", arguments));
	}

	[Theory]
	[InlineData(true)]
	[InlineData(false)]
	public async Task IngressCleansTemporaryVolumesOnlyWhenRecreated(bool recreated)
	{
		using var bundle = DeliveryBundle.Open(this.CreateBundle("1.0", false));
		var service = bundle.Plan.Services.Single(item => item.Id == "web");
		service.Kind = "ingress";
		service.Mounts.Add(new() { Temporary = true, Target = "/data" });
		var runner = new StorageRunner { ReplaceContainer = recreated };
		await new DockerHost(_store, runner).StartAsync(bundle, service, CancellationToken.None);
		var arguments = Assert.Single(runner.Calls, arguments => arguments[0] == "compose");
		Assert.Contains("--renew-anon-volumes", arguments);
		Assert.DoesNotContain("--no-recreate", arguments);

		if(recreated)
			Assert.Equal(new[] { "volume", "rm", new string('a', 64) }, Assert.Single(runner.Calls, call => call[0] == "volume" && call[1] == "rm"));
		else
			Assert.DoesNotContain(runner.Calls, call => call[0] == "volume");
	}

	[Theory]
	[InlineData(0)]
	[InlineData(2)]
	[InlineData(3)]
	public void OtherDeliveryProtocolsAreRejected(int schema)
	{
		var path = this.CreateBundle("1.0", false);
		var file = Path.Combine(path, "containerizer.json");
		var plan = Files.Load(file, ProtocolJson.Default.DeliveryPlan);
		plan.Schema = schema;
		Files.Save(file, plan, ProtocolJson.Default.DeliveryPlan);
		Assert.Equal(3, Assert.Throws<ContainerizationException>(() => DeliveryBundle.Open(path)).Code);
	}

	private sealed class StorageRunner : IProcessRunner
	{
		private bool _started;
		public bool ReplaceContainer { get; set; }
		public List<string[]> Calls { get; } = [];
		public Task<ProcessResult> RunAsync(string executable, IReadOnlyList<string> arguments, string directory, CancellationToken cancellation, int timeoutSeconds = 900)
		{
			this.Calls.Add(arguments.ToArray());
			if(arguments[0] == "compose")
				_started = true;
			var output = arguments[0] switch
			{
				"ps" => _started && this.ReplaceContainer ? "replacement-container\n" : "owned-container\n",
				"inspect" => "[{\"Config\":{\"Labels\":{\"org.zongsoft.containerizer.service\":\"redis\"}},\"Mounts\":[{\"Type\":\"volume\",\"Destination\":\"/data\",\"Name\":\"" + new string('a', 64) + "\"}]}]",
				_ => "",
			};
			return Task.FromResult(new ProcessResult(0, output, ""));
		}
	}

	private string CreateBundle(string version, bool migration, bool changedInfrastructure = false, string configuration = null)
	{
		var directory = Path.Combine(_root, "bundle-" + Guid.NewGuid().ToString("N"));
		Directory.CreateDirectory(directory);
		var plan = new DeliveryPlan { Name = "example", Version = version, Distribution = "debian@13", Architecture = "x64", Project = "containerizer-example-" + Files.HashText("example")[..8], DataRoot = "/var/lib/containerizer/data/example" };

		if(configuration != null)
		{
			Directory.CreateDirectory(Path.Combine(directory, "config/nginx"));
			File.WriteAllText(Path.Combine(directory, "config/nginx/site.conf"), configuration);
		}

		foreach(var file in new[] { "containerizer", "compose.yaml", "install.sh", "uninstall.sh", "images/redis.tar", "images/web.tar" })
		{
			var path = Path.Combine(directory, file);
			Directory.CreateDirectory(Path.GetDirectoryName(path));
			File.WriteAllText(path, file);
		}

		foreach(var id in new[] { "redis", "web" })
			plan.Services.Add(new() { Id = id, Kind = id == "redis" ? "infrastructure" : "application", ConfigurationHash = id == "redis" && changedInfrastructure ? "changed" : "same", Image = new() { Id = "sha256:" + new string('a', 64), Digest = "sha256:" + new string('b', 64), Tag = "containerizer/" + plan.Project + "/" + id + ":locked", Platform = "linux/amd64", Archive = "images/" + id + ".tar" } });

		if(configuration != null)
			plan.Services.Add(new() { Id = "nginx", Kind = "ingress", Image = new() { Id = plan.Services[1].Image.Id, Tag = "containerizer/" + plan.Project + "/nginx:locked", Platform = "linux/amd64", Archive = "images/web.tar" }, Mounts = [new() { Source = "config/nginx/site.conf", Target = "/etc/nginx/conf.d/site.conf", ReadOnly = true }] });

		if(migration)
		{
			Directory.CreateDirectory(Path.Combine(directory, "migration"));
			File.WriteAllText(Path.Combine(directory, "migration/migration.tar.gz"), "opaque migration archive");
			File.WriteAllText(Path.Combine(directory, "migration/migration.sh"), "opaque migration script");
			plan.Migrations.Add(new() { Version = "1.0", Archive = "migration/migration.tar.gz", Script = "migration/migration.sh", Identity = Files.HashText(Files.Hash(Path.Combine(directory, "migration/migration.tar.gz")) + Files.Hash(Path.Combine(directory, "migration/migration.sh"))) });
		}

		plan.Files = Directory.EnumerateFiles(directory, "*", SearchOption.AllDirectories).Select(path => new FileRecord { Path = Path.GetRelativePath(directory, path).Replace('\\', '/'), Hash = Files.Hash(path), Length = new FileInfo(path).Length }).ToList();
		Files.Save(Path.Combine(directory, "containerizer.json"), plan, ProtocolJson.Default.DeliveryPlan);
		Files.Write(Path.Combine(directory, "checksums.sha256"), string.Join('\n', plan.Files.Append(new() { Path = "containerizer.json", Hash = Files.Hash(Path.Combine(directory, "containerizer.json")) }).Select(file => file.Hash + "  " + file.Path)) + "\n", true);
		return directory;
	}

	[Theory]
	[InlineData(false, false)]
	[InlineData(true, false)]
	[InlineData(false, true)]
	public async Task OnlineMirrorsKeepPinnedIdentityAndReuseCachedImages(bool wrongMirror, bool cached)
	{
		using var bundle = DeliveryBundle.Open(this.CreateBundle("1.0", false));
		bundle.Plan.Services.RemoveAt(1);
		var image = bundle.Plan.Services[0].Image;
		image.Mode = "online";
		image.Repository = "docker.io/library/redis";
		var runner = new OnlineImageRunner(image, wrongMirror, cached);
		var sources = new RegistryMirrors { Registries = new() { ["docker.io"] = ["mirror.example.com/docker.io"] } };
		await new DockerHost(_store, runner, sources).ImagesAsync(bundle, TestContext.Current.CancellationToken);
		Assert.Equal(cached ? 0 : wrongMirror ? 2 : 1, runner.Calls.Count(arguments => arguments[0] == "pull"));
		Assert.All(runner.Calls.Where(arguments => arguments[0] == "pull"), arguments => Assert.EndsWith($"@{image.Digest}", arguments[^1], StringComparison.Ordinal));
		Assert.Equal(["tag", image.Id, image.Tag], Assert.Single(runner.Calls, arguments => arguments[0] == "tag"));
		Assert.Equal("docker.io/library/redis", image.Repository);
	}

	private sealed class OnlineImageRunner(ImagePlan image, bool wrongMirror, bool cached) : IProcessRunner
	{
		public List<string[]> Calls { get; } = [];
		private bool _pulled;
		public Task<ProcessResult> RunAsync(string executable, IReadOnlyList<string> arguments, string directory, CancellationToken cancellation, int timeoutSeconds = 900)
		{
			this.Calls.Add(arguments.ToArray());
			if(arguments[0] == "pull")
				_pulled = true;

			if(arguments[0] == "image")
			{
				if(!cached && !_pulled)
					return Task.FromResult(new ProcessResult(1, "", "image missing"));
				var reference = arguments[^1];
				var id = wrongMirror && reference.StartsWith("mirror.example.com", StringComparison.Ordinal) ? $"sha256:{new string('f', 64)}" : image.Id;
				return Task.FromResult(new ProcessResult(0, System.Text.Json.JsonSerializer.Serialize(new[] { new { Id = id, Os = "linux", Architecture = "amd64", RepoDigests = new[] { reference } } }), ""));
			}

			return Task.FromResult(new ProcessResult(0, "", ""));
		}
	}

	private sealed class ImportedImageRunner(bool wrongArchitecture, bool cached) : IProcessRunner
	{
		public List<string> PublishedTags { get; } = [];
		public int Loads { get; private set; }
		public Task<ProcessResult> RunAsync(string executable, IReadOnlyList<string> arguments, string directory, CancellationToken cancellation, int timeoutSeconds = 900)
		{
			var id = "sha256:" + new string('a', 64);

			if(arguments[0] == "image" && arguments[1] == "load")
				this.Loads++;

			if(arguments[0] == "tag")
			{
				Assert.Equal(id, arguments[1]);
				this.PublishedTags.Add(arguments[2]);
			}

			if(arguments[0] == "image" && arguments[1] == "inspect")
			{
				if(arguments[2] != id || !cached && this.Loads == 0)
					return Task.FromResult(new ProcessResult(1, "", "Only the imported image ID and upstream localhost tag exist."));

				return Task.FromResult(new ProcessResult(0, "[{\"Id\":\"" + id + "\",\"Os\":\"linux\",\"Architecture\":\"" + (wrongArchitecture ? "arm64" : "amd64") + "\"}]", ""));
			}

			return Task.FromResult(new ProcessResult(0, "", ""));
		}
	}

	private sealed class BootstrapRunner : IProcessRunner
	{
		public Task<ProcessResult> RunAsync(string executable, IReadOnlyList<string> arguments, string directory, CancellationToken cancellation, int timeoutSeconds = 900) =>
			Task.FromResult(new ProcessResult(executable == "sh" ? 1 : 0, "", ""));
	}

	private sealed class HealthRunner(string failure) : IProcessRunner
	{
		public Task<ProcessResult> RunAsync(string executable, IReadOnlyList<string> arguments, string directory, CancellationToken cancellation, int timeoutSeconds = 900)
		{
			var output = arguments[0] == "ps" ? failure == "missing" ? "" : "redis-container-id" :
				failure == "no-health" ? "[{\"State\":{\"Running\":true}}]" : "[{\"State\":{\"Running\":false,\"Health\":{\"Status\":\"unhealthy\"}}}]";
			return Task.FromResult(new ProcessResult(0, output, ""));
		}
	}

	private sealed class FakeHost : IInstallationHost
	{
		public List<string> Calls { get; } = [];
		public int Applies { get; private set; }
		public List<string> MigrationPaths { get; } = [];
		public bool FailMigration { get; set; }
		public bool FailImages { get; set; }
		public bool FailVerify { get; set; }
		public bool FailFinalPurge { get; set; }
		public int Uninstalls { get; private set; }
		public Task VerifyAsync(DeliveryBundle bundle, Installation installation, CancellationToken cancellation) => this.FailVerify ? throw new ContainerizationException(3, "Simulated target failure") : Task.CompletedTask;
		public Task InstallExecutorAsync(DeliveryBundle bundle, CancellationToken cancellation) => Task.CompletedTask;
		public Task BootstrapAsync(DeliveryBundle bundle, Installation installation, CancellationToken cancellation) => Task.CompletedTask;
		public Task ImagesAsync(DeliveryBundle bundle, CancellationToken cancellation) => this.FailImages ? throw new ContainerizationException(5, "Simulated image failure") : Task.CompletedTask;
		public Task DirectoriesAsync(DeliveryBundle bundle, Installation installation, CancellationToken cancellation) => Task.CompletedTask;
		public Task StartAsync(DeliveryBundle bundle, ServicePlan service, CancellationToken cancellation) { this.Calls.Add("start:" + service.Id); return Task.CompletedTask; }
		public Task HealthyAsync(DeliveryBundle bundle, ServicePlan service, CancellationToken cancellation) => Task.CompletedTask;
		public Task StopApplicationsAsync(Installation installation, CancellationToken cancellation)
		{
			installation.Maintenance = true;
			this.Calls.Add("stop");
			return Task.CompletedTask;
		}
		public Task RestorePoliciesAsync(DeliveryBundle bundle, CancellationToken cancellation) => Task.CompletedTask;
		public Task RestartAsync(DeliveryBundle bundle, ServicePlan service, CancellationToken cancellation) { this.Calls.Add("restart:" + service.Id); return Task.CompletedTask; }
		public Task<int> MigrateAsync(DeliveryBundle bundle, MigrationPlan migration, string state, string operation, string log, CancellationToken cancellation)
		{
			this.MigrationPaths.Add(state);
			if(operation == "apply")
			{
				this.Applies++;
				return Task.FromResult(this.FailMigration ? 1 : 0);
			}

			return Task.FromResult(0);
		}
		public Task UninstallAsync(Installation installation, bool purge, CancellationToken cancellation) { this.Uninstalls++; return Task.CompletedTask; }
		public Task FinalizePurgeAsync(Installation installation, CancellationToken cancellation) => this.FailFinalPurge ? throw new ContainerizationException(9, "Simulated cleanup failure") : Task.CompletedTask;
		public Task LogsAsync(Installation installation, ExecutorArguments arguments, CancellationToken cancellation) => Task.CompletedTask;
	}
}
