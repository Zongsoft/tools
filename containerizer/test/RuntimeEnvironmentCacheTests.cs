using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using System.Collections.Generic;

using Xunit;
using Zongsoft.Tools.Containerizer.Protocol;

namespace Zongsoft.Tools.Containerizer.Tests;

public sealed class RuntimeEnvironmentCacheTests : IDisposable
{
	#region 成员字段
	private readonly string _root = Path.Combine(Path.GetTempPath(), $"containerizer-runtime-tests-{Guid.NewGuid():N}");
	private readonly RuntimeRunner _runner = new();
	#endregion

	#region 公共方法
	public RuntimeEnvironmentCacheTests() => Directory.CreateDirectory(_root);
	public void Dispose() => Directory.Delete(_root, true);

	[Fact]
	public void InstalledRuntimeSelectsACompatibleStableVersion()
	{
		Assert.Equal("dotnet-runtime-10.0.8", RuntimeEnvironmentCache.GetInstalledRuntime("dotnet-runtime-10.0.0",
			"Microsoft.NETCore.App 9.0.9 [/usr/share/dotnet]\nMicrosoft.NETCore.App 10.0.8 [/usr/share/dotnet]\nMicrosoft.NETCore.App 10.0.10-preview [/usr/share/dotnet]\n"));
		Assert.Throws<ContainerizationException>(() => RuntimeEnvironmentCache.GetInstalledRuntime("dotnet-runtime-10.0.2", "Microsoft.NETCore.App 10.0.1 [/usr/share/dotnet]\n"));
	}

	[Theory]
	[InlineData("podman")]
	[InlineData("docker")]
	public async Task ReusesVerifiedEnvironmentAcrossBuildsWithoutCachingApplicationInputsAsync(string engine)
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
	public async Task RefreshReplacesOnlyTheRequiredEnvironmentOncePerInvocationAsync()
	{
		await this.PrepareAsync("podman");
		await this.PrepareAsync("podman", runtime: "aspnetcore-runtime-10.0.0");

		var cache = new RuntimeEnvironmentCache(new("podman", _runner), Path.Combine(_root, "cache"), true);
		await cache.PrepareAsync("debian@13", "x64", "dotnet-runtime-10.0.0", this.GetNewContextPath(), TestContext.Current.CancellationToken);
		await cache.PrepareAsync("debian@13", "x64", "dotnet-runtime-10.0.0", this.GetNewContextPath(), TestContext.Current.CancellationToken);

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
	public async Task FailedRefreshPreservesOldCacheAndCleansAttemptResourcesAsync(string failure)
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
	public async Task InvalidOrInsufficientEnvironmentIsRebuiltAsync(string change)
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
			var record = JsonNode.Parse(File.ReadAllText(metadata));
			record["Environment"] = new JsonArray("invalid");
			File.WriteAllText(metadata, record.ToJsonString());
		}

		if(change == "recipe")
		{
			var record = JsonNode.Parse(File.ReadAllText(metadata));
			record["Signature"] = "obsolete";
			File.WriteAllText(metadata, record.ToJsonString());
		}

		if(change == "base")
			_runner.Digest = $"sha256:{new string('c', 64)}";

		_runner.Patch = 9;
		await this.PrepareAsync("podman", runtime: change == "minimum" ? "dotnet-runtime-10.0.9" : "dotnet-runtime-10.0.0");
		Assert.Equal(2, _runner.Builds);
		Assert.Single(Directory.GetFiles(Path.Combine(_root, "cache"), "*.tar", SearchOption.AllDirectories));
	}

	[Fact]
	public async Task ConcurrentBuildWaitsAndCancellationDoesNotDamageCurrentCacheAsync()
	{
		await this.PrepareAsync("podman");
		var cacheRoot = Path.Combine(_root, "cache");
		var cacheDirectory = Path.GetDirectoryName(Assert.Single(Directory.GetFiles(cacheRoot, "environment.json", SearchOption.AllDirectories)));

		using var locked = BuildStorage.AcquireLock(cacheDirectory, cacheRoot);
		using var cancellation = new CancellationTokenSource();

		var cache = new RuntimeEnvironmentCache(new("podman", _runner), cacheRoot);
		var waiting = cache.PrepareAsync("debian@13", "x64", "dotnet-runtime-10.0.0", this.GetNewContextPath(), cancellation.Token);

		Assert.False(waiting.IsCompleted);
		cancellation.Cancel();
		await Assert.ThrowsAnyAsync<OperationCanceledException>(() => waiting);
		Assert.Equal(1, _runner.Builds);
		locked.Dispose();
		await this.PrepareAsync("podman");
		Assert.Equal(1, _runner.Builds);
	}

	[Fact]
	public async Task ConcurrentBuildersShareTheFirstCompletedEnvironmentAsync()
	{
		await this.PrepareAsync("podman");
		var cacheRoot = Path.Combine(_root, "cache");
		var archive = Assert.Single(Directory.GetFiles(cacheRoot, "*.tar", SearchOption.AllDirectories));
		File.Delete(archive);
		using var locked = BuildStorage.AcquireLock(Path.GetDirectoryName(archive), cacheRoot);
		var first = this.PrepareAsync("podman");
		var second = this.PrepareAsync("podman");

		Assert.False(first.IsCompleted);
		Assert.False(second.IsCompleted);

		locked.Dispose();
		await Task.WhenAll(first, second);

		Assert.Equal(2, _runner.Builds);
		Assert.Empty(_runner.Images);
		Assert.Empty(_runner.Containers);
	}

	[Fact]
	public async Task EngineAndRuntimeFamiliesUseSeparateCachesAsync()
	{
		await this.PrepareAsync("podman");
		await this.PrepareAsync("docker");
		await this.PrepareAsync("podman", runtime: "aspnetcore-runtime-10.0.0");
		await this.PrepareAsync("podman", runtime: null);

		Assert.Equal(4, _runner.Builds);
		Assert.Equal(4, Directory.GetFiles(Path.Combine(_root, "cache"), "*.tar", SearchOption.AllDirectories).Length);
	}

	[Theory]
	[InlineData("ubuntu@22.04", "dotnet-runtime-10.0.0", "ppa:dotnet/backports")]
	[InlineData("debian@13", null, "apt-get install -y --no-install-recommends ca-certificates curl")]
	[InlineData("rocky@9", null, "command -v curl >/dev/null 2>&1 || dnf install -y curl-minimal")]
	public async Task EnvironmentRecipeInstallsRequiredRuntimeAndProbeDependenciesAsync(string distribution, string runtime, string expected)
	{
		await new RuntimeEnvironmentCache(new("podman", _runner), Path.Combine(_root, "cache"))
			.PrepareAsync(distribution, "x64", runtime, this.GetNewContextPath(), TestContext.Current.CancellationToken);

		Assert.Contains(expected, Assert.Single(_runner.Recipes));
	}
	#endregion

	#region 私有方法
	private string GetNewContextPath() => Path.Combine(_root, $"build-{Guid.NewGuid():N}");
	private Task<ImagePlan> PrepareAsync(string engine, bool refresh = false, string runtime = "dotnet-runtime-10.0.0") =>
		new RuntimeEnvironmentCache(new(engine, _runner), Path.Combine(_root, "cache"), refresh)
			.PrepareAsync("debian@13", "x64", runtime, this.GetNewContextPath(), TestContext.Current.CancellationToken);
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

		public Task<int> StreamAsync(string executable, IReadOnlyList<string> arguments, string directory, CancellationToken cancellation) => throw new NotSupportedException();

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
					RepoDigests = new[] { $"{ImageReference.GetRepository(args[^1])}@{this.Digest}" },
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
