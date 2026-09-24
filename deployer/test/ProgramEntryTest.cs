using System.Reflection;

using Xunit;

namespace Zongsoft.Tools.Deployer.Tests;

public sealed class ProgramEntryTest
{
	[Fact]
	public async Task Main_SpacedEqualsOption_RepeatedCallsAndCommandOverwriteTakePrecedenceAsync()
	{
		using var fixture = new DeploymentFixture();
		fixture.Write("source/file.txt", "source content");
		var manifest = fixture.Manifest("file.txt");
		var target = fixture.Write("target with spaces/file.txt", "original content");
		var previousDirectory = Environment.CurrentDirectory;
		var previousOverwrite = Environment.GetEnvironmentVariable("overwrite");

		try
		{
			Environment.CurrentDirectory = fixture.Root;
			Environment.SetEnvironmentVariable("overwrite", "alway");
			var destination = "--destination=" + Path.GetDirectoryName(target);

			Assert.Equal(1, await RunAsync([destination, "missing.deploy"]));
			Assert.Equal(0, await RunAsync(["--overwrite=never", destination, manifest]));
			Assert.Equal("original content", File.ReadAllText(target));
			Assert.Equal(0, await RunAsync([destination, "--overwrite=alway", manifest]));
			Assert.Equal("source content", File.ReadAllText(target));
		}
		finally
		{
			Environment.SetEnvironmentVariable("overwrite", previousOverwrite);
			Environment.CurrentDirectory = previousDirectory;
		}
	}

	private static Task<int> RunAsync(string[] arguments)
	{
		var type = typeof(Deployer).Assembly.GetType("Zongsoft.Tools.Deployer.Program", true);
		var main = type.GetMethod("Main", BindingFlags.Public | BindingFlags.Static);
		return Assert.IsType<Task<int>>(main.Invoke(null, [arguments]));
	}
}
