using Xunit;

namespace Zongsoft.Tools.Deployer.Tests;

public sealed class LocalSearchTest
{
	[Fact]
	public async Task Deploy_FileLinks_PreserveLogicalNamesAndReportResolvedSourcesAsync()
	{
		using var fixture = new DeploymentFixture();
		var target = fixture.Write("physical/actual.dat", "linked payload");
		Directory.CreateDirectory(Path.Combine(fixture.Root, "source"));
		var first = Path.Combine(fixture.Root, "source", "first.txt");
		var second = Path.Combine(fixture.Root, "source", "second.txt");
		File.CreateSymbolicLink(first, target);
		File.CreateSymbolicLink(second, target);
		var report = Path.Combine(fixture.Root, "report.json");
		fixture.Variables["report"] = report;
		var deployer = fixture.CreateDeployer();

		var result = await deployer.DeployAsync(fixture.Manifest("*.txt"), fixture.Destination, TestContext.Current.CancellationToken);

		Assert.Equal(0, result.Failures);
		Assert.Equal(2, result.Successes);
		Assert.Equal(new[] { "first.txt", "second.txt" }, Directory.GetFiles(fixture.Destination).Select(Path.GetFileName).Order(StringComparer.Ordinal));
		Assert.Equal("linked payload", File.ReadAllText(Path.Combine(fixture.Destination, "first.txt")));
		Assert.Equal("linked payload", File.ReadAllText(Path.Combine(fixture.Destination, "second.txt")));
		var operations = DeploymentPlan.Load(report).Operations;
		Assert.Equal(new[] { first, second }, operations.Select(operation => operation.Source));
		Assert.All(operations, operation => Assert.Equal(target, operation.ResolvedSource));
		Assert.All(Directory.GetFiles(fixture.Destination), path => Assert.Null(new FileInfo(path).LinkTarget));
	}

	[Theory]
	[InlineData(false)]
	[InlineData(true)]
	public async Task Deploy_ExplicitPackageFileLink_PreservesLogicalNameAndResolvedSourceAsync(bool outsideCache)
	{
		using var fixture = new DeploymentFixture();
		var package = fixture.Package("Linked.Source");
		var target = fixture.Write(outsideCache ? "physical/actual.dat" : "packages/linked.source/1.0.0/actual.dat", "explicit package linked content");
		var link = Path.Combine(package, "payload.txt");
		File.CreateSymbolicLink(link, target);
		var deployer = fixture.CreateDeployer();

		var result = await deployer.DeployAsync(fixture.Manifest("nuget:Linked.Source@1.0.0/payload.txt"), fixture.Destination, TestContext.Current.CancellationToken);

		Assert.Equal(0, result.Failures);
		Assert.Equal(1, result.Successes);
		var operation = Assert.Single(deployer.Plan.Operations);
		Assert.Equal(link, operation.Source);
		Assert.Equal(target, operation.ResolvedSource);
		Assert.Equal(Path.Combine(fixture.Destination, "payload.txt"), operation.Destination);
		Assert.Equal("explicit package linked content", File.ReadAllText(operation.Destination));
		Assert.Equal(new[] { "payload.txt" }, Directory.GetFiles(fixture.Destination).Select(Path.GetFileName));
		Assert.Null(new FileInfo(operation.Destination).LinkTarget);
	}

	[Fact]
	public async Task Deploy_ExplicitPackagePathEscapesLogicalRoot_RejectsBeforeWritingAsync()
	{
		using var fixture = new DeploymentFixture();
		fixture.Package("Linked.Source");
		var outside = fixture.Write("packages/linked.source/outside.txt", "must not deploy");

		var result = await fixture.CreateDeployer().DeployAsync(fixture.Manifest("nuget:Linked.Source@1.0.0/../outside.txt"), fixture.Destination, TestContext.Current.CancellationToken);

		Assert.True(result.Failures > 0);
		Assert.Equal(0, result.Successes);
		Assert.Empty(Directory.GetFiles(fixture.Destination));
		Assert.Equal("must not deploy", File.ReadAllText(outside));
	}

	[Fact]
	public async Task Deploy_SelectedDirectoryLink_ExpandsContentsAndSkipsNestedDirectoryLinksAsync()
	{
		using var fixture = new DeploymentFixture();
		fixture.Write("physical/assets/ordinary/data.txt", "ordinary target");
		var file = fixture.Write("physical/value.dat", "linked target");
		fixture.Write("physical/hidden/secret.txt", "must not deploy");
		Directory.CreateDirectory(Path.Combine(fixture.Root, "source"));
		Directory.CreateSymbolicLink(Path.Combine(fixture.Root, "source/assets"), Path.Combine(fixture.Root, "physical/assets"));
		Directory.CreateSymbolicLink(Path.Combine(fixture.Root, "physical/assets/nested"), Path.Combine(fixture.Root, "physical/hidden"));
		File.CreateSymbolicLink(Path.Combine(fixture.Root, "physical/assets/visible.txt"), file);

		var result = await fixture.CreateDeployer().DeployAsync(fixture.Manifest("[assets]\nassets"), fixture.Destination, TestContext.Current.CancellationToken);

		Assert.Equal(0, result.Failures);
		Assert.Equal(2, result.Successes);
		Assert.Equal("ordinary target", File.ReadAllText(Path.Combine(fixture.Destination, "assets/ordinary/data.txt")));
		Assert.Equal("linked target", File.ReadAllText(Path.Combine(fixture.Destination, "assets/visible.txt")));
		Assert.False(Directory.Exists(Path.Combine(fixture.Destination, "assets/nested")));
		Assert.Equal(2, Directory.GetFiles(fixture.Destination, "*", SearchOption.AllDirectories).Length);
	}

	[Fact]
	public async Task Deploy_DirectoryWildcard_SelectsLinkAsTopLevelPayloadAsync()
	{
		using var fixture = new DeploymentFixture();
		fixture.Write("physical/content/item.txt", "selected directory target");
		Directory.CreateDirectory(Path.Combine(fixture.Root, "source/assets"));
		Directory.CreateSymbolicLink(Path.Combine(fixture.Root, "source/assets/visible"), Path.Combine(fixture.Root, "physical/content"));

		var result = await fixture.CreateDeployer().DeployAsync(fixture.Manifest("assets/*"), fixture.Destination, TestContext.Current.CancellationToken);

		Assert.Equal(0, result.Failures);
		Assert.Equal(1, result.Successes);
		Assert.Equal("selected directory target", File.ReadAllText(Path.Combine(fixture.Destination, "visible/item.txt")));
		Assert.False(Directory.Exists(Path.Combine(fixture.Destination, "content")));
	}

	[Fact]
	public async Task Deploy_RecursiveFilePattern_DoesNotTraverseDirectoryLinksAsync()
	{
		using var fixture = new DeploymentFixture();
		fixture.Write("source/ordinary/data.txt", "ordinary source");
		fixture.Write("physical/hidden/secret.txt", "must not deploy");
		Directory.CreateSymbolicLink(Path.Combine(fixture.Root, "source/linked"), Path.Combine(fixture.Root, "physical/hidden"));

		var result = await fixture.CreateDeployer().DeployAsync(fixture.Manifest("**/*.txt"), fixture.Destination, TestContext.Current.CancellationToken);

		Assert.Equal(0, result.Failures);
		Assert.Equal(1, result.Successes);
		Assert.Equal("ordinary source", File.ReadAllText(Path.Combine(fixture.Destination, "ordinary/data.txt")));
		Assert.False(Directory.Exists(Path.Combine(fixture.Destination, "linked")));
	}

	[Fact]
	public async Task Deploy_LinkedManifest_UsesLogicalDirectoryForImportsAndSourcesAsync()
	{
		using var fixture = new DeploymentFixture();
		var physical = fixture.Manifest("#@import child.deploy\nroot.txt", "physical/config.data");
		fixture.Manifest("unknown:wrong", "physical/child.deploy");
		fixture.Write("physical/root.txt", "wrong root");
		fixture.Write("source/root.txt", "logical root");
		fixture.Write("source/imported.txt", "logical import");
		fixture.Manifest("[imported]\nimported.txt", "source/child.deploy");
		var link = Path.Combine(fixture.Root, "source/linked.deploy");
		File.CreateSymbolicLink(link, physical);

		var result = await fixture.CreateDeployer().DeployAsync(fixture.Manifest("linked.deploy"), fixture.Destination, TestContext.Current.CancellationToken);

		Assert.Equal(0, result.Failures);
		Assert.Equal(2, result.Successes);
		Assert.Equal("logical root", File.ReadAllText(Path.Combine(fixture.Destination, "root.txt")));
		Assert.Equal("logical import", File.ReadAllText(Path.Combine(fixture.Destination, "imported/imported.txt")));
		Assert.False(File.Exists(Path.Combine(fixture.Destination, "linked.deploy")));
		Assert.False(File.Exists(Path.Combine(fixture.Destination, "config.data")));
	}

	[Fact]
	public async Task Deploy_LockedLinkRetargetedToIdenticalContent_FailsBeforeWritingAsync()
	{
		using var fixture = new DeploymentFixture();
		var original = fixture.Write("physical/first.dat", "same bytes");
		var replacement = fixture.Write("physical/second.dat", "same bytes");
		Directory.CreateDirectory(Path.Combine(fixture.Root, "source"));
		var link = Path.Combine(fixture.Root, "source/payload.txt");
		File.CreateSymbolicLink(link, original);
		var manifest = fixture.Manifest("payload.txt");
		var lockFile = Path.Combine(fixture.Root, "deployment.lock.json");
		fixture.Variables["lockFile"] = lockFile;
		fixture.Variables["overwrite"] = "alway";
		Assert.Equal(0, (await fixture.CreateDeployer().DeployAsync(manifest, fixture.Destination, TestContext.Current.CancellationToken)).Failures);
		var lockBytes = File.ReadAllBytes(lockFile);
		var destination = fixture.Write("target/payload.txt", "must remain untouched");
		File.Delete(link);
		File.CreateSymbolicLink(link, replacement);
		fixture.Variables["locked"] = "true";

		var result = await fixture.CreateDeployer().DeployAsync(manifest, fixture.Destination, TestContext.Current.CancellationToken);

		Assert.True(result.Failures > 0);
		Assert.Equal(0, result.Successes);
		Assert.Equal("must remain untouched", File.ReadAllText(destination));
		Assert.Equal(lockBytes, File.ReadAllBytes(lockFile));
	}

	[Fact]
	public async Task Deploy_LinkRetargetedAfterPlanValidation_StopsBeforeCopyingLinkAsync()
	{
		using var fixture = new DeploymentFixture();
		fixture.Write("source/first.txt", "first operation");
		var original = fixture.Write("physical/first.dat", "same bytes");
		var replacement = fixture.Write("physical/second.dat", "same bytes");
		var link = Path.Combine(fixture.Root, "source/payload.txt");
		File.CreateSymbolicLink(link, original);
		fixture.Variables["explain"] = "true";
		var changed = false;
		using var writer = new CallbackWriter(() =>
		{
			// 在第一项实际写入后的通知中改向，避免依赖定时或并行竞态。
			if(changed || !File.Exists(Path.Combine(fixture.Destination, "first.txt")))
				return;

			File.Delete(link);
			File.CreateSymbolicLink(link, replacement);
			changed = true;
		});
		var deployer = new Deployer(fixture.Variables, writer);

		var result = await deployer.DeployAsync(fixture.Manifest("first.txt\npayload.txt"), fixture.Destination, TestContext.Current.CancellationToken);

		Assert.True(changed);
		Assert.Equal(1, result.Successes);
		Assert.Equal(1, result.Failures);
		Assert.Equal("first operation", File.ReadAllText(Path.Combine(fixture.Destination, "first.txt")));
		Assert.False(File.Exists(Path.Combine(fixture.Destination, "payload.txt")));
		Assert.Equal("Failed", deployer.Plan.Operations[1].Status);
		Assert.Equal(original, deployer.Plan.Operations[1].ResolvedSource);
	}

	[Fact]
	public async Task Deploy_SelectedDanglingLink_FailsBeforeAnyCopyAsync()
	{
		using var fixture = new DeploymentFixture();
		fixture.Write("source/ordinary.txt", "would copy first");
		File.CreateSymbolicLink(Path.Combine(fixture.Root, "source/missing.txt"), Path.Combine(fixture.Root, "physical/absent.dat"));

		var result = await fixture.CreateDeployer().DeployAsync(fixture.Manifest("ordinary.txt\nmissing.txt"), fixture.Destination, TestContext.Current.CancellationToken);

		Assert.True(result.Failures > 0);
		Assert.Equal(0, result.Successes);
		Assert.Empty(Directory.GetFiles(fixture.Destination));
	}

	[Theory]
	[InlineData(false, "orders/config")]
	[InlineData(true, "orders/assets/config")]
	public void GetFiles_FixedSegmentsBetweenWildcards_PreserveExpansionContract(bool expansion, string suffix)
	{
		using var fixture = new DeploymentFixture();
		var selected = fixture.Write("source/plugins/orders/assets/config/site.json", "selected");
		fixture.Write("source/plugins/orders/assets/ignored.txt", "excluded");
		if(expansion)
			fixture.Variables["expansion"] = "true";

		var match = Assert.Single(DeploymentUtility.GetFiles(Path.Combine(fixture.Root, "source/plugins/*/assets/**/*.json"), fixture.Variables, cancellation: TestContext.Current.CancellationToken));

		Assert.Equal(selected, match.Path);
		Assert.Equal(suffix, match.Suffix.Replace('\\', '/'));
	}

	[Fact]
	public async Task Deploy_MissingFileInsideValidDirectoryLink_WarnsAndContinuesAsync()
	{
		using var fixture = new DeploymentFixture();
		fixture.Write("source/ordinary.txt", "ordinary source");
		var physical = Path.Combine(fixture.Root, "physical");
		Directory.CreateDirectory(physical);
		Directory.CreateSymbolicLink(Path.Combine(fixture.Root, "source/linked"), physical);
		var deployer = fixture.CreateDeployer();

		var result = await deployer.DeployAsync(fixture.Manifest("linked/missing.txt\nordinary.txt"), fixture.Destination, TestContext.Current.CancellationToken);

		Assert.Equal(0, result.Failures);
		Assert.Equal(1, result.Skipped);
		Assert.Equal(1, result.Successes);
		Assert.Equal("ordinary source", File.ReadAllText(Path.Combine(fixture.Destination, "ordinary.txt")));
		Assert.Single(deployer.Plan.Diagnostics);
	}

	[Theory]
	[InlineData(false)]
	[InlineData(true)]
	public async Task Deploy_MissingSourceInsideDanglingDirectoryLink_FailsBeforeAnyCopyAsync(bool manifestArgument)
	{
		using var fixture = new DeploymentFixture();
		fixture.Write("source/ordinary.txt", "must not copy");
		Directory.CreateSymbolicLink(Path.Combine(fixture.Root, "source/linked"), Path.Combine(fixture.Root, "absent"));
		var deployer = fixture.CreateDeployer();
		var manifest = fixture.Manifest(manifestArgument ? "ordinary.txt" : "ordinary.txt\nlinked/missing.txt");
		var paths = manifestArgument ? new[] { manifest, Path.Combine(fixture.Root, "source/linked/.deploy") } : [manifest];

		var result = await deployer.DeployManyAsync(paths, fixture.Destination, TestContext.Current.CancellationToken);

		Assert.Equal(1, result.Failures);
		Assert.Equal(0, result.Successes);
		Assert.False(deployer.Plan.Succeeded);
		Assert.Empty(Directory.GetFiles(fixture.Destination));
	}

	private sealed class CallbackWriter(Action callback) : StringWriter
	{
		public override void WriteLine(string value)
		{
			base.WriteLine(value);
			callback();
		}
	}

}
