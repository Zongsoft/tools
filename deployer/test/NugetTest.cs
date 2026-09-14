using System.Xml.Linq;

using NuGet.Versioning;

using Xunit;

namespace Zongsoft.Tools.Deployer.Tests;

public class NugetTest
{
	[Fact]
	public async Task Deploy_SharedDependencyCopiesEachTargetOnlyOnce()
	{
		using var fixture = new DeploymentFixture();
		fixture.Package("Shared.Left", dependencies: [("Shared.Common", "[1.0.0]")]);
		fixture.Package("Shared.Right", dependencies: [("Shared.Common", "[1.0.0]")]);
		fixture.Package("Shared.Common");
		fixture.Variables["overwrite"] = "alway";
		var deployer = fixture.CreateDeployer();
		var result = await deployer.DeployAsync(fixture.Manifest("nuget:Shared.Left@1.0.0\nnuget:Shared.Right@1.0.0"), fixture.Destination, TestContext.Current.CancellationToken);
		Assert.Equal(0, result.Failures);
		Assert.Equal(3, result.Successes);
		Assert.Equal(1, result.Skipped);
		Assert.Equal(3, Directory.GetFiles(fixture.Destination).Length);
		Assert.Equal("Shared.Common@1.0.0", File.ReadAllText(Path.Combine(fixture.Destination, "Shared.Common.dll")));
		var duplicate = Assert.Single(deployer.Plan.Operations, operation => operation.Status == "Duplicate");
		Assert.Equal(Path.Combine(fixture.Destination, "Shared.Common.dll"), duplicate.Destination);
		Assert.False(string.IsNullOrWhiteSpace(duplicate.Reason));
	}

	[Fact]
	public async Task Deploy_SamePackageAfterExplicitDeleteIsCopiedAgain()
	{
		using var fixture = new DeploymentFixture();
		fixture.Package("Repeated.Root");
		fixture.Variables["overwrite"] = "alway";
		var deployer = fixture.CreateDeployer();
		var result = await deployer.DeployAsync(fixture.Manifest("nuget:Repeated.Root@1.0.0\ndelete:Repeated.Root.dll\nnuget:Repeated.Root@latest"), fixture.Destination, TestContext.Current.CancellationToken);
		Assert.Equal(0, result.Failures);
		Assert.Equal(2, result.Successes);
		Assert.Equal(1, result.Deleted);
		Assert.Equal(0, result.Skipped);
		Assert.Equal("Repeated.Root@1.0.0", File.ReadAllText(Path.Combine(fixture.Destination, "Repeated.Root.dll")));
		Assert.Equal(["Copied", "Deleted", "Copied"], deployer.Plan.Operations.Select(operation => operation.Status).ToArray());
	}

	[Theory]
	[InlineData(false, false, false)]
	[InlineData(true, false, true)]
	[InlineData(true, true, true)]
	public async Task Deploy_ContentFilesHonorCopyToOutputAndFlatten(bool copy, bool flatten, bool exists)
	{
		using var fixture = new DeploymentFixture();
		var root = fixture.Package("Content.Root");
		fixture.Write("packages/content.root/1.0.0/contentFiles/any/net10.0/nested/config.txt", "selected content");
		fixture.Write("packages/content.root/1.0.0/contentFiles/any/net10.0/nested/excluded.txt", "excluded content");
		var specPath = Directory.GetFiles(root, "*.nuspec").Single();
		var spec = XDocument.Load(specPath);
		spec.Root.Element("metadata").Add(new XElement("contentFiles", new XElement("files", new XAttribute("include", "any/net10.0/**/*.txt"),
			new XAttribute("exclude", "**/excluded.txt"), new XAttribute("copyToOutput", copy), new XAttribute("flatten", flatten))));
		spec.Save(specPath);
		var result = await fixture.CreateDeployer().DeployAsync(fixture.Manifest("nuget:Content.Root@1.0.0"), fixture.Destination, TestContext.Current.CancellationToken);
		Assert.Equal(0, result.Failures);
		var path = Path.Combine(fixture.Destination, flatten ? "config.txt" : "nested/config.txt");
		Assert.Equal(exists, File.Exists(path));
		if(exists)
			Assert.Equal("selected content", File.ReadAllText(path));
		Assert.DoesNotContain(Directory.GetFiles(fixture.Destination, "*", SearchOption.AllDirectories), file => Path.GetFileName(file) == "excluded.txt");
		Assert.Equal("Content.Root@1.0.0", File.ReadAllText(Path.Combine(fixture.Destination, "Content.Root.dll")));
	}

	[Fact]
	public async Task Deploy_ContentFilesWithoutCopyRuleAreNotDeployed()
	{
		using var fixture = new DeploymentFixture();
		fixture.Package("Content.Default");
		fixture.Write("packages/content.default/1.0.0/contentFiles/any/net10.0/config.txt", "not opted in");
		var result = await fixture.CreateDeployer().DeployAsync(fixture.Manifest("nuget:Content.Default@1.0.0"), fixture.Destination, TestContext.Current.CancellationToken);
		Assert.Equal(0, result.Failures);
		Assert.False(File.Exists(Path.Combine(fixture.Destination, "config.txt")));
		Assert.Equal(1, result.Successes);
	}

	[Fact]
	public async Task Deploy_LegacyContentIncludesRootAndNestedFiles()
	{
		using var fixture = new DeploymentFixture();
		fixture.Package("Content.Legacy");
		fixture.Write("packages/content.legacy/1.0.0/content/root.txt", "root");
		fixture.Write("packages/content.legacy/1.0.0/content/nested/child.txt", "child");
		var result = await fixture.CreateDeployer().DeployAsync(fixture.Manifest("nuget:Content.Legacy@1.0.0"), fixture.Destination, TestContext.Current.CancellationToken);
		Assert.Equal(0, result.Failures);
		Assert.Equal("root", File.ReadAllText(Path.Combine(fixture.Destination, "root.txt")));
		Assert.Equal("child", File.ReadAllText(Path.Combine(fixture.Destination, "nested", "child.txt")));
	}

	[Theory]
	[InlineData("windows", "x32", "win-x86")]
	[InlineData("mac", "arm64", "osx-arm64")]
	[InlineData("linux-musl", "x64", "linux-x64")]
	public async Task Deploy_RuntimeAliasesAndPortableFallbackSelectExpectedGroup(string platform, string architecture, string runtime)
	{
		using var fixture = new DeploymentFixture();
		fixture.Package("Aliases.Root");
		fixture.Write("packages/aliases.root/1.0.0/lib/net10.0/PortableOnly.dll", "must not mix runtime group");
		fixture.Write($"packages/aliases.root/1.0.0/runtimes/{runtime}/lib/net10.0/Aliases.Root.dll", "runtime selected");
		fixture.Variables["platform"] = platform;
		fixture.Variables["architecture"] = architecture;
		var result = await fixture.CreateDeployer().DeployAsync(fixture.Manifest("nuget:Aliases.Root@1.0.0"), fixture.Destination, TestContext.Current.CancellationToken);
		Assert.Equal(0, result.Failures);
		Assert.Equal("runtime selected", File.ReadAllText(Path.Combine(fixture.Destination, "Aliases.Root.dll")));
		Assert.False(File.Exists(Path.Combine(fixture.Destination, "PortableOnly.dll")));
	}

	[Fact]
	public async Task Metadata_LatestDefaultsToStableAndHonorsChangedPrereleasePolicy()
	{
		using var fixture = new DeploymentFixture();
		fixture.Package("Preview.Root", "1.0.0");
		fixture.Package("Preview.Root", "2.0.0-preview.1");
		var stable = await NugetUtility.GetPackageMetadataAsync(fixture.Variables, "Preview.Root", "latest", TestContext.Current.CancellationToken);
		Assert.Equal("1.0.0", stable.Identity.Version.ToNormalizedString());
		fixture.Variables["prerelease"] = "true";
		var preview = await NugetUtility.GetPackageMetadataAsync(fixture.Variables, "Preview.Root", "latest", TestContext.Current.CancellationToken);
		Assert.Equal("2.0.0-preview.1", preview.Identity.Version.ToNormalizedString());
	}

	[Theory]
	[InlineData("net8.0", "group.eight")]
	[InlineData("net9.0", "group.eight")]
	[InlineData("net10.0", "group.ten")]
	public async Task Dependencies_NearestFrameworkGroupIsSelected(string framework, string expected)
	{
		using var fixture = new DeploymentFixture();
		var root = fixture.Package("Group.Root", framework: "net8.0", dependencies: [("Group.Eight", "[1.0.0]")]);
		fixture.Package("Group.Eight", framework: "net8.0");
		fixture.Package("Group.Ten");
		var specPath = Directory.GetFiles(root, "*.nuspec").Single();
		var spec = XDocument.Load(specPath);
		spec.Root.Element("metadata").Element("dependencies").Add(new XElement("group", new XAttribute("targetFramework", "net10.0"),
			new XElement("dependency", new XAttribute("id", "Group.Ten"), new XAttribute("version", "[1.0.0]"))));
		spec.Save(specPath);
		fixture.Variables["Framework"] = framework;
		var deployer = fixture.CreateDeployer();

		var result = await deployer.DeployAsync(fixture.Manifest("nuget:Group.Root@1.0.0"), fixture.Destination, TestContext.Current.CancellationToken);

		Assert.Equal(0, result.Failures);
		Assert.Equal(2, result.Successes);
		var dependency = Assert.Single(deployer.Plan.Packages, package => package.Id != "Group.Root");
		Assert.Equal(expected, dependency.Id.ToLowerInvariant());
		Assert.Equal(dependency.Id + "@1.0.0", File.ReadAllText(Path.Combine(fixture.Destination, dependency.Id + ".dll")));
	}

	[Fact]
	public async Task Deploy_ExplicitPackagePathDoesNotPullUnusedDependencies()
	{
		using var fixture = new DeploymentFixture();
		fixture.Package("Explicit.Root", dependencies: [("Unavailable.Child", "[1.0.0]")]);
		fixture.Write("packages/explicit.root/1.0.0/content/requested.txt", "explicit content");
		var result = await fixture.CreateDeployer().DeployAsync(fixture.Manifest("nuget:Explicit.Root@1.0.0/content/requested.txt"), fixture.Destination, TestContext.Current.CancellationToken);
		Assert.Equal(0, result.Failures);
		Assert.Equal(1, result.Successes);
		Assert.Equal("explicit content", File.ReadAllText(Path.Combine(fixture.Destination, "requested.txt")));
		Assert.False(Directory.Exists(Path.Combine(fixture.Packages, "unavailable.child")));
	}

	[Fact]
	public async Task Deploy_PackageManifestDoesNotPullUnusedDependencies()
	{
		using var fixture = new DeploymentFixture();
		fixture.Package("Manifest.Root", dependencies: [("Unavailable.Child", "[1.0.0]")]);
		fixture.Write("packages/manifest.root/1.0.0/content/requested.txt", "manifest content");
		fixture.Write("packages/manifest.root/1.0.0/.deploy", "content/requested.txt");
		var result = await fixture.CreateDeployer().DeployAsync(fixture.Manifest("nuget:Manifest.Root@1.0.0"), fixture.Destination, TestContext.Current.CancellationToken);
		Assert.Equal(0, result.Failures);
		Assert.Equal("manifest content", File.ReadAllText(Path.Combine(fixture.Destination, "requested.txt")));
		Assert.False(Directory.Exists(Path.Combine(fixture.Packages, "unavailable.child")));
	}

	[Fact]
	public async Task Deploy_MultipleRootsWithIncompatibleDependencyConstraintsFailBeforeCopy()
	{
		using var fixture = new DeploymentFixture();
		fixture.Package("Roots.First", dependencies: [("Roots.Shared", "[1.0.0]")]);
		fixture.Package("Roots.Second", dependencies: [("Roots.Shared", "[2.0.0]")]);
		fixture.Package("Roots.Shared");
		fixture.Package("Roots.Shared", "2.0.0");
		var result = await fixture.CreateDeployer().DeployAsync(fixture.Manifest("nuget:Roots.First@1.0.0\nnuget:Roots.Second@1.0.0"), fixture.Destination, TestContext.Current.CancellationToken);
		Assert.True(result.Failures > 0);
		Assert.Equal(0, result.Successes);
		Assert.Empty(Directory.GetFiles(fixture.Destination));
		Assert.Contains("Roots.Shared", fixture.Log.ToString(), StringComparison.OrdinalIgnoreCase);
	}

	[Fact]
	public async Task Dependencies_CustomPrefixesAndDefaultPrefixes_ExcludeMatches()
	{
		using var fixture = new DeploymentFixture();
		fixture.Package("Review.Root", dependencies:
			[("extra.child", "[1.0.0]"), ("Another.Child", "[1.0.0]"), ("system.child", "[1.0.0]"), ("MICROSOFT.EXTENSIONS.Child", "[1.0.0]"), ("zongsoft.child", "[1.0.0]"), ("Retained.Child", "[1.0.0]")]);
		foreach(var id in new[] { "extra.child", "Another.Child", "system.child", "MICROSOFT.EXTENSIONS.Child", "zongsoft.child", "Retained.Child" })
			fixture.Package(id);
		fixture.Variables["ignoreDependentPrefix"] = " ;EXTRA., | Another.;";
		var deployer = fixture.CreateDeployer();

		var result = await deployer.DeployAsync(fixture.Manifest("nuget:Review.Root@1.0.0"), fixture.Destination, TestContext.Current.CancellationToken);

		Assert.Equal(0, result.Failures);
		Assert.Equal(["Retained.Child", "Review.Root"], deployer.Plan.Packages.Select(package => package.Id).Order(StringComparer.Ordinal).ToArray());
		Assert.Equal(["Retained.Child.dll", "Review.Root.dll"], Directory.GetFiles(fixture.Destination).Select(Path.GetFileName).Order(StringComparer.Ordinal).ToArray());
	}

	[Fact]
	public async Task Dependencies_ExplicitRootIsNotIgnored()
	{
		using var fixture = new DeploymentFixture();
		fixture.Package("System.Explicit");
		fixture.Variables["ignoreDependentPrefix"] = "System.";
		var result = await fixture.CreateDeployer().DeployAsync(fixture.Manifest("nuget:System.Explicit@1.0.0"), fixture.Destination, TestContext.Current.CancellationToken);
		Assert.Equal(0, result.Failures);
		Assert.Equal("System.Explicit@1.0.0", File.ReadAllText(Path.Combine(fixture.Destination, "System.Explicit.dll")));
	}

	[Theory]
	[InlineData("net8.0")]
	[InlineData("net9.0")]
	[InlineData("net10.0")]
	public async Task Dependencies_ChainIncludesGrandchild(string framework)
	{
		using var fixture = new DeploymentFixture();
		fixture.Package("Chain.Root", framework: framework, dependencies: [("Chain.Child", "[1.0.0]")]);
		fixture.Package("Chain.Child", framework: framework, dependencies: [("Chain.Grandchild", "[1.0.0]")]);
		fixture.Package("Chain.Grandchild", framework: framework);
		fixture.Variables["Framework"] = framework;
		var deployer = fixture.CreateDeployer();

		var result = await deployer.DeployAsync(fixture.Manifest("nuget:Chain.Root@1.0.0"), fixture.Destination, TestContext.Current.CancellationToken);

		Assert.Equal(0, result.Failures);
		Assert.Equal(3, result.Successes);
		Assert.Equal(["Chain.Child", "Chain.Grandchild", "Chain.Root"], deployer.Plan.Packages.Select(package => package.Id).Order(StringComparer.Ordinal).ToArray());
		Assert.Equal("Chain.Grandchild@1.0.0", File.ReadAllText(Path.Combine(fixture.Destination, "Chain.Grandchild.dll")));
	}

	[Theory]
	[InlineData("(1.0.0,2.0.0)", "1.1.0")]
	[InlineData("(,2.0.0]", "1.0.0")]
	[InlineData("[1.1.0,2.0.0)", "1.1.0")]
	[InlineData("[2.0.0]", "2.0.0")]
	public async Task Dependencies_VersionRangeSelectsAvailableVersion(string range, string expected)
	{
		using var fixture = new DeploymentFixture();
		fixture.Package("Range.Root", dependencies: [("Range.Child", range)]);
		foreach(var version in new[] { "1.0.0", "1.1.0", "2.0.0" })
			fixture.Package("Range.Child", version);
		var deployer = fixture.CreateDeployer();

		var result = await deployer.DeployAsync(fixture.Manifest("nuget:Range.Root@1.0.0"), fixture.Destination, TestContext.Current.CancellationToken);

		Assert.Equal(0, result.Failures);
		Assert.Equal(expected, Assert.Single(deployer.Plan.Packages, package => package.Id == "Range.Child").Version);
		Assert.Equal($"Range.Child@{expected}", File.ReadAllText(Path.Combine(fixture.Destination, "Range.Child.dll")));
	}

	[Fact]
	public async Task Dependencies_DiamondUnifiesCompatibleRanges()
	{
		using var fixture = new DeploymentFixture();
		fixture.Package("Diamond.Root", dependencies: [("Diamond.Left", "[1.0.0]"), ("Diamond.Right", "[1.0.0]")]);
		fixture.Package("Diamond.Left", dependencies: [("Diamond.Shared", "[1.0.0,3.0.0)")]);
		fixture.Package("Diamond.Right", dependencies: [("Diamond.Shared", "[2.0.0,3.0.0)")]);
		fixture.Package("Diamond.Shared");
		fixture.Package("Diamond.Shared", "2.0.0");
		var deployer = fixture.CreateDeployer();

		var result = await deployer.DeployAsync(fixture.Manifest("nuget:Diamond.Root@1.0.0"), fixture.Destination, TestContext.Current.CancellationToken);

		Assert.Equal(0, result.Failures);
		Assert.Equal(4, result.Successes);
		Assert.Equal(4, deployer.Plan.Packages.Count);
		Assert.Equal("2.0.0", Assert.Single(deployer.Plan.Packages, package => package.Id == "Diamond.Shared").Version);
		Assert.Equal("Diamond.Shared@2.0.0", File.ReadAllText(Path.Combine(fixture.Destination, "Diamond.Shared.dll")));
	}

	[Fact]
	public async Task Dependencies_UnsatisfiableRangeFails()
	{
		using var fixture = new DeploymentFixture();
		fixture.Package("Unsolvable.Root", dependencies: [("Unsolvable.Child", "(1.0.0,2.0.0)")]);
		fixture.Package("Unsolvable.Child", "1.0.0");
		fixture.Package("Unsolvable.Child", "2.0.0");
		var result = await fixture.CreateDeployer().DeployAsync(fixture.Manifest("nuget:Unsolvable.Root@1.0.0"), fixture.Destination, TestContext.Current.CancellationToken);

		Assert.True(result.Failures > 0);
		Assert.Equal(0, result.Successes);
		Assert.Empty(Directory.GetFiles(fixture.Destination));
		Assert.Contains("Unsolvable.Child", fixture.Log.ToString(), StringComparison.OrdinalIgnoreCase);
	}

	[Fact]
	public async Task Dependencies_IncompatibleDiamondFails()
	{
		using var fixture = new DeploymentFixture();
		fixture.Package("Conflict.Root", dependencies: [("Conflict.Left", "[1.0.0]"), ("Conflict.Right", "[1.0.0]")]);
		fixture.Package("Conflict.Left", dependencies: [("Conflict.Shared", "[1.0.0]")]);
		fixture.Package("Conflict.Right", dependencies: [("Conflict.Shared", "[2.0.0]")]);
		fixture.Package("Conflict.Shared");
		fixture.Package("Conflict.Shared", "2.0.0");
		var result = await fixture.CreateDeployer().DeployAsync(fixture.Manifest("nuget:Conflict.Root@1.0.0"), fixture.Destination, TestContext.Current.CancellationToken);

		Assert.True(result.Failures > 0);
		Assert.Equal(0, result.Successes);
		Assert.Empty(Directory.GetFiles(fixture.Destination));
		Assert.Contains("Conflict.Shared", fixture.Log.ToString(), StringComparison.OrdinalIgnoreCase);
	}

	[Fact]
	public async Task Dependencies_CycleTerminates()
	{
		using var fixture = new DeploymentFixture();
		fixture.Package("Cycle.Root", dependencies: [("Cycle.Child", "[1.0.0]")]);
		fixture.Package("Cycle.Child", dependencies: [("Cycle.Root", "[1.0.0]")]);
		using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(10));
		var result = await fixture.CreateDeployer().DeployAsync(fixture.Manifest("nuget:Cycle.Root@1.0.0"), fixture.Destination, cancellation.Token);

		Assert.True(result.Failures > 0);
		Assert.False(cancellation.IsCancellationRequested);
		Assert.Equal(0, result.Successes);
		Assert.Empty(Directory.GetFiles(fixture.Destination));
		Assert.Contains("Cycle.Root", fixture.Log.ToString(), StringComparison.OrdinalIgnoreCase);
	}

	[Fact]
	public void GetFolderPath_UsesEachCacheRoot()
	{
		using var fixture = new DeploymentFixture();
		var first = NugetUtility.GetFolderPath(fixture.Packages, "Cache.Package", NuGetVersion.Parse("1.0.0"));
		var second = NugetUtility.GetFolderPath(Path.Combine(fixture.Root, "second-cache"), "Cache.Package", NuGetVersion.Parse("1.0.0"));
		Assert.Equal(Path.Combine(fixture.Packages, "cache.package", "1.0.0"), first);
		Assert.Equal(Path.Combine(fixture.Root, "second-cache", "cache.package", "1.0.0"), second);
	}

	[Fact]
	public async Task Metadata_SamePackageInDifferentCachesRemainsIsolated()
	{
		using var first = new DeploymentFixture();
		using var second = new DeploymentFixture();
		first.Package("Cache.Same", dependencies: [("First.Child", "[1.0.0]")]);
		second.Package("Cache.Same", dependencies: [("Second.Child", "[1.0.0]")]);
		var firstMetadata = await NugetUtility.GetPackageMetadataAsync(first.Variables, "Cache.Same", "1.0.0", TestContext.Current.CancellationToken);
		var secondMetadata = await NugetUtility.GetPackageMetadataAsync(second.Variables, "Cache.Same", "1.0.0", TestContext.Current.CancellationToken);
		Assert.Equal("First.Child", Assert.Single(Assert.Single(firstMetadata.DependencySets).Packages).Id);
		Assert.Equal("Second.Child", Assert.Single(Assert.Single(secondMetadata.DependencySets).Packages).Id);
		Assert.Equal(second.Packages + Path.DirectorySeparatorChar + Path.Combine("cache.same", "1.0.0"), await NugetUtility.DownloadPackageAsync(second.Variables, "Cache.Same", NuGetVersion.Parse("1.0.0"), TestContext.Current.CancellationToken));
	}

	[Fact]
	public async Task Dependencies_ChangedIgnorePolicyIsNotCached()
	{
		using var fixture = new DeploymentFixture();
		fixture.Package("Policy.Root", dependencies: [("Policy.Child", "[1.0.0]")]);
		fixture.Package("Policy.Child");
		var manifest = fixture.Manifest("nuget:Policy.Root@1.0.0");
		var deployer = fixture.CreateDeployer();
		Assert.Equal(0, (await deployer.DeployAsync(manifest, fixture.Destination, TestContext.Current.CancellationToken)).Failures);
		Assert.Equal(["Policy.Child", "Policy.Root"], deployer.Plan.Packages.Select(package => package.Id).Order(StringComparer.Ordinal).ToArray());

		deployer.Variables["ignoreDependentPrefix"] = "Policy.Child";
		var secondDestination = Path.Combine(fixture.Root, "second-target");
		var result = await deployer.DeployAsync(manifest, secondDestination, TestContext.Current.CancellationToken);

		Assert.Equal(0, result.Failures);
		Assert.Equal("Policy.Root", Assert.Single(deployer.Plan.Packages).Id);
		Assert.Equal("Policy.Root.dll", Path.GetFileName(Assert.Single(Directory.GetFiles(secondDestination))));
		Assert.Equal("Policy.Child@1.0.0", File.ReadAllText(Path.Combine(fixture.Destination, "Policy.Child.dll")));
	}

	[Theory]
	[InlineData("net8.0", "net8.0")]
	[InlineData("net9.0", "net8.0")]
	[InlineData("net10.0", "net10.0")]
	public async Task Assets_NearestFrameworkIsSelected(string requested, string expected)
	{
		using var fixture = new DeploymentFixture();
		var package = fixture.Package("Nearest.Asset", framework: "netstandard2.0");
		fixture.Write("packages/nearest.asset/1.0.0/lib/net8.0/Nearest.Asset.dll", "eight");
		fixture.Write("packages/nearest.asset/1.0.0/lib/net10.0/Nearest.Asset.dll", "ten");
		fixture.Variables["Framework"] = requested;
		var deployer = fixture.CreateDeployer();

		var result = await deployer.DeployAsync(fixture.Manifest("nuget:Nearest.Asset@1.0.0"), fixture.Destination, TestContext.Current.CancellationToken);

		Assert.Equal(0, result.Failures);
		Assert.Equal(Path.Combine(package, "lib", expected, "Nearest.Asset.dll"), Assert.Single(deployer.Plan.Operations).Source);
		Assert.Equal(expected == "net10.0" ? "ten" : "eight", File.ReadAllText(Path.Combine(fixture.Destination, "Nearest.Asset.dll")));
	}

	[Theory]
	[InlineData("win", "x64")]
	[InlineData("linux", "x64")]
	[InlineData("linux-musl", "x64")]
	[InlineData("osx", "arm64")]
	public async Task Deploy_RuntimeImplementationOverridesSamePackageLibrary(string platform, string architecture)
	{
		using var fixture = new DeploymentFixture();
		fixture.Package("Runtime.Root");
		fixture.Write($"packages/runtime.root/1.0.0/runtimes/{platform}-{architecture}/lib/net10.0/Runtime.Root.dll", "runtime implementation");
		fixture.Write($"packages/runtime.root/1.0.0/runtimes/{platform}-{architecture}/native/library.native", "native implementation");
		fixture.Write("packages/runtime.root/1.0.0/runtimes/other-x86/native/wrong.native", "wrong architecture");
		fixture.Variables["platform"] = platform;
		fixture.Variables["architecture"] = architecture;
		var result = await fixture.CreateDeployer().DeployAsync(fixture.Manifest("nuget:Runtime.Root@1.0.0"), fixture.Destination, TestContext.Current.CancellationToken);
		Assert.Equal(0, result.Failures);
		Assert.Equal("runtime implementation", File.ReadAllText(Path.Combine(fixture.Destination, "Runtime.Root.dll")));
		Assert.Equal("native implementation", File.ReadAllText(Path.Combine(fixture.Destination, "library.native")));
		Assert.False(File.Exists(Path.Combine(fixture.Destination, "wrong.native")));
	}

	[Fact]
	public async Task Deploy_CrossPackageConflictingAssetFails()
	{
		using var fixture = new DeploymentFixture();
		fixture.Package("Assets.Root", dependencies: [("Assets.Child", "[1.0.0]")]);
		fixture.Package("Assets.Child");
		fixture.Write("packages/assets.root/1.0.0/lib/net10.0/Shared.dll", "root implementation");
		fixture.Write("packages/assets.child/1.0.0/lib/net10.0/Shared.dll", "child implementation");
		var result = await fixture.CreateDeployer().DeployAsync(fixture.Manifest("nuget:Assets.Root@1.0.0"), fixture.Destination, TestContext.Current.CancellationToken);
		Assert.True(result.Failures > 0);
		Assert.Contains("Shared.dll", fixture.Log.ToString());
		Assert.Contains("Assets.Root", fixture.Log.ToString(), StringComparison.OrdinalIgnoreCase);
		Assert.Contains("Assets.Child", fixture.Log.ToString(), StringComparison.OrdinalIgnoreCase);
	}

	[Theory]
	[InlineData("net10.0", "net9.0^", true)]
	[InlineData("net10.0", "net10.0", true)]
	[InlineData("net10.0", "net8.0,net9.0", false)]
	[InlineData("net8.0", "net9.0^", false)]
	[InlineData("net10.0-windows10.0.19041", "net10.0-windows10.0.17763^", true)]
	[InlineData("net10.0-windows10.0.19041", "net10.0^", false)]
	public async Task Deploy_FrameworkFiltersSelectOnlyMatchingRoots(string framework, string filter, bool selected)
	{
		using var fixture = new DeploymentFixture();
		fixture.Package("Filter.Root", framework: "net8.0");
		fixture.Variables["Framework"] = framework;
		var deployer = fixture.CreateDeployer();

		var result = await deployer.DeployAsync(fixture.Manifest($"nuget:Filter.Root@1.0.0 = <framework:{filter}>"), fixture.Destination, TestContext.Current.CancellationToken);

		Assert.Equal(0, result.Failures);
		Assert.Equal(selected ? 1 : 0, result.Successes);
		Assert.Equal(selected ? new[] { "Filter.Root" } : [], deployer.Plan.Packages.Select(package => package.Id).ToArray());
		Assert.Equal(selected ? new[] { "Filter.Root.dll" } : [], Directory.GetFiles(fixture.Destination).Select(Path.GetFileName).ToArray());
	}

	[Fact]
	public async Task Deploy_DependencyBacktrackingAndLockedReplayKeepConsistentVersions()
	{
		using var fixture = new DeploymentFixture();
		fixture.Package("Backtrack.Root", dependencies: [("Backtrack.Choice", "[1.0.0,3.0.0)"), ("Backtrack.Fixed", "[1.0.0]")]);
		fixture.Package("Backtrack.Choice", dependencies: [("Backtrack.Leaf", "[1.0.0]")]);
		fixture.Package("Backtrack.Choice", "2.0.0", dependencies: [("Backtrack.Leaf", "[2.0.0]")]);
		fixture.Package("Backtrack.Fixed", dependencies: [("Backtrack.Leaf", "[2.0.0]")]);
		fixture.Package("Backtrack.Leaf");
		fixture.Package("Backtrack.Leaf", "2.0.0");
		fixture.Variables["lockFile"] = Path.Combine(fixture.Root, "deployment.lock.json");
		var manifest = fixture.Manifest("nuget:Backtrack.Root@1.0.0");
		var deployer = fixture.CreateDeployer();

		var initial = await deployer.DeployAsync(manifest, fixture.Destination, TestContext.Current.CancellationToken);

		Assert.Equal(0, initial.Failures);
		Assert.Equal(4, initial.Successes);
		Assert.Equal("2.0.0", Assert.Single(deployer.Plan.Packages, package => package.Id == "Backtrack.Choice").Version);
		Assert.Equal("2.0.0", Assert.Single(deployer.Plan.Packages, package => package.Id == "Backtrack.Leaf").Version);
		Assert.Equal("Backtrack.Choice@2.0.0", File.ReadAllText(Path.Combine(fixture.Destination, "Backtrack.Choice.dll")));
		var lockBytes = File.ReadAllBytes(fixture.Variables["lockFile"]);

		// 新增更低且可解的候选；锁定重跑仍必须使用原求解结果。
		fixture.Package("Backtrack.Choice", "1.5.0", dependencies: [("Backtrack.Leaf", "[2.0.0]")]);
		deployer.Variables["locked"] = "true";
		var locked = await deployer.DeployAsync(manifest, fixture.Destination, TestContext.Current.CancellationToken);

		Assert.Equal(0, locked.Failures);
		Assert.Equal(4, deployer.Plan.Packages.Count);
		Assert.Equal("2.0.0", Assert.Single(deployer.Plan.Packages, package => package.Id == "Backtrack.Choice").Version);
		Assert.Equal("Backtrack.Leaf@2.0.0", File.ReadAllText(Path.Combine(fixture.Destination, "Backtrack.Leaf.dll")));
		Assert.Equal(lockBytes, File.ReadAllBytes(fixture.Variables["lockFile"]));
	}

	[Fact]
	public async Task Metadata_ChangingCacheRootOnSameVariablesRefreshesVersionsAndMetadata()
	{
		using var first = new DeploymentFixture();
		using var second = new DeploymentFixture();
		first.Package("Cache.Context", dependencies: [("First.Child", "[1.0.0]")]);
		second.Package("Cache.Context", dependencies: [("Second.Child", "[1.0.0]")]);
		second.Package("Cache.Context", "2.0.0");
		var variables = first.Variables;
		var initial = await NugetUtility.GetPackageMetadataAsync(variables, "Cache.Context", "latest", TestContext.Current.CancellationToken);
		var initialExact = await NugetUtility.GetPackageMetadataAsync(variables, "Cache.Context", "1.0.0", TestContext.Current.CancellationToken);
		Assert.Equal("1.0.0", initial.Identity.Version.ToNormalizedString());
		Assert.Equal("First.Child", Assert.Single(Assert.Single(initialExact.DependencySets).Packages).Id);

		variables["NuGet_Packages"] = second.Packages;
		var latest = await NugetUtility.GetPackageMetadataAsync(variables, "Cache.Context", "latest", TestContext.Current.CancellationToken);
		var exact = await NugetUtility.GetPackageMetadataAsync(variables, "Cache.Context", "1.0.0", TestContext.Current.CancellationToken);
		var downloaded = await NugetUtility.DownloadPackageAsync(variables, "Cache.Context", NuGetVersion.Parse("1.0.0"), TestContext.Current.CancellationToken);

		Assert.Equal("2.0.0", latest.Identity.Version.ToNormalizedString());
		Assert.Equal("Second.Child", Assert.Single(Assert.Single(exact.DependencySets).Packages).Id);
		Assert.Equal(Path.Combine(second.Packages, "cache.context", "1.0.0"), downloaded);
		Assert.Equal("First.Child", Assert.Single(Assert.Single(initialExact.DependencySets).Packages).Id);
	}

	[Fact]
	public async Task Deploy_NugetCancellationAllowsSameInstanceRetry()
	{
		using var fixture = new DeploymentFixture();
		fixture.Package("Cancelled.Root", dependencies: [("Cancelled.Child", "[1.0.0]")]);
		fixture.Package("Cancelled.Child");
		var manifest = fixture.Manifest("nuget:Cancelled.Root@1.0.0");
		var deployer = fixture.CreateDeployer();
		using var cancellation = new CancellationTokenSource();
		cancellation.Cancel();

		await Assert.ThrowsAnyAsync<OperationCanceledException>(() => deployer.DeployAsync(manifest, fixture.Destination, cancellation.Token));
		Assert.Empty(Directory.GetFiles(fixture.Destination));

		var result = await deployer.DeployAsync(manifest, fixture.Destination, TestContext.Current.CancellationToken);

		Assert.Equal(0, result.Failures);
		Assert.Equal(2, result.Successes);
		Assert.Equal(["Cancelled.Child", "Cancelled.Root"], deployer.Plan.Packages.Select(package => package.Id).Order(StringComparer.Ordinal).ToArray());
		Assert.Equal("Cancelled.Child@1.0.0", File.ReadAllText(Path.Combine(fixture.Destination, "Cancelled.Child.dll")));
	}
}
