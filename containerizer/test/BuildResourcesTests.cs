using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Collections.Generic;

using Xunit;
using Zongsoft.Tools.Containerizer.Protocol;

namespace Zongsoft.Tools.Containerizer.Tests;

public sealed class BuildResourcesTests
{
	[Theory]
	[InlineData("podman")]
	[InlineData("docker")]
	public async Task MissingResourcesDoNotProduceCleanupWarningsAsync(string engine)
	{
		var runner = new CleanupRunner(arguments => arguments[1] == "rm" ? new(1, "", "not found") : new(0, "", ""));
		var errors = new List<string>();
		var resources = new BuildResources(new ContainerEngine(engine, runner), errors.Add);
		resources.RegisterImage();
		resources.RegisterContainer();

		await resources.DisposeAsync();
		Assert.Empty(errors);
		Assert.Equal(2, runner.Calls.Count(arguments => arguments[1] == "rm"));
		Assert.Equal(2, runner.Calls.Count(arguments => arguments[1] == "ls"));
	}

	[Theory]
	[InlineData("podman")]
	[InlineData("docker")]
	public async Task CleanupErrorsPreserveTheOriginalFailureAndDoNotPreventOtherRemovalsAsync(string engine)
	{
		string blocked = null;
		var runner = new CleanupRunner(arguments => arguments[^1].Contains(blocked, StringComparison.Ordinal) ? throw new IOException("engine unavailable") : new(0, "", ""));
		var errors = new List<string>();
		var resources = new BuildResources(new ContainerEngine(engine, runner), errors.Add);
		var removable = resources.RegisterImage();
		blocked = resources.RegisterContainer();

		var exception = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
		{
			await using var scope = resources;
			await Task.Yield();
			throw new InvalidOperationException("original build failure");
		});

		Assert.Equal("original build failure", exception.Message);
		Assert.Contains(blocked, string.Join('\n', errors));
		Assert.Contains("engine unavailable", string.Join('\n', errors));
		Assert.Contains(engine, string.Join('\n', errors));
		Assert.Contains(runner.Calls, arguments => arguments.SequenceEqual(["image", "rm", removable]));
		Assert.DoesNotContain(runner.Calls, arguments => arguments.Contains("--force") || arguments.Contains("-f") || arguments.Contains("prune"));
	}

	[Theory]
	[InlineData(false)]
	[InlineData(true)]
	public async Task BuilderCleanupWarningsDistinguishOwnedBuildersFromOtherBuildersAsync(bool retained)
	{
		string builder = null;
		var runner = new CleanupRunner(arguments => arguments[1] == "rm" ? new(1, "", "builder removal failed") : new(0, builder + "-other\n" + (retained ? builder : ""), ""));
		var errors = new List<string>();
		var resources = new BuildResources(new ContainerEngine("docker", runner), errors.Add);
		builder = resources.RegisterBuilder();

		await resources.DisposeAsync();
		Assert.Equal(retained, string.Join('\n', errors).Contains("builder removal failed", StringComparison.Ordinal));
		Assert.Equal(["buildx", "rm", builder], runner.Calls[0]);
		Assert.Equal(["buildx", "ls", "--format", "{{.Name}}"], runner.Calls[1]);
	}

	private sealed class CleanupRunner(Func<IReadOnlyList<string>, ProcessResult> response) : IProcessRunner
	{
		public List<string[]> Calls { get; } = [];
		public Task<int> StreamAsync(string executable, IReadOnlyList<string> arguments, string directory, CancellationToken cancellation) => throw new NotSupportedException();

		public Task<ProcessResult> RunAsync(string executable, IReadOnlyList<string> arguments, string directory, CancellationToken cancellation, int timeoutSeconds = 900)
		{
			Assert.False(cancellation.CanBeCanceled);
			Assert.Equal(arguments[0] == "buildx" && arguments[1] == "rm" ? 120 : 30, timeoutSeconds);
			this.Calls.Add(arguments.ToArray());
			return Task.FromResult(response(arguments));
		}
	}
}
