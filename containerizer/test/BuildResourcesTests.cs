using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Collections.Generic;

using Xunit;
using Zongsoft.Tools.Containerizer.Protocol;

namespace Zongsoft.Tools.Containerizer.Tests;

[Collection("Build cleanup")]
public sealed class BuildResourcesTests
{
	[Theory]
	[InlineData("podman")]
	[InlineData("docker")]
	public async Task MissingResourcesDoNotProduceCleanupWarnings(string engine)
	{
		var runner = new CleanupRunner(arguments => arguments[1] == "rm" ? new(1, "", "not found") : new(0, "", ""));
		var resources = new BuildResources(new ContainerEngine(engine, runner));
		resources.Image();
		resources.Container();
		var original = Console.Error;
		using var errors = new StringWriter();
		Console.SetError(errors);

		try
		{
			await resources.DisposeAsync();
			Assert.Equal("", errors.ToString());
			Assert.Equal(2, runner.Calls.Count(arguments => arguments[1] == "rm"));
			Assert.Equal(2, runner.Calls.Count(arguments => arguments[1] == "ls"));
		}
		finally { Console.SetError(original); }
	}

	[Theory]
	[InlineData("podman")]
	[InlineData("docker")]
	public async Task CleanupErrorsPreserveTheOriginalFailureAndDoNotPreventOtherRemovals(string engine)
	{
		string blocked = null;
		var runner = new CleanupRunner(arguments => arguments[^1].Contains(blocked, StringComparison.Ordinal) ? throw new IOException("engine unavailable") : new(0, "", ""));
		var resources = new BuildResources(new ContainerEngine(engine, runner));
		var removable = resources.Image();
		blocked = resources.Container();
		var original = Console.Error;
		using var errors = new StringWriter();
		Console.SetError(errors);

		try
		{
			var exception = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
			{
				await using var scope = resources;
				await Task.Yield();
				throw new InvalidOperationException("original build failure");
			});

			Assert.Equal("original build failure", exception.Message);
			Assert.Contains(blocked, errors.ToString());
			Assert.Contains("engine unavailable", errors.ToString());
			Assert.Contains(engine, errors.ToString());
			Assert.Contains(runner.Calls, arguments => arguments.SequenceEqual(["image", "rm", removable]));
			Assert.DoesNotContain(runner.Calls, arguments => arguments.Contains("--force") || arguments.Contains("-f") || arguments.Contains("prune"));
		}
		finally { Console.SetError(original); }
	}

	[Theory]
	[InlineData(false)]
	[InlineData(true)]
	public async Task BuilderCleanupWarningsDistinguishOwnedBuildersFromOtherBuilders(bool retained)
	{
		string builder = null;
		var runner = new CleanupRunner(arguments => arguments[1] == "rm" ? new(1, "", "builder removal failed") : new(0, builder + "-other\n" + (retained ? builder : ""), ""));
		var resources = new BuildResources(new ContainerEngine("docker", runner));
		builder = resources.Builder();
		var original = Console.Error;
		using var errors = new StringWriter();
		Console.SetError(errors);

		try
		{
			await resources.DisposeAsync();
			Assert.Equal(retained, errors.ToString().Contains("builder removal failed", StringComparison.Ordinal));
			Assert.Equal(["buildx", "rm", builder], runner.Calls[0]);
			Assert.Equal(["buildx", "ls", "--format", "{{.Name}}"], runner.Calls[1]);
		}
		finally { Console.SetError(original); }
	}

	private sealed class CleanupRunner(Func<IReadOnlyList<string>, ProcessResult> response) : IProcessRunner
	{
		public List<string[]> Calls { get; } = [];
		public Task<ProcessResult> RunAsync(string executable, IReadOnlyList<string> arguments, string directory, CancellationToken cancellation, int timeoutSeconds = 900)
		{
			Assert.False(cancellation.CanBeCanceled);
			Assert.Equal(arguments[0] == "buildx" && arguments[1] == "rm" ? 120 : 30, timeoutSeconds);
			this.Calls.Add(arguments.ToArray());
			return Task.FromResult(response(arguments));
		}
	}
}
