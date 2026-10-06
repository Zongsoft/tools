using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Collections.Generic;

using Xunit;
using Zongsoft.Tools.Containerizer.Protocol;

namespace Zongsoft.Tools.Containerizer.Tests;

[Collection("Build cleanup")]
public sealed class RuntimeEnvironmentCacheTests : IDisposable
{
	#region 成员字段
	private readonly string _root = Path.Combine(Path.GetTempPath(), $"containerizer-runtime-tests-{Guid.NewGuid():N}");
	private readonly RuntimeRunner _runner = new();
	#endregion

	#region 公共方法
	public void Dispose() => Directory.Delete(_root, true);

	[Theory]
	[InlineData("podman")]
	[InlineData("docker")]
	public async Task ReusesVerifiedEnvironmentAcrossBuildsWithoutCachingApplicationInputs(string engine)
	{
		await this.PrepareAsync(engine);
		await this.PrepareAsync(engine);

		Assert.Equal(1, _runner.Builds);
		Assert.Empty(_runner.Images);
		Assert.Empty(_runner.Containers);
		Assert.Empty(_runner.Builders);
		Assert.Single(Directory.GetFiles(Path.Combine(_root, "cache"), "*.tar", SearchOption.AllDirectories));
		Assert.All(_runner.Recipes, recipe =>
		{
			Assert.DoesNotContain("COPY", recipe);
			Assert.DoesNotContain("/input", recipe);
		});
		Assert.DoesNotContain(_runner.Calls, call => call[0] is "pull" or "manifest" || call.Contains("prune"));
	}

	[Fact]
	public async Task RefreshReplacesOnlyTheRequiredEnvironmentOncePerInvocation()
	{
		await this.PrepareAsync("podman");
		await this.PrepareAsync("podman", runtime: "aspnetcore-runtime-10.0.0");

		var cache = new RuntimeEnvironmentCache(new("podman", _runner), Path.Combine(_root, "cache"), true);
		await cache.PrepareAsync("debian@13", "x64", "dotnet-runtime-10.0.0", this.NewContext(), TestContext.Current.CancellationToken);
		await cache.PrepareAsync("debian@13", "x64", "dotnet-runtime-10.0.0", this.NewContext(), TestContext.Current.CancellationToken);

		Assert.Equal(3, _runner.Builds);
		Assert.Equal(2, Directory.GetFiles(Path.Combine(_root, "cache"), "*.tar", SearchOption.AllDirectories).Length);
		Assert.Single(_runner.Calls, call => call[0] == "pull");
	}

	[Theory]
	[InlineData("build")]
	[InlineData("export")]
	[InlineData("cancel")]
	[InlineData("platform")]
	[InlineData("volumes")]
	public async Task FailedRefreshPreservesOldCacheAndCleansAttemptResources(string failure)
	{
		await this.PrepareAsync("podman");
		var metadata = Assert.Single(Directory.GetFiles(Path.Combine(_root, "cache"), "environment.json", SearchOption.AllDirectories));
		var original = File.ReadAllText(metadata);
		_runner.Failure = failure;

		Assert.NotNull(await Record.ExceptionAsync(() => this.PrepareAsync("podman", true)));
		Assert.Equal(original, File.ReadAllText(metadata));
		Assert.Single(Directory.GetFiles(Path.Combine(_root, "cache"), "*.tar", SearchOption.AllDirectories));
		Assert.Empty(_runner.Images);
		Assert.Empty(_runner.Containers);

		_runner.Failure = null;
		var builds = _runner.Builds;
		await this.PrepareAsync("podman");
		Assert.Equal(builds, _runner.Builds);
	}

	[Theory]
	[InlineData("hash")]
	[InlineData("metadata")]
	[InlineData("environment")]
	[InlineData("missing")]
	[InlineData("recipe")]
	[InlineData("minimum")]
	[InlineData("base")]
	public async Task InvalidOrInsufficientEnvironmentIsRebuilt(string change)
	{
		await this.PrepareAsync("podman");
		var archive = Assert.Single(Directory.GetFiles(Path.Combine(_root, "cache"), "*.tar", SearchOption.AllDirectories));
		var metadata = Path.Combine(Path.GetDirectoryName(archive), "environment.json");

		if(change == "hash")
			File.WriteAllText(archive, "corrupt");
		if(change == "metadata")
			File.WriteAllText(metadata, "null");
		if(change == "missing")
			File.Delete(archive);

		if(change == "environment")
		{
			var record = Files.Load(metadata, RuntimeEnvironmentCache.CacheJson.Default.Record);
			record.Environment = ["invalid"];
			Files.Save(metadata, record, RuntimeEnvironmentCache.CacheJson.Default.Record);
		}

		if(change == "recipe")
		{
			var record = Files.Load(metadata, RuntimeEnvironmentCache.CacheJson.Default.Record);
			record.Signature = "obsolete";
			Files.Save(metadata, record, RuntimeEnvironmentCache.CacheJson.Default.Record);
		}

		if(change == "base")
			_runner.Digest = $"sha256:{new string('c', 64)}";

		_runner.Patch = 9;
		await this.PrepareAsync("podman", runtime: change == "minimum" ? "dotnet-runtime-10.0.9" : "dotnet-runtime-10.0.0");
		Assert.Equal(2, _runner.Builds);
		Assert.Single(Directory.GetFiles(Path.Combine(_root, "cache"), "*.tar", SearchOption.AllDirectories));
	}

	[Fact]
	public async Task ConcurrentBuildWaitsAndCancellationDoesNotDamageCurrentCache()
	{
		await this.PrepareAsync("podman");
		var profile = RuntimeEnvironmentCache.Profile("debian@13", "x64", "dotnet-runtime-10.0.0");
		var cacheRoot = Path.Combine(_root, "cache");

		using var locked = BuildStorage.Lock(Path.Combine(cacheRoot, "runtime", "podman", profile), cacheRoot);
		using var cancellation = new CancellationTokenSource();

		var cache = new RuntimeEnvironmentCache(new("podman", _runner), cacheRoot);
		var waiting = cache.PrepareAsync("debian@13", "x64", "dotnet-runtime-10.0.0", this.NewContext(), cancellation.Token);

		Assert.False(waiting.IsCompleted);
		cancellation.Cancel();
		await Assert.ThrowsAnyAsync<OperationCanceledException>(() => waiting);
		Assert.Equal(1, _runner.Builds);
		locked.Dispose();
		await this.PrepareAsync("podman");
		Assert.Equal(1, _runner.Builds);
	}

	[Fact]
	public async Task ConcurrentBuildersShareTheFirstCompletedEnvironment()
	{
		Directory.CreateDirectory(_root);
		var cacheRoot = Path.Combine(_root, "cache");
		var profile = RuntimeEnvironmentCache.Profile("debian@13", "x64", "dotnet-runtime-10.0.0");
		using var locked = BuildStorage.Lock(Path.Combine(cacheRoot, "runtime", "podman", profile), cacheRoot);
		var first = this.PrepareAsync("podman");
		var second = this.PrepareAsync("podman");

		Assert.False(first.IsCompleted);
		Assert.False(second.IsCompleted);

		locked.Dispose();
		await Task.WhenAll(first, second);

		Assert.Equal(1, _runner.Builds);
		Assert.Empty(_runner.Images);
		Assert.Empty(_runner.Containers);
	}

	[Fact]
	public async Task EngineAndRuntimeFamiliesUseSeparateCaches()
	{
		await this.PrepareAsync("podman");
		await this.PrepareAsync("docker");
		await this.PrepareAsync("podman", runtime: "aspnetcore-runtime-10.0.0");
		await this.PrepareAsync("podman", runtime: null);

		Assert.Equal(4, _runner.Builds);
		Assert.Equal(4, Directory.GetFiles(Path.Combine(_root, "cache"), "*.tar", SearchOption.AllDirectories).Length);
		Assert.NotEqual(RuntimeEnvironmentCache.Profile("debian@12", "arm64", null), RuntimeEnvironmentCache.Profile("debian@13", "x64", null));
	}
	#endregion

	#region 私有方法
	private string NewContext() => Path.Combine(_root, $"build-{Guid.NewGuid():N}");
	private Task<ImagePlan> PrepareAsync(string engine, bool refresh = false, string runtime = "dotnet-runtime-10.0.0") =>
		new RuntimeEnvironmentCache(new(engine, _runner), Path.Combine(_root, "cache"), refresh)
			.PrepareAsync("debian@13", "x64", runtime, this.NewContext(), TestContext.Current.CancellationToken);
	#endregion

	#region 嵌套类型
	private sealed class RuntimeRunner : IProcessRunner
	{
		public int Builds { get; private set; }
		public int Patch { get; set; } = 8;
		public string Failure { get; set; }
		public string Digest { get; set; } = $"sha256:{new string('a', 64)}";
		public List<string[]> Calls { get; } = [];
		public List<string> Recipes { get; } = [];
		public HashSet<string> Images { get; } = [];
		public HashSet<string> Containers { get; } = [];
		public HashSet<string> Builders { get; } = [];

		public Task<ProcessResult> RunAsync(string executable, IReadOnlyList<string> arguments, string directory, CancellationToken cancellation, int timeoutSeconds = 900)
		{
			var args = arguments.ToArray();
			this.Calls.Add(args);
			var output = "";

			if(args[0] == "manifest")
				output = JsonSerializer.Serialize(new { digest = this.Digest });

			if(args[0] == "image" && args[1] == "inspect")
			{
				var preparing = this.Images.Contains(args[^1]);
				output = JsonSerializer.Serialize(new[] { new
				{
					Id = $"sha256:{new string('b', 64)}", Os = "linux",
					Architecture = preparing && this.Failure == "platform" ? "arm64" : "amd64", Digest = this.Digest,
					RepoDigests = new[] { $"docker.io/library/debian@{this.Digest}" },
					Config = new
					{
						Env = new[] { "PATH=/usr/bin:/bin" },
						Volumes = preparing && this.Failure == "volumes" ? new Dictionary<string, object> { ["/data"] = new object() } : null,
					},
				} });
			}

			if(args[0] == "build" || args[0] == "buildx" && args[1] == "build")
			{
				this.Builds++;
				this.Images.Add(args[Array.IndexOf(args, "-t") + 1]);
				this.Recipes.Add(File.ReadAllText(Path.Combine(directory, "Dockerfile")));

				if(this.Failure == "build")
					return Task.FromResult(new ProcessResult(1, "", "build failed"));
			}

			if(args[0] == "buildx" && args[1] == "create")
				this.Builders.Add(args[Array.IndexOf(args, "--name") + 1]);
			if(args[0] == "buildx" && args[1] == "rm")
				this.Builders.Remove(args[^1]);
			if(args[0] is "create" or "run")
				this.Containers.Add(args[Array.IndexOf(args, "--name") + 1]);
			if(args[0] == "run")
				output = $"Microsoft.NETCore.App 10.0.{this.Patch} [/usr/share/dotnet]\nMicrosoft.AspNetCore.App 10.0.{this.Patch} [/usr/share/dotnet]\n";

			if(args[0] == "export")
			{
				File.WriteAllText(args[2], $"public environment {this.Builds}");
				if(this.Failure == "cancel")
					throw new OperationCanceledException();
				if(this.Failure == "export")
					return Task.FromResult(new ProcessResult(1, "", "export failed"));
			}

			if(args[0] == "image" && args[1] == "rm")
				this.Images.Remove(args[^1]);
			if(args[0] == "container" && args[1] == "rm")
				this.Containers.Remove(args[^1]);

			return Task.FromResult(new ProcessResult(0, output, ""));
		}
	}
	#endregion
}
