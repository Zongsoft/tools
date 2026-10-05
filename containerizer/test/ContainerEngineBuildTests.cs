using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Collections.Generic;

using Xunit;
using Zongsoft.Tools.Containerizer.Protocol;

namespace Zongsoft.Tools.Containerizer.Tests;

[Collection("Build cleanup")]
public sealed class ContainerEngineBuildTests
{
	[Fact]
	public async Task PodmanDoesNotPersistIntermediateLayers()
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
	public async Task DockerDisposesEachPrivateBuilderAndItsCacheWithoutChangingTheDefault()
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
	[InlineData("create")]
	[InlineData("build")]
	[InlineData("cancel")]
	public async Task DockerCleansPartialBuildersAndCachesWhenCreationOrBuildingFails(string failure)
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
		public List<string[]> Calls { get; } = [];
		public HashSet<string> Builders { get; } = [];
		public HashSet<string> Caches { get; } = ["shared-cache"];

		public Task<ProcessResult> RunAsync(string executable, IReadOnlyList<string> arguments, string directory, CancellationToken cancellation, int timeoutSeconds = 900)
		{
			this.Calls.Add(arguments.ToArray());

			if(arguments[0] == "buildx")
			{
				switch(arguments[1])
				{
					case "create":
						Assert.Contains("docker-container", arguments);
						this.Builders.Add(arguments[3]);
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
