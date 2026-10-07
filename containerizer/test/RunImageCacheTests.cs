using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Collections.Generic;

using Xunit;
using Zongsoft.Components;
using Zongsoft.Tools.Containerizer.Protocol;

namespace Zongsoft.Tools.Containerizer.Tests;

[Collection("Run sessions")]
public sealed class RunImageCacheTests : IDisposable
{
	private const string INFRASTRUCTURE = "sha256:aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
	private const string APPLICATION = "sha256:bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb";
	private const string OBSOLETE = "sha256:cccccccccccccccccccccccccccccccccccccccccccccccccccccccccccccccc";
	private readonly string _root = Path.Combine(Path.GetTempPath(), $"containerizer-image-cache-test-{Guid.NewGuid():N}");

	public void Dispose() => Directory.Delete(_root, true);

	[Fact]
	public async Task RepeatedSessionsKeepOnlyCurrentInfrastructureAndNeverKeepTestDataOrApplicationImagesAsync()
	{
		var runner = new Runner();
		using var bundle = this.CreateBundle();
		Assert.Equal(0, await ExecuteSessionAsync("podman", runner, bundle));
		var volume = runner.Volume;

		Assert.Equal([INFRASTRUCTURE], runner.Images);
		Assert.Contains(runner.Calls, call => call[0] == "exec" && call.Skip(2).SequenceEqual(["docker", "rm", "--force", "--volumes", "test-container"]));
		Assert.Contains(runner.Calls, call => call[0] == "exec" && call.Skip(2).SequenceEqual(["docker", "volume", "rm", "test-data"]));
		Assert.Contains(runner.Calls, call => call[0] == "exec" && call.Skip(2).SequenceEqual(["docker", "network", "rm", "test-network"]));
		Assert.Contains(runner.Calls, call => call[0] == "exec" && call.Skip(2).SequenceEqual(["docker", "image", "rm", "old-release-tag"]));
		Assert.DoesNotContain(runner.Calls, call => call.Contains("prune") || call.Take(2).SequenceEqual(["volume", "rm"]));

		Assert.Equal(0, await ExecuteSessionAsync("podman", runner, bundle));
		Assert.Equal(volume, runner.Volume);
		Assert.Single(runner.Calls, call => call.Take(2).SequenceEqual(["volume", "create"]));
		Assert.Contains(runner.Calls, call => call[0] == "run" && call.Contains($"{volume}:/cache:ro"));
	}

	[Theory]
	[InlineData(false)]
	[InlineData(true)]
	public async Task DirtyOrChangedEngineCachesAreReplacedBeforeInstallationAsync(bool changedEngine)
	{
		var runner = new Runner();
		using var bundle = this.CreateBundle();
		Assert.Equal(0, await ExecuteSessionAsync("docker", runner, bundle));

		if(changedEngine)
			bundle.Plan.Bootstrap.EngineVersion = "different";
		else
			runner.Clean = false;

		Assert.Equal(0, await ExecuteSessionAsync("docker", runner, bundle));
		Assert.Equal(2, runner.Calls.Count(call => call.Take(2).SequenceEqual(["volume", "create"])));
		Assert.Single(runner.Calls, call => call.SequenceEqual(["volume", "rm", runner.Volume]));
		Assert.True(runner.Clean);
	}

	[Theory]
	[InlineData(false)]
	[InlineData(true)]
	public async Task ForeignOrAttachedCachesCannotBeDeletedAsync(bool attached)
	{
		var runner = new Runner();
		using var bundle = this.CreateBundle();
		Assert.Equal(0, await ExecuteSessionAsync("podman", runner, bundle));
		runner.Attached = attached;

		if(!attached)
			runner.Labels["org.zongsoft.containerizer.run-cache"] = "another-owner";

		await Assert.ThrowsAsync<ContainerizationException>(() => ExecuteSessionAsync("podman", runner, bundle));
		Assert.DoesNotContain(runner.Calls, call => call.Take(2).SequenceEqual(["volume", "rm"]));
		Assert.True(runner.Exists);
	}

	[Fact]
	public async Task CleanupFailureDiscardsTheWholeCacheAndInterruptedCreatesAreAlsoRemovedAsync()
	{
		var runner = new Runner { FailInner = true };
		using var bundle = this.CreateBundle();
		Assert.Equal(0, await ExecuteSessionAsync("podman", runner, bundle));
		Assert.False(runner.Exists);
		Assert.False(runner.Clean);

		runner = new Runner { CancelCreate = true };
		await Assert.ThrowsAsync<OperationCanceledException>(() => ExecuteSessionAsync("docker", runner, bundle));
		Assert.False(runner.Exists);
	}

	[Fact]
	public async Task UnchangedReleaseVersionDoesNotPreserveOldInfrastructureContentAsync()
	{
		using var bundle = this.CreateBundle();
		var runner = new Runner();
		bundle.Plan.Services[0].Image.Id = OBSOLETE;
		Assert.Equal(0, await ExecuteSessionAsync("docker", runner, bundle));

		Assert.Equal([OBSOLETE], runner.Images);
		Assert.Contains(runner.Calls, call => call[0] == "exec" && call.Skip(2).SequenceEqual(["docker", "image", "rm", "--force", INFRASTRUCTURE]));
	}

	[Fact]
	public async Task ApplicationOnlySessionsDoNotKeepAnEmptyImageStoreAsync()
	{
		using var bundle = this.CreateBundle();
		var runner = new Runner();
		bundle.Plan.Services.RemoveAt(0);
		Assert.Equal(0, await ExecuteSessionAsync("podman", runner, bundle));

		Assert.Empty(runner.Images);
		Assert.False(runner.Exists);
		Assert.False(runner.Clean);
	}

	[Fact]
	public async Task ReleaseRechecksOwnershipBeforeDeletingADirtyCacheAsync()
	{
		var runner = new Runner { FailInner = true };
		using var bundle = this.CreateBundle();
		Assert.Equal(4, await ExecuteSessionAsync("podman", runner, bundle, () => runner.Labels["org.zongsoft.containerizer.run-cache"] = "another-owner"));
		Assert.True(runner.Exists);
		Assert.DoesNotContain(runner.Calls, call => call.Take(2).SequenceEqual(["volume", "rm"]));
	}

	private static async Task<int> ExecuteSessionAsync(string executable, Runner runner, DeliveryBundle bundle, Action onReady = null)
	{
		using var stopping = new CancellationTokenSource(TimeSpan.FromSeconds(10));
		var run = new RunContext(new(executable, runner), bundle, content =>
		{
			for(var item = content.First; item != null; item = item.Next)
			{
				if(item.ForegroundColor == CommandOutletColor.Green)
				{
					onReady?.Invoke();
					stopping.Cancel();
					break;
				}
			}
		});
		return await run.ExecuteAsync(stopping.Token);
	}

	private DeliveryBundle CreateBundle()
	{
		var name = $"cache-test-{Guid.NewGuid():N}";
		var plan = new DeliveryPlan
		{
			Name = name,
			Architecture = "x64",
			Distribution = "debian@13",
			Project = $"containerizer-{name}-{Files.HashText(name)[..8]}",
			DataRoot = Installation.Paths.GetDataPath(name),
		};
		plan.Services.Add(new() { Id = "redis", Image = new() { Id = INFRASTRUCTURE, Platform = "linux/amd64", Mode = "online", Reference = $"containerizer/{plan.Project}/redis:fixture" } });
		plan.Services.Add(new() { Id = "web", Kind = "application", Image = new() { Id = APPLICATION, Platform = "linux/amd64", Mode = "offline", Archive = "images/web.tar", Reference = $"containerizer/{plan.Project}/web:fixture" } });

		foreach(var file in new[] { "containerizer", "compose.yaml", "install.sh", "uninstall.sh", "images/web.tar" })
		{
			var path = Path.Combine(_root, file);
			Files.Write(path, "fixture");
			plan.Files.Add(new() { Path = file, Length = new FileInfo(path).Length, Hash = Files.Hash(path) });
		}

		Files.Save(Path.Combine(_root, DeliveryPlan.FileName), plan, ProtocolJson.Default.DeliveryPlan);
		Files.Write(Path.Combine(_root, "checksums.sha256"), $"{Files.Hash(Path.Combine(_root, DeliveryPlan.FileName))}  {DeliveryPlan.FileName}\n{string.Join('\n', plan.Files.Select(file => $"{file.Hash}  {file.Path}"))}\n");
		return DeliveryBundle.Open(_root);
	}

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
		public bool ContainerCreated { get; private set; }

		public Task<int> StreamAsync(string executable, IReadOnlyList<string> arguments, string directory, CancellationToken cancellation) => Task.FromResult(0);

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
				output = arguments.Any(argument => argument.StartsWith("volume=", StringComparison.Ordinal)) ? this.Attached ? "attached-container" : "" : arguments[^1] == "{{.ID}}" && this.ContainerCreated ? "session-container" : "";
			else if(arguments[0] == "info")
				output = "linux/amd64";
			else if(arguments[0] == "create")
				this.ContainerCreated = true;
			else if(arguments[0] == "rm")
				this.ContainerCreated = false;
			else if(arguments[0] == "inspect")
				output = """[{"NetworkSettings":{"Ports":{}}}]""";
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
