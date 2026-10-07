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

public sealed class ContainerEngineBuildTests
{
	[Fact]
	public async Task PodmanDoesNotPersistIntermediateLayersAsync()
	{
		var runner = new BuildRunner();
		await new ContainerEngine("podman", runner).BuildAsync("context", "linux/arm64", "application", CancellationToken.None, "final");
		var call = Assert.Single(runner.Calls);
		Assert.Contains("--layers=false", call);
		Assert.Contains("--force-rm", call);
		Assert.Contains("--target", call);
		Assert.Contains("final", call);
		Assert.Contains("linux/arm64", call);
		Assert.Equal("context", call[^1]);
	}

	[Fact]
	public async Task DockerDisposesEachPrivateBuilderAndItsCacheWithoutChangingTheDefaultAsync()
	{
		var runner = new BuildRunner();
		var engine = new ContainerEngine("docker", runner);
		await engine.BuildAsync("context", "linux/amd64", "first", CancellationToken.None, "final");
		await engine.BuildAsync("context", "linux/amd64", "second", CancellationToken.None);

		var builds = runner.Calls.Where(arguments => arguments.Take(2).SequenceEqual(["buildx", "build"])).ToArray();
		Assert.Equal(2, builds.Length);
		Assert.NotEqual(builds[0][3], builds[1][3]);
		Assert.Empty(runner.Builders);
		Assert.Equal(["shared-cache"], runner.Caches);
		Assert.All(builds, arguments => Assert.Contains("--load", arguments));
		Assert.DoesNotContain("--target", builds[1]);
		Assert.DoesNotContain(runner.Calls, arguments => arguments.Contains("--use") || arguments.Contains("prune") || arguments.Contains("--keep-state") || arguments.Contains("--force"));
	}

	[Theory]
	[InlineData(null)]
	[InlineData("create")]
	[InlineData("build")]
	[InlineData("cancel")]
	public async Task DockerMirrorConfigurationAndPrivateBuilderAreRemovedAfterEveryOutcomeAsync(string failure)
	{
		using var cancellation = new CancellationTokenSource();
		var runner = new BuildRunner { MirrorFlow = true, Failure = failure, Cancellation = cancellation };
		var engine = new ContainerEngine("docker", runner)
		{
			Mirrors = new() { Registries = new() { ["docker.io"] = ["mirror.example.com/docker.io", "backup.example.com"] } },
		};
		var error = await Record.ExceptionAsync(() => engine.BuildAsync("context", "linux/arm64", "application", cancellation.Token));
		Assert.Equal(failure == null, error == null);
		Assert.Contains("[registry.\"docker.io\"]", runner.ConfigurationText);
		Assert.Contains("mirrors = [\"mirror.example.com/docker.io\", \"backup.example.com\"]", runner.ConfigurationText);
		Assert.False(File.Exists(runner.ConfigurationPath));
		Assert.False(Directory.Exists(Path.GetDirectoryName(runner.ConfigurationPath)));
		Assert.Empty(runner.Builders);
		Assert.Equal(["shared-cache"], runner.Caches);
		var create = Assert.Single(runner.Calls, arguments => arguments.Take(2).SequenceEqual(["buildx", "create"]));
		Assert.Contains($"image=mirror.example.com/docker.io/moby/buildkit@{runner.Digest}", create);
		var pull = Assert.Single(runner.Calls, arguments => arguments[0] == "pull");
		Assert.Contains("linux/amd64", pull);
		Assert.DoesNotContain(runner.Calls, arguments => arguments.Contains("--use") || arguments.Contains("prune") || arguments.Contains("daemon.json"));
	}

	[Fact]
	public async Task PodmanMirrorBuildUsesTheVerifiedLocalBaseWithoutRegistryAccessAsync()
	{
		var runner = new BuildRunner();
		var engine = new ContainerEngine("podman", runner) { Mirrors = new() { Registries = new() { ["docker.io"] = ["mirror.example.com"] } } };
		var image = new ImagePlan { Id = $"sha256:{new string('a', 64)}", Repository = "docker.io/library/debian", Digest = $"sha256:{new string('b', 64)}" };
		Assert.Equal(image.Id, engine.GetBuildReference(image));
		await engine.BuildAsync("context", "linux/amd64", "application", TestContext.Current.CancellationToken);
		Assert.Contains("--pull=never", Assert.Single(runner.Calls));
	}

	[Theory]
	[InlineData("create")]
	[InlineData("build")]
	[InlineData("cancel")]
	public async Task DockerCleansPartialBuildersAndCachesWhenCreationOrBuildingFailsAsync(string failure)
	{
		using var cancellation = new CancellationTokenSource();
		var runner = new BuildRunner { Failure = failure, Cancellation = cancellation };
		var exception = await Record.ExceptionAsync(() => new ContainerEngine("docker", runner).BuildAsync("context", "linux/amd64", "application", cancellation.Token));

		if(failure == "cancel")
			Assert.IsType<OperationCanceledException>(exception);
		else
			Assert.Contains("fixture failure", Assert.IsType<ContainerizationException>(exception).Message);

		Assert.Empty(runner.Builders);
		Assert.Equal(["shared-cache"], runner.Caches);
		Assert.Single(runner.Calls, arguments => arguments.Take(2).SequenceEqual(["buildx", "rm"]));
	}

	private sealed class BuildRunner : IProcessRunner
	{
		public string Failure { get; init; }
		public CancellationTokenSource Cancellation { get; init; }
		public bool MirrorFlow { get; init; }
		public string ConfigurationPath { get; private set; }
		public string ConfigurationText { get; private set; }
		public string Digest { get; } = $"sha256:{new string('a', 64)}";
		private string _toolkit;
		public List<string[]> Calls { get; } = [];
		public HashSet<string> Builders { get; } = [];
		public HashSet<string> Caches { get; } = ["shared-cache"];

		public Task<int> StreamAsync(string executable, IReadOnlyList<string> arguments, string directory, CancellationToken cancellation) => throw new NotSupportedException();

		public Task<ProcessResult> RunAsync(string executable, IReadOnlyList<string> arguments, string directory, CancellationToken cancellation, int timeoutSeconds = 900)
		{
			this.Calls.Add(arguments.ToArray());
			if(this.MirrorFlow)
			{
				if(arguments[0] == "info")
					return Task.FromResult(new ProcessResult(0, "linux/amd64", ""));
				if(arguments[0] == "image")
					return Task.FromResult(_toolkit == null ? new ProcessResult(1, "", "missing") : new ProcessResult(0, JsonSerializer.Serialize(new[] { new { Id = $"sha256:{new string('b', 64)}", Os = "linux", Architecture = "amd64", Digest = this.Digest, RepoDigests = new[] { _toolkit } } }), ""));
				if(arguments.Take(2).SequenceEqual(["buildx", "imagetools"]))
					return Task.FromResult(new ProcessResult(0, JsonSerializer.Serialize(new { digest = this.Digest }), ""));
				if(arguments[0] == "pull")
					_toolkit = arguments[^1];
			}

			if(arguments[0] == "buildx")
			{
				switch(arguments[1])
				{
					case "create":
						Assert.Contains("docker-container", arguments);
						this.Builders.Add(arguments[3]);

						if(this.MirrorFlow)
						{
							this.ConfigurationPath = arguments[arguments.ToList().IndexOf("--buildkitd-config") + 1];
							this.ConfigurationText = File.ReadAllText(this.ConfigurationPath);
						}
						break;
					case "build":
						Assert.Contains(arguments[3], this.Builders);
						this.Caches.Add(arguments[3]);

						if(this.Failure == "cancel")
						{
							this.Cancellation.Cancel();
							throw new OperationCanceledException(cancellation);
						}

						break;
					case "rm":
						Assert.False(cancellation.CanBeCanceled);
						Assert.Equal(120, timeoutSeconds);
						Assert.True(this.Builders.Remove(arguments[^1]));
						this.Caches.Remove(arguments[^1]);
						break;
				}

				if(this.Failure == arguments[1])
					return Task.FromResult(new ProcessResult(1, "", "fixture failure"));
			}

			return Task.FromResult(new ProcessResult(0, "", ""));
		}
	}
}
