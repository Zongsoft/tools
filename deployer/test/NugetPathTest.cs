using Xunit;

namespace Zongsoft.Tools.Deployer.Tests;

public class NugetPathTest
{
	#region 路径适配
	[Theory]
	[InlineData("lib/net9.0/Path.Source.dll", "net10.0")]
	[InlineData("lib/net9.0/*.dll", "net10.0")]
	[InlineData("lib/*.dll", "net9.0")]
	public async Task Deploy_LocalCacheLibraryPathUsesExplicitFrameworkBeforeVariable(string requested, string framework)
	{
		using var fixture = new DeploymentFixture();
		var package = fixture.Package("Path.Source", framework: "net8.0");
		fixture.Write("packages/path.source/1.0.0/lib/net10.0/Path.Source.dll", "framework ten asset");
		fixture.Variables["Framework"] = framework;
		var source = Path.Combine(package, requested.Replace('/', Path.DirectorySeparatorChar));
		var deployer = fixture.CreateDeployer();

		var result = await deployer.DeployAsync(fixture.Manifest("path:" + source), fixture.Destination, TestContext.Current.CancellationToken);

		Assert.Equal(0, result.Failures);
		Assert.Equal(1, result.Successes);
		Assert.Equal("Path.Source@1.0.0", File.ReadAllText(Path.Combine(fixture.Destination, "Path.Source.dll")));
		Assert.Equal(Path.Combine(package, "lib", "net8.0", "Path.Source.dll"), Assert.Single(deployer.Plan.Operations).Source);
	}

	[Theory]
	[InlineData("lib/net9.0/Path.Explicit.dll")]
	[InlineData("lib/net9.0/*.dll")]
	public async Task Deploy_ExplicitNugetLibraryPathSelectsNearestFramework(string requested)
	{
		using var fixture = new DeploymentFixture();
		var package = fixture.Package("Path.Explicit", framework: "net8.0");
		fixture.Write("packages/path.explicit/1.0.0/lib/net10.0/Path.Explicit.dll", "framework ten asset");
		var deployer = fixture.CreateDeployer();

		var result = await deployer.DeployAsync(fixture.Manifest("nuget:Path.Explicit@1.0.0/" + requested), fixture.Destination, TestContext.Current.CancellationToken);

		Assert.Equal(0, result.Failures);
		Assert.Equal(1, result.Successes);
		Assert.Equal("Path.Explicit@1.0.0", File.ReadAllText(Path.Combine(fixture.Destination, "Path.Explicit.dll")));
		Assert.Equal(Path.Combine(package, "lib", "net8.0", "Path.Explicit.dll"), Assert.Single(deployer.Plan.Operations).Source);
	}

	[Fact]
	public void GetFiles_ExplicitFrameworkPreservesNestedWildcardSuffix()
	{
		using var fixture = new DeploymentFixture();
		var first = fixture.Write("packages/path.nested/1.0.0/lib/net8.0/sub1/deep/file.txt", "first compatible asset");
		var second = fixture.Write("packages/path.nested/1.0.0/lib/net8.0/sub2/deep/file.txt", "second compatible asset");
		fixture.Write("packages/path.nested/1.0.0/lib/net10.0/sub1/deep/file.txt", "framework ten asset");
		fixture.Variables["expansion"] = "true";
		var requested = Path.Combine(fixture.Packages, "path.nested", "1.0.0", "lib", "net8.0", "sub?", "deep", "*.txt");

		var files = DeploymentUtility.GetFiles(requested, fixture.Variables, cancellation: TestContext.Current.CancellationToken).OrderBy(file => file.Path, StringComparer.Ordinal).ToArray();

		Assert.Equal([first, second], files.Select(file => file.Path).ToArray());
		Assert.Equal([Path.Combine("sub1", "deep"), Path.Combine("sub2", "deep")], files.Select(file => file.Suffix).ToArray());
		Assert.Equal(["first compatible asset", "second compatible asset"], files.Select(file => File.ReadAllText(file.Path)).ToArray());
	}

	[Fact]
	public void GetFiles_MissingExplicitFramework_ResolvesBeforeNestedWildcardSearch()
	{
		using var fixture = new DeploymentFixture();
		var first = fixture.Write("packages/path.fallback/1.0.0/lib/net8.0/sub1/file.txt", "first compatible asset");
		var second = fixture.Write("packages/path.fallback/1.0.0/lib/net8.0/sub2/file.txt", "second compatible asset");
		fixture.Write("packages/path.fallback/1.0.0/lib/net10.0/sub1/file.txt", "must not select newer framework");
		var requested = Path.Combine(fixture.Packages, "path.fallback", "1.0.0", "lib", "net9.0", "sub?", "*.txt");

		var files = DeploymentUtility.GetFiles(requested, fixture.Variables, cancellation: TestContext.Current.CancellationToken).ToArray();

		Assert.Equal(new[] { first, second }, files.Select(file => file.Path));
		Assert.Equal(new[] { "sub1", "sub2" }, files.Select(file => file.Suffix));
		Assert.Equal(new[] { "first compatible asset", "second compatible asset" }, files.Select(file => File.ReadAllText(file.Path)));
	}

	[Fact]
	public void GetFiles_WildcardFramework_PreservesFrameworkAndDirectoryCaptures()
	{
		using var fixture = new DeploymentFixture();
		var first = fixture.Write("packages/path.framework/1.0.0/lib/net10.0/sub1/file.txt", "framework ten");
		var second = fixture.Write("packages/path.framework/1.0.0/lib/net8.0/sub1/file.txt", "framework eight first");
		var third = fixture.Write("packages/path.framework/1.0.0/lib/net8.0/sub2/file.txt", "framework eight second");
		fixture.Write("packages/path.framework/1.0.0/lib/other/sub1/file.txt", "not a matching framework name");
		var requested = Path.Combine(fixture.Packages, "path.framework", "1.0.0", "lib", "net*", "sub?", "*.txt");

		var files = DeploymentUtility.GetFiles(requested, fixture.Variables, cancellation: TestContext.Current.CancellationToken).ToArray();

		Assert.Equal(new[] { first, second, third }, files.Select(file => file.Path));
		Assert.Equal(new[] { "net10.0/sub1", "net8.0/sub1", "net8.0/sub2" }, files.Select(file => file.Suffix.Replace('\\', '/')));
		Assert.Equal(new[] { "framework ten", "framework eight first", "framework eight second" }, files.Select(file => File.ReadAllText(file.Path)));
	}

	[Fact]
	public void GetFiles_LibraryFallbackWithoutCompatibleFrameworkPreservesOriginalPath()
	{
		using var fixture = new DeploymentFixture();
		fixture.Write("packages/path.incompatible/1.0.0/lib/net10.0/file.dll", "incompatible asset");
		var requested = Path.Combine(fixture.Packages, "path.incompatible", "1.0.0", "lib", "net8.0", "file.dll");

		var file = Assert.Single(DeploymentUtility.GetFiles(requested, fixture.Variables, cancellation: TestContext.Current.CancellationToken));

		Assert.Equal(requested, file.Path);
		Assert.False(file.Exists());
	}
	#endregion

	#region 缓存边界
	[Fact]
	public void GetFiles_CachePrefixSiblingIsNotRewritten()
	{
		using var fixture = new DeploymentFixture();
		fixture.Write("packages-other/path.outside/1.0.0/lib/net8.0/file.dll", "outside cache asset");
		var directory = Path.Combine(fixture.Root, "packages-other", "path.outside", "1.0.0", "lib", "net9.0");
		var requested = Path.Combine(directory, "file.dll");

		var file = Assert.Single(DeploymentUtility.GetFiles(requested, fixture.Variables, cancellation: TestContext.Current.CancellationToken));

		Assert.Equal(requested, file.Path);
		Assert.False(file.Exists());
		Assert.Empty(DeploymentUtility.GetFiles(Path.Combine(directory, "*.dll"), fixture.Variables, cancellation: TestContext.Current.CancellationToken));
	}

	[Fact]
	public void GetFiles_CacheAncestorNamedLibDoesNotHidePackageLibrary()
	{
		using var fixture = new DeploymentFixture();
		var expected = fixture.Write("lib/net10.0/cache/path.ancestor/1.0.0/lib/net8.0/file.dll", "package library asset");
		fixture.Variables["NuGet_Packages"] = Path.Combine(fixture.Root, "lib", "net10.0", "cache");
		var requested = Path.Combine(fixture.Variables["NuGet_Packages"], "path.ancestor", "1.0.0", "lib", "net9.0", "file.dll");

		var file = Assert.Single(DeploymentUtility.GetFiles(requested, fixture.Variables, cancellation: TestContext.Current.CancellationToken));

		Assert.Equal(expected, file.Path);
		Assert.Equal("package library asset", File.ReadAllText(file.Path));
	}
	#endregion

	#region 自包内容
	[Fact]
	public async Task Deploy_AutomaticContentEnumerationPreservesAllLibraryNamedDirectories()
	{
		using var fixture = new DeploymentFixture();
		fixture.Package("Content.Paths", framework: "net8.0");
		fixture.Write("packages/content.paths/1.0.0/content/lib/net8.0/file.txt", "framework eight content");
		fixture.Write("packages/content.paths/1.0.0/content/lib/net9.0/file.txt", "framework nine content");
		fixture.Variables["Framework"] = "net9.0";

		var result = await fixture.CreateDeployer().DeployAsync(fixture.Manifest("nuget:Content.Paths@1.0.0"), fixture.Destination, TestContext.Current.CancellationToken);

		Assert.Equal(0, result.Failures);
		Assert.Equal(3, result.Successes);
		Assert.Equal("framework eight content", File.ReadAllText(Path.Combine(fixture.Destination, "lib", "net8.0", "file.txt")));
		Assert.Equal("framework nine content", File.ReadAllText(Path.Combine(fixture.Destination, "lib", "net9.0", "file.txt")));
		Assert.Equal(["Content.Paths.dll", "lib/net8.0/file.txt", "lib/net9.0/file.txt"],
			Directory.GetFiles(fixture.Destination, "*", SearchOption.AllDirectories).Select(file => Path.GetRelativePath(fixture.Destination, file).Replace('\\', '/')).Order(StringComparer.Ordinal).ToArray());
		Assert.False(File.Exists(Path.Combine(fixture.Destination, "lib", "file.txt")));
	}
	#endregion
}
