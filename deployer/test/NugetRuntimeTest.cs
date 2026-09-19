using Xunit;

namespace Zongsoft.Tools.Deployer.Tests;

public class NugetRuntimeTest
{
	[Theory]
	[InlineData("linux-musl", "x64", "unix;linux;linux-x64;linux-musl;linux-musl-x64", "linux-musl-x64")]
	[InlineData("linux-musl", "x64", "linux;linux-x64;linux-musl", "linux-musl")]
	[InlineData("linux-musl", "x64", "linux;unix-x64;linux-x64", "linux-x64")]
	[InlineData("linux-musl", "x64", "unix;unix-x64", "unix-x64")]
	[InlineData("osx", "arm64", "unix;unix-arm64;osx", "osx")]
	[InlineData("osx", "arm64", "unix;unix-arm64", "unix-arm64")]
	[InlineData("win", "x64", "any;win", "win")]
	public async Task Deploy_RuntimeGraphPrefersNearestCandidateInImportOrderAsync(string platform, string architecture, string candidates, string expected)
	{
		using var fixture = new DeploymentFixture();
		fixture.Package("Graph.Root");

		//候选目录故意包含多层祖先；最终内容必须由固定图谱的广度优先顺序决定。
		foreach(var candidate in candidates.Split(';'))
		{
			fixture.Write($"packages/graph.root/1.0.0/runtimes/{candidate}/lib/net10.0/Graph.Root.dll", $"managed:{candidate}");
			fixture.Write($"packages/graph.root/1.0.0/runtimes/{candidate}/native/graph.native", $"native:{candidate}");
		}

		fixture.Variables["platform"] = platform;
		fixture.Variables["architecture"] = architecture;
		var result = await fixture.CreateDeployer().DeployAsync(fixture.Manifest("nuget:Graph.Root@1.0.0"), fixture.Destination, TestContext.Current.CancellationToken);

		Assert.Equal(0, result.Failures);
		Assert.Equal(2, result.Successes);
		Assert.Equal("managed:" + expected, File.ReadAllText(Path.Combine(fixture.Destination, "Graph.Root.dll")));
		Assert.Equal("native:" + expected, File.ReadAllText(Path.Combine(fixture.Destination, "graph.native")));
		Assert.Equal(["Graph.Root.dll", "graph.native"], Directory.GetFiles(fixture.Destination).Select(Path.GetFileName).Order(StringComparer.Ordinal).ToArray());
	}

	[Fact]
	public async Task Deploy_RuntimeGraphSelectsCompatibleManagedAndNativeAssetsIndependentlyAsync()
	{
		using var fixture = new DeploymentFixture();
		fixture.Package("Graph.Independent");
		fixture.Write("packages/graph.independent/1.0.0/runtimes/linux-musl-x64/lib/net11.0/Graph.Independent.dll", "incompatible exact framework");
		fixture.Write("packages/graph.independent/1.0.0/runtimes/linux-musl/lib/net11.0/Graph.Independent.dll", "incompatible parent framework");
		fixture.Write("packages/graph.independent/1.0.0/runtimes/linux-x64/lib/net10.0/Graph.Independent.dll", "compatible managed fallback");
		fixture.Write("packages/graph.independent/1.0.0/runtimes/linux-musl-x64/native/graph.native", "exact native asset");
		fixture.Write("packages/graph.independent/1.0.0/runtimes/linux-x64/native/graph.native", "fallback native asset");
		fixture.Variables["platform"] = "linux-musl";
		fixture.Variables["architecture"] = "x64";

		var result = await fixture.CreateDeployer().DeployAsync(fixture.Manifest("nuget:Graph.Independent@1.0.0"), fixture.Destination, TestContext.Current.CancellationToken);

		Assert.Equal(0, result.Failures);
		Assert.Equal(2, result.Successes);
		Assert.Equal("compatible managed fallback", File.ReadAllText(Path.Combine(fixture.Destination, "Graph.Independent.dll")));
		Assert.Equal("exact native asset", File.ReadAllText(Path.Combine(fixture.Destination, "graph.native")));
		Assert.Equal(2, Directory.GetFiles(fixture.Destination).Length);
	}

	[Fact]
	public async Task Deploy_RuntimeGraphSelectsExactManagedAndFallbackNativeAssetsIndependentlyAsync()
	{
		using var fixture = new DeploymentFixture();
		fixture.Package("Graph.Reverse");
		fixture.Write("packages/graph.reverse/1.0.0/runtimes/linux-musl-x64/lib/net10.0/Graph.Reverse.dll", "exact managed asset");
		fixture.Write("packages/graph.reverse/1.0.0/runtimes/linux-x64/lib/net10.0/Graph.Reverse.dll", "fallback managed asset");
		fixture.Write("packages/graph.reverse/1.0.0/runtimes/linux-x64/native/graph.native", "compatible native fallback");
		fixture.Write("packages/graph.reverse/1.0.0/runtimes/linux-arm64/native/graph.native", "wrong architecture");
		fixture.Variables["platform"] = "linux-musl";
		fixture.Variables["architecture"] = "x64";

		var result = await fixture.CreateDeployer().DeployAsync(fixture.Manifest("nuget:Graph.Reverse@1.0.0"), fixture.Destination, TestContext.Current.CancellationToken);

		Assert.Equal(0, result.Failures);
		Assert.Equal(2, result.Successes);
		Assert.Equal("exact managed asset", File.ReadAllText(Path.Combine(fixture.Destination, "Graph.Reverse.dll")));
		Assert.Equal("compatible native fallback", File.ReadAllText(Path.Combine(fixture.Destination, "graph.native")));
		Assert.Equal(["Graph.Reverse.dll", "graph.native"], Directory.GetFiles(fixture.Destination).Select(Path.GetFileName).Order(StringComparer.Ordinal).ToArray());
	}
}
