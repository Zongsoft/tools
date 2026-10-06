using System;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Collections.Generic;

using Xunit;
using Zongsoft.Tools.Containerizer.Protocol;

namespace Zongsoft.Tools.Containerizer.Tests;

public sealed class RunImageCacheTests
{
	private const string INFRASTRUCTURE = "sha256:aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
	private const string APPLICATION = "sha256:bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb";
	private const string OBSOLETE = "sha256:cccccccccccccccccccccccccccccccccccccccccccccccccccccccccccccccc";

	[Fact]
	public async Task RepeatedSessionsKeepOnlyCurrentInfrastructureAndNeverKeepTestDataOrApplicationImages()
	{
		var runner = new Runner();
		var plan = Plan();
		var engine = new ContainerEngine("podman", runner);
		var first = new RunContext.ImageCache(engine, plan, "first");

		await first.PrepareAsync("base", TestContext.Current.CancellationToken);
		await first.CleanAsync("session", TestContext.Current.CancellationToken);
		await first.ReleaseAsync(TestContext.Current.CancellationToken);

		Assert.Equal([INFRASTRUCTURE], runner.Images);
		Assert.Contains(runner.Calls, call => call.SequenceEqual(["exec", "session", "docker", "rm", "--force", "--volumes", "test-container"]));
		Assert.Contains(runner.Calls, call => call.SequenceEqual(["exec", "session", "docker", "volume", "rm", "test-data"]));
		Assert.Contains(runner.Calls, call => call.SequenceEqual(["exec", "session", "docker", "network", "rm", "test-network"]));
		Assert.Contains(runner.Calls, call => call.SequenceEqual(["exec", "session", "docker", "image", "rm", "old-release-tag"]));
		Assert.DoesNotContain(runner.Calls, call => call.Contains("prune") || call.Take(2).SequenceEqual(["volume", "rm"]));

		var next = new RunContext.ImageCache(engine, plan, "second");
		await next.PrepareAsync("base", TestContext.Current.CancellationToken);
		Assert.Equal(first.Name, next.Name);
		Assert.Single(runner.Calls, call => call.Take(2).SequenceEqual(["volume", "create"]));
		Assert.Contains(runner.Calls, call => call[0] == "run" && call.Contains($"{first.Name}:/cache:ro"));
	}

	[Theory]
	[InlineData(false)]
	[InlineData(true)]
	public async Task DirtyOrChangedEngineCachesAreReplacedBeforeInstallation(bool changedEngine)
	{
		var runner = new Runner();
		var engine = new ContainerEngine("docker", runner);
		var plan = Plan();
		var first = new RunContext.ImageCache(engine, plan, "first");

		await first.PrepareAsync("base", TestContext.Current.CancellationToken);
		await first.CleanAsync("session", TestContext.Current.CancellationToken);

		if(changedEngine)
			plan.Bootstrap.EngineVersion = "different";
		else
			runner.Clean = false;

		await new RunContext.ImageCache(engine, plan, "second").PrepareAsync("base", TestContext.Current.CancellationToken);
		Assert.Equal(2, runner.Calls.Count(call => call.Take(2).SequenceEqual(["volume", "create"])));
		Assert.Single(runner.Calls, call => call.SequenceEqual(["volume", "rm", first.Name]));
		Assert.False(runner.Clean);
	}

	[Theory]
	[InlineData(false)]
	[InlineData(true)]
	public async Task ForeignOrAttachedCachesCannotBeDeleted(bool attached)
	{
		var runner = new Runner();
		var engine = new ContainerEngine("podman", runner);
		var first = new RunContext.ImageCache(engine, Plan(), "first");
		await first.PrepareAsync("base", TestContext.Current.CancellationToken);
		runner.Attached = attached;

		if(!attached)
			runner.Labels[RunContext.ImageCache.LABEL] = "another-owner";

		var next = new RunContext.ImageCache(engine, Plan(), "second");
		await Assert.ThrowsAsync<ContainerizationException>(() => next.PrepareAsync("base", TestContext.Current.CancellationToken));
		await next.ReleaseAsync(TestContext.Current.CancellationToken);
		Assert.DoesNotContain(runner.Calls, call => call.Take(2).SequenceEqual(["volume", "rm"]));
		Assert.True(runner.Exists);
	}

	[Fact]
	public async Task CleanupFailureDiscardsTheWholeCacheAndInterruptedCreatesAreAlsoRemoved()
	{
		var runner = new Runner { FailInner = true };
		var cache = new RunContext.ImageCache(new("podman", runner), Plan(), "session");
		await cache.PrepareAsync("base", TestContext.Current.CancellationToken);
		await Assert.ThrowsAsync<ContainerizationException>(() => cache.CleanAsync("session", TestContext.Current.CancellationToken));
		await cache.ReleaseAsync(TestContext.Current.CancellationToken);
		Assert.False(runner.Exists);
		Assert.False(runner.Clean);

		runner = new Runner { CancelCreate = true };
		cache = new RunContext.ImageCache(new("docker", runner), Plan(), "session");
		await Assert.ThrowsAsync<OperationCanceledException>(() => cache.PrepareAsync("base", TestContext.Current.CancellationToken));
		await cache.ReleaseAsync(TestContext.Current.CancellationToken);
		Assert.False(runner.Exists);
	}

	[Fact]
	public async Task UnchangedReleaseVersionDoesNotPreserveOldInfrastructureContent()
	{
		var plan = Plan();
		var runner = new Runner();
		plan.Services[0].Image.Id = OBSOLETE;
		var cache = new RunContext.ImageCache(new("docker", runner), plan, "session");

		await cache.PrepareAsync("base", TestContext.Current.CancellationToken);
		await cache.CleanAsync("session", TestContext.Current.CancellationToken);

		Assert.Equal([OBSOLETE], runner.Images);
		Assert.Contains(runner.Calls, call => call.SequenceEqual(["exec", "session", "docker", "image", "rm", "--force", INFRASTRUCTURE]));
	}

	[Fact]
	public async Task ApplicationOnlySessionsDoNotKeepAnEmptyImageStore()
	{
		var plan = Plan();
		var runner = new Runner();
		plan.Services.RemoveAt(0);
		var cache = new RunContext.ImageCache(new("podman", runner), plan, "session");

		await cache.PrepareAsync("base", TestContext.Current.CancellationToken);
		await cache.CleanAsync("session", TestContext.Current.CancellationToken);
		await cache.ReleaseAsync(TestContext.Current.CancellationToken);

		Assert.Empty(runner.Images);
		Assert.False(runner.Exists);
		Assert.False(runner.Clean);
	}

	[Fact]
	public async Task ReleaseRechecksOwnershipBeforeDeletingADirtyCache()
	{
		var runner = new Runner();
		var cache = new RunContext.ImageCache(new("podman", runner), Plan(), "session");
		await cache.PrepareAsync("base", TestContext.Current.CancellationToken);
		runner.Labels[RunContext.ImageCache.LABEL] = "another-owner";

		await Assert.ThrowsAsync<ContainerizationException>(() => cache.ReleaseAsync(TestContext.Current.CancellationToken));
		Assert.True(runner.Exists);
		Assert.DoesNotContain(runner.Calls, call => call.Take(2).SequenceEqual(["volume", "rm"]));
	}

	private static DeliveryPlan Plan() => new()
	{
		Name = "cache-test",
		Version = "1.0",
		Architecture = "x64",
		Distribution = "debian@13",
		Services = [new() { Id = "redis", Kind = "infrastructure", Image = new() { Id = INFRASTRUCTURE } }, new() { Id = "web", Kind = "application", Image = new() { Id = APPLICATION } }],
	};

	private sealed class Runner : IProcessRunner
	{
		public List<string[]> Calls { get; } = [];
		public Dictionary<string, string> Labels { get; } = [];
		public List<string> Images { get; } = [INFRASTRUCTURE, APPLICATION, OBSOLETE];
		public string Volume { get; private set; }
		public bool Exists { get; private set; }
		public bool Clean { get; set; }
		public bool Attached { get; set; }
		public bool FailInner { get; init; }
		public bool CancelCreate { get; init; }

		public Task<ProcessResult> RunAsync(string executable, IReadOnlyList<string> arguments, string directory, CancellationToken cancellation, int timeoutSeconds = 900)
		{
			this.Calls.Add(arguments.ToArray());
			var output = "";

			if(arguments[0] == "volume")
			{
				switch(arguments[1])
				{
					case "ls":
						output = this.Exists ? this.Volume : "";
						break;
					case "inspect":
						output = JsonSerializer.Serialize(new[] { new { this.Labels } });
						break;
					case "rm":
						this.Exists = false;
						this.Clean = false;
						break;
					case "create":
						this.Exists = true;
						this.Volume = arguments[^1];

						for(var index = 0; index < arguments.Count; index++)
						{
							if(arguments[index] == "--label")
							{
								var pair = arguments[index + 1].Split('=', 2);
								this.Labels[pair[0]] = pair[1];
								index++;
							}
						}

						if(this.CancelCreate)
							throw new OperationCanceledException(cancellation);

						break;
				}
			}
			else if(arguments[0] == "ps")
				output = this.Attached ? "attached-container" : "";
			else if(arguments[0] == "run")
				output = this.Clean ? this.Labels["org.zongsoft.containerizer.run-profile"] : "";
			else if(arguments[0] == "exec" && arguments[2] == "sh")
				this.Clean = true;
			else if(arguments[0] == "exec" && arguments[2] == "docker")
			{
				if(this.FailInner)
					return Task.FromResult(new ProcessResult(1, "", "fixture daemon failure"));

				switch(arguments[3])
				{
					case "ps":
						output = "test-container";
						break;
					case "volume" when arguments[4] == "ls":
						output = "test-data";
						break;
					case "network" when arguments[4] == "ls":
						output = "test-network";
						break;
					case "image" when arguments[4] == "ls":
						output = string.Join('\n', this.Images);
						break;
					case "image" when arguments[4] == "rm" && arguments[5] == "--force":
						this.Images.Remove(arguments[6]);
						break;
					case "image" when arguments[4] == "inspect":
						output = JsonSerializer.Serialize(new[] { new { RepoTags = new[] { "old-release-tag", $"containerizer/run-cache:{arguments[5][7..]}" } } });
						break;
				}
			}

			return Task.FromResult(new ProcessResult(0, output, ""));
		}
	}
}
