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

public sealed class ContainerEngineOwnershipTests : IDisposable
{
	private readonly string _root = Path.Combine(Path.GetTempPath(), $"containerizer-ownership-{Guid.NewGuid():N}");

	public void Dispose()
	{
		if(Directory.Exists(_root))
			Directory.Delete(_root, true);
	}

	[Theory]
	[InlineData("redis", "", 999, "redis", "999:999")]
	[InlineData("redis", "root", 100, "redis", "100:100")]
	[InlineData("valkey", "0:0", 101, "valkey:storage", "101:200")]
	[InlineData("redis", "redis", 999, null, "999:999")]
	[InlineData("redis", "", 999, "123:456", "123:456")]
	public async Task DataOwnerUsesLockedImageAccountsWithoutChangingProcessUser(string account, string imageUser, int uid, string owner, string expected)
	{
		var source = new ServiceBuildContext { Plan = new() { Id = account, Health = new() { Test = ["CMD", "true"] } } };
		source.Plan.Mounts.Add(new() { Source = "/data", Target = "/data", User = owner });
		source.Plan.Mounts.Add(new() { Source = "/config", Target = "/config", ReadOnly = true, User = "untouched" });
		source.Plan.Mounts.Add(new() { Target = "/temporary", Temporary = true, User = "untouched" });
		var runner = new Runner(imageUser, $"{account}:x:{uid}:{uid}::/home/{account}:/bin/sh\n");

		await new ContainerEngine("podman", runner).PrepareOwnershipAsync(source, _root, TestContext.Current.CancellationToken);

		Assert.Equal(expected, source.Plan.Mounts[0].User);
		Assert.Null(source.Plan.User);
		Assert.Equal("untouched", source.Plan.Mounts[1].User);
		Assert.Equal("untouched", source.Plan.Mounts[2].User);
		Assert.Single(runner.Calls, call => call[0] == "create");
		var removal = Assert.Single(runner.Calls, call => call.Take(2).SequenceEqual(["container", "rm"]));
		Assert.Equal(["container", "rm", "--volumes", runner.Calls.Single(call => call[0] == "create")[2]], removal);
		Assert.DoesNotContain(runner.Calls, call => call[0] is "run" or "start");
	}

	[Fact]
	public async Task UnknownDataOwnerFailsAndRemovesTheInspectionContainer()
	{
		var source = new ServiceBuildContext { Plan = new() { Id = "redis", Health = new() { Test = ["CMD", "true"] } } };
		source.Plan.Mounts.Add(new() { Source = "/data", Target = "/data", User = "missing" });
		var runner = new Runner("", "redis:x:999:999::/home/redis:/bin/sh\n");

		var failure = await Assert.ThrowsAsync<ContainerizationException>(() => new ContainerEngine("podman", runner).PrepareOwnershipAsync(source, _root, TestContext.Current.CancellationToken));

		Assert.Equal(2, failure.Code);
		Assert.Equal("missing", source.Plan.Mounts[0].User);
		var removal = Assert.Single(runner.Calls, call => call.Take(2).SequenceEqual(["container", "rm"]));
		Assert.Equal(["container", "rm", "--volumes", runner.Calls.Single(call => call[0] == "create")[2]], removal);
	}

	[Fact]
	public async Task RootImageWithoutDeclaredOwnerDoesNotGuessAnAccount()
	{
		var source = new ServiceBuildContext { Plan = new() { Id = "service", Health = new() { Test = ["CMD", "true"] } } };
		source.Plan.Mounts.Add(new() { Source = "/data", Target = "/data" });
		var runner = new Runner("root", "redis:x:999:999::/home/redis:/bin/sh\n");

		await new ContainerEngine("podman", runner).PrepareOwnershipAsync(source, _root, TestContext.Current.CancellationToken);

		Assert.Null(source.Plan.Mounts[0].User);
		Assert.DoesNotContain(runner.Calls, call => call[0] == "create");
	}

	private sealed class Runner(string imageUser, string passwd) : IProcessRunner
	{
		public List<string[]> Calls { get; } = [];
		public Task<ProcessResult> RunAsync(string executable, IReadOnlyList<string> arguments, string directory, CancellationToken cancellation, int timeoutSeconds = 900)
		{
			this.Calls.Add(arguments.ToArray());
			var output = arguments[0] == "image" ? JsonSerializer.Serialize<object[]>([new { Config = new { User = imageUser } }]) : "";

			if(arguments[0] == "cp")
				File.WriteAllText(arguments[^1], arguments[1].EndsWith("/etc/passwd", StringComparison.Ordinal) ? passwd : "storage:x:200:\n");

			return Task.FromResult(new ProcessResult(0, output, ""));
		}
	}
}
