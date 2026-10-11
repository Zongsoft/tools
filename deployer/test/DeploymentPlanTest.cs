using Xunit;

namespace Zongsoft.Tools.Deployer.Tests;

public class DeploymentPlanTest
{
	[Fact]
	public async Task Deploy_ReportSaveFailureMarksPlanUnsuccessfulAsync()
	{
		using var fixture = new DeploymentFixture();
		fixture.Write("source/file.txt", "deployed before report failure");
		var reportDirectory = Path.Combine(fixture.Root, "reports");
		Directory.CreateDirectory(reportDirectory);
		fixture.Variables["report"] = (reportDirectory).Replace('\\', '/');
		var deployer = fixture.CreateDeployer();
		var result = await deployer.DeployAsync((fixture.Manifest("file.txt")).Replace('\\', '/'), (fixture.OutputDirectory).Replace('\\', '/'), TestContext.Current.CancellationToken);
		Assert.True(result.Failures > 0);
		Assert.False(deployer.Plan.Succeeded);
		Assert.Equal(1, result.Successes);
		Assert.Equal("deployed before report failure", File.ReadAllText(Path.Combine(fixture.OutputDirectory, "file.txt")));
		Assert.Contains(deployer.Plan.Diagnostics, diagnostic => diagnostic.Contains(reportDirectory, StringComparison.OrdinalIgnoreCase));
		Assert.True(Directory.Exists(reportDirectory));
	}

	[Theory]
	[InlineData(false)]
	[InlineData(true)]
	public async Task Deploy_ReportAndLockSamePathRejectsBeforeCopyAndPreservesLockAsync(bool invalidManifest)
	{
		using var fixture = new DeploymentFixture();
		fixture.Write("source/file.txt", "locked content");
		var manifest = fixture.Manifest("file.txt");
		var lockFile = Path.Combine(fixture.Root, "deployment.lock.json");
		fixture.Variables["lockFile"] = (lockFile).Replace('\\', '/');
		fixture.Variables["overwrite"] = "alway";
		Assert.Equal(0, (await fixture.CreateDeployer().DeployAsync((manifest).Replace('\\', '/'), (fixture.OutputDirectory).Replace('\\', '/'), TestContext.Current.CancellationToken)).Failures);
		var originalLock = File.ReadAllBytes(lockFile);
		fixture.Variables["report"] = (lockFile).Replace('\\', '/');
		fixture.Variables["locked"] = "true";

		if(invalidManifest)
			manifest = fixture.Manifest("unknown:input", "source/invalid.deploy");
		var deployer = fixture.CreateDeployer();
		var result = await deployer.DeployAsync((manifest).Replace('\\', '/'), (fixture.OutputDirectory).Replace('\\', '/'), TestContext.Current.CancellationToken);
		Assert.True(result.Failures > 0);
		Assert.False(deployer.Plan.Succeeded);
		Assert.Equal(0, result.Successes);
		Assert.Equal(originalLock, File.ReadAllBytes(lockFile));
		Assert.Equal("locked content", File.ReadAllText(Path.Combine(fixture.OutputDirectory, "file.txt")));
	}

	[Fact]
	public async Task Deploy_DuplicateRecordDoesNotRevokePruneOwnershipAsync()
	{
		using var fixture = new DeploymentFixture();
		fixture.Package("Owned.Root");
		fixture.Variables["overwrite"] = "alway";
		var report = Path.Combine(fixture.Root, "previous.json");
		fixture.Variables["report"] = (report).Replace('\\', '/');
		var initial = await fixture.CreateDeployer().DeployAsync((fixture.Manifest("nuget:Owned.Root@1.0.0\nnuget:Owned.Root@latest")).Replace('\\', '/'), (fixture.OutputDirectory).Replace('\\', '/'), TestContext.Current.CancellationToken);
		Assert.Equal(0, initial.Failures);
		Assert.Equal(1, initial.Successes);
		Assert.Equal(1, initial.Skipped);
		Assert.Equal(["Copied", "Duplicate"], DeploymentPlan.Load(report).Operations.Select(operation => operation.Status).ToArray());
		fixture.Variables.Remove("report");
		fixture.Variables["previous"] = report.Replace('\\', '/');
		fixture.Variables["prune"] = "true";
		var result = await fixture.CreateDeployer().DeployAsync((fixture.Manifest("", "source/empty.deploy")).Replace('\\', '/'), (fixture.OutputDirectory).Replace('\\', '/'), TestContext.Current.CancellationToken);
		Assert.Equal(0, result.Failures);
		Assert.Equal(1, result.Deleted);
		Assert.False(File.Exists(Path.Combine(fixture.OutputDirectory, "Owned.Root.dll")));
	}

	[Fact]
	public async Task Deploy_LockedLatestKeepsRootAndTransitiveVersionsAfterCacheGrowsAsync()
	{
		using var fixture = new DeploymentFixture();
		fixture.Package("Locked.Root", dependencies: [("Locked.Child", "[1.0.0,)")]);
		fixture.Package("Locked.Child");
		var manifest = fixture.Manifest("nuget:Locked.Root@latest");
		var lockFile = Path.Combine(fixture.Root, "deployment.lock.json");
		fixture.Variables["lockFile"] = (lockFile).Replace('\\', '/');
		Assert.Equal(0, (await fixture.CreateDeployer().DeployAsync((manifest).Replace('\\', '/'), (fixture.OutputDirectory).Replace('\\', '/'), TestContext.Current.CancellationToken)).Failures);
		var lockBytes = File.ReadAllBytes(lockFile);
		fixture.Package("Locked.Root", "2.0.0", dependencies: [("Locked.Child", "[2.0.0,)")]);
		fixture.Package("Locked.Child", "2.0.0");
		fixture.Variables["locked"] = "true";
		var deployer = fixture.CreateDeployer();
		var result = await deployer.DeployAsync((manifest).Replace('\\', '/'), (fixture.OutputDirectory).Replace('\\', '/'), TestContext.Current.CancellationToken);
		Assert.Equal(0, result.Failures);
		Assert.Equal("1.0.0", Assert.Single(deployer.Plan.Packages, package => package.Id == "Locked.Root").Version);
		Assert.Equal("1.0.0", Assert.Single(deployer.Plan.Packages, package => package.Id == "Locked.Child").Version);
		Assert.Equal("Locked.Root@1.0.0", File.ReadAllText(Path.Combine(fixture.OutputDirectory, "Locked.Root.dll")));
		Assert.Equal("Locked.Child@1.0.0", File.ReadAllText(Path.Combine(fixture.OutputDirectory, "Locked.Child.dll")));
		Assert.Equal(lockBytes, File.ReadAllBytes(lockFile));
	}

	[Fact]
	public async Task Deploy_LockedRunRejectsChangedUndeployedPackageContentAsync()
	{
		using var fixture = new DeploymentFixture();
		fixture.Package("Integrity.Root");
		var unused = fixture.Write("packages/integrity.root/1.0.0/docs/readme.txt", "original documentation");
		var manifest = fixture.Manifest("nuget:Integrity.Root@1.0.0");
		var lockFile = Path.Combine(fixture.Root, "deployment.lock.json");
		fixture.Variables["lockFile"] = (lockFile).Replace('\\', '/');
		Assert.Equal(0, (await fixture.CreateDeployer().DeployAsync((manifest).Replace('\\', '/'), (fixture.OutputDirectory).Replace('\\', '/'), TestContext.Current.CancellationToken)).Failures);
		Assert.False(File.Exists(Path.Combine(fixture.OutputDirectory, "readme.txt")));
		File.WriteAllText(unused, "changed documentation");
		fixture.Variables["locked"] = "true";
		var result = await fixture.CreateDeployer().DeployAsync((manifest).Replace('\\', '/'), (fixture.OutputDirectory).Replace('\\', '/'), TestContext.Current.CancellationToken);
		Assert.True(result.Failures > 0);
		Assert.Equal(0, result.Successes);
		Assert.Equal("Integrity.Root@1.0.0", File.ReadAllText(Path.Combine(fixture.OutputDirectory, "Integrity.Root.dll")));
	}

	[Theory]
	[InlineData("unknown:input", "unknown")]
	[InlineData("${MissingVariable}", "MissingVariable")]
	public async Task Deploy_FailedPlanWritesUnsuccessfulReportWithDiagnosticsAsync(string entry, string diagnostic)
	{
		using var fixture = new DeploymentFixture();
		var report = Path.Combine(fixture.Root, "failed.json");
		fixture.Variables["report"] = (report).Replace('\\', '/');
		var manifest = fixture.Manifest(entry);
		var result = await fixture.CreateDeployer().DeployAsync((manifest).Replace('\\', '/'), (fixture.OutputDirectory).Replace('\\', '/'), TestContext.Current.CancellationToken);
		Assert.True(result.Failures > 0);
		var plan = DeploymentPlan.Load(report);
		Assert.False(plan.Succeeded);
		Assert.NotEmpty(plan.Diagnostics);
		Assert.Contains(plan.Diagnostics, message => message.Contains(diagnostic, StringComparison.OrdinalIgnoreCase));
		Assert.Empty(Directory.GetFiles(fixture.OutputDirectory));
	}

	[Fact]
	public async Task Deploy_ReportCannotOverwriteManifestEvenWhenPlanFailsAsync()
	{
		using var fixture = new DeploymentFixture();
		var manifest = fixture.Manifest("unknown:input");
		fixture.Variables["report"] = (manifest).Replace('\\', '/');
		var original = File.ReadAllBytes(manifest);
		var result = await fixture.CreateDeployer().DeployAsync((manifest).Replace('\\', '/'), (fixture.OutputDirectory).Replace('\\', '/'), TestContext.Current.CancellationToken);
		Assert.True(result.Failures > 0);
		Assert.Equal(original, File.ReadAllBytes(manifest));
	}

	[Fact]
	public async Task Deploy_PruneDoesNotOwnFileDeletedByPreviousDeploymentAsync()
	{
		using var fixture = new DeploymentFixture();
		fixture.Write("source/file.txt", "same content");
		var report = Path.Combine(fixture.Root, "previous.json");
		fixture.Variables["report"] = (report).Replace('\\', '/');
		Assert.Equal(0, (await fixture.CreateDeployer().DeployAsync((fixture.Manifest("file.txt\ndelete:file.txt")).Replace('\\', '/'), (fixture.OutputDirectory).Replace('\\', '/'), TestContext.Current.CancellationToken)).Failures);
		var target = fixture.Write("target/file.txt", "same content");
		fixture.Variables.Remove("report");
		fixture.Variables["previous"] = report.Replace('\\', '/');
		fixture.Variables["prune"] = "true";
		var result = await fixture.CreateDeployer().DeployAsync((fixture.Manifest("", "source/next.deploy")).Replace('\\', '/'), (fixture.OutputDirectory).Replace('\\', '/'), TestContext.Current.CancellationToken);
		Assert.Equal(0, result.Failures);
		Assert.Equal(0, result.Deleted);
		Assert.Equal("same content", File.ReadAllText(target));
	}

	[Fact]
	public async Task Deploy_PruneDoesNotOwnPreexistingFileSkippedByNeverAsync()
	{
		using var fixture = new DeploymentFixture();
		fixture.Write("source/file.txt", "packaged content");
		var target = fixture.Write("target/file.txt", "user content");
		var report = Path.Combine(fixture.Root, "previous.json");
		fixture.Variables["report"] = (report).Replace('\\', '/');
		fixture.Variables["overwrite"] = "never";
		Assert.Equal(0, (await fixture.CreateDeployer().DeployAsync((fixture.Manifest("file.txt")).Replace('\\', '/'), (fixture.OutputDirectory).Replace('\\', '/'), TestContext.Current.CancellationToken)).Failures);
		fixture.Variables.Remove("report");
		fixture.Variables["previous"] = report.Replace('\\', '/');
		fixture.Variables["prune"] = "true";
		var result = await fixture.CreateDeployer().DeployAsync((fixture.Manifest("", "source/next.deploy")).Replace('\\', '/'), (fixture.OutputDirectory).Replace('\\', '/'), TestContext.Current.CancellationToken);
		Assert.Equal(0, result.Failures);
		Assert.Equal(0, result.Deleted);
		Assert.Equal("user content", File.ReadAllText(target));
	}

	[Fact]
	public async Task Deploy_DryRunCanSaveReportWithoutCreatingOutputAsync()
	{
		using var fixture = new DeploymentFixture();
		fixture.Write("source/file.txt", "source");
		fixture.Variables["dry_run"] = "true";
		var report = Path.Combine(fixture.Root, "reports", "preview.json");
		fixture.Variables["report"] = (report).Replace('\\', '/');
		var output = Path.Combine(fixture.Root, "preview-target");
		var result = await fixture.CreateDeployer().DeployAsync((fixture.Manifest("[nested]\nfile.txt")).Replace('\\', '/'), (output).Replace('\\', '/'), TestContext.Current.CancellationToken);
		Assert.Equal(0, result.Failures);
		Assert.False(Directory.Exists(output));
		var plan = DeploymentPlan.Load(report);
		Assert.True(plan.Succeeded);
		Assert.Equal(output, plan.Root);
		var operation = Assert.Single(plan.Operations);
		Assert.Equal(Path.Combine(output, "nested", "file.txt"), operation.Output);
		Assert.Equal(Path.Combine(fixture.Root, "source", "file.txt"), operation.Source);
	}

	[Fact]
	public async Task Deploy_ReportTracksPackageParentsAndFinalTargetHashAsync()
	{
		using var fixture = new DeploymentFixture();
		fixture.Package("Report.Root", dependencies: [("Report.Child", "[1.0.0]")]);
		fixture.Package("Report.Child");
		var report = Path.Combine(fixture.Root, "completed.json");
		fixture.Variables["report"] = (report).Replace('\\', '/');
		var result = await fixture.CreateDeployer().DeployAsync((fixture.Manifest("nuget:Report.Root@1.0.0")).Replace('\\', '/'), (fixture.OutputDirectory).Replace('\\', '/'), TestContext.Current.CancellationToken);
		Assert.Equal(0, result.Failures);
		var plan = DeploymentPlan.Load(report);
		Assert.True(plan.Succeeded);
		Assert.Equal(2, plan.Packages.Count);
		var child = Assert.Single(plan.Packages, package => package.Id == "Report.Child");
		Assert.Equal("1.0.0", child.Version);
		Assert.Contains(child.RequiredBy, parent => parent.Contains("Report.Root", StringComparison.OrdinalIgnoreCase));
		var operation = Assert.Single(plan.Operations, operation => operation.Output.EndsWith("Report.Child.dll"));
		Assert.Equal(Hash(operation.Output), operation.Hash);
		Assert.Equal(Hash(operation.Source), operation.SourceHash);
		Assert.Contains("Report.Child", operation.Package, StringComparison.OrdinalIgnoreCase);
	}

	[Fact]
	public async Task Deploy_LockedRunRejectsChangedSourceAndPreservesLockAsync()
	{
		using var fixture = new DeploymentFixture();
		var source = fixture.Write("source/file.txt", "original");
		var manifest = fixture.Manifest("file.txt");
		var lockFile = Path.Combine(fixture.Root, "deployment.lock.json");
		fixture.Variables["lockFile"] = (lockFile).Replace('\\', '/');
		Assert.Equal(0, (await fixture.CreateDeployer().DeployAsync((manifest).Replace('\\', '/'), (fixture.OutputDirectory).Replace('\\', '/'), TestContext.Current.CancellationToken)).Failures);
		var originalLock = File.ReadAllBytes(lockFile);
		fixture.Variables["locked"] = "true";
		Assert.Equal(0, (await fixture.CreateDeployer().DeployAsync((manifest).Replace('\\', '/'), (fixture.OutputDirectory).Replace('\\', '/'), TestContext.Current.CancellationToken)).Failures);
		File.WriteAllText(source, "changed");
		var result = await fixture.CreateDeployer().DeployAsync((manifest).Replace('\\', '/'), (fixture.OutputDirectory).Replace('\\', '/'), TestContext.Current.CancellationToken);
		Assert.True(result.Failures > 0);
		Assert.Equal("original", File.ReadAllText(Path.Combine(fixture.OutputDirectory, "file.txt")));
		Assert.Equal(originalLock, File.ReadAllBytes(lockFile));
	}

	[Theory]
	[InlineData(false, false, true)]
	[InlineData(true, false, false)]
	[InlineData(true, true, true)]
	public async Task Deploy_PreviousReportPrunesOnlyUnchangedOwnedFilesAsync(bool prune, bool modified, bool retained)
	{
		using var fixture = new DeploymentFixture();
		fixture.Write("source/old.txt", "owned content");
		fixture.Write("source/current.txt", "current content");
		fixture.Write("target/user.txt", "user owned");
		var report = Path.Combine(fixture.Root, "previous.json");
		fixture.Variables["report"] = (report).Replace('\\', '/');
		Assert.Equal(0, (await fixture.CreateDeployer().DeployAsync((fixture.Manifest("old.txt")).Replace('\\', '/'), (fixture.OutputDirectory).Replace('\\', '/'), TestContext.Current.CancellationToken)).Failures);
		var oldPath = Path.Combine(fixture.OutputDirectory, "old.txt");

		if(modified)
			File.WriteAllText(oldPath, "user modified");
		fixture.Variables.Remove("report");
		fixture.Variables["previous"] = report.Replace('\\', '/');
		fixture.Variables["prune"] = prune.ToString();
		var result = await fixture.CreateDeployer().DeployAsync((fixture.Manifest("current.txt", "source/next.deploy")).Replace('\\', '/'), (fixture.OutputDirectory).Replace('\\', '/'), TestContext.Current.CancellationToken);
		Assert.Equal(0, result.Failures);
		Assert.Equal(retained, File.Exists(oldPath));

		if(retained)
			Assert.Equal(modified ? "user modified" : "owned content", File.ReadAllText(oldPath));
		Assert.Equal("user owned", File.ReadAllText(Path.Combine(fixture.OutputDirectory, "user.txt")));
		Assert.Equal("current content", File.ReadAllText(Path.Combine(fixture.OutputDirectory, "current.txt")));
		Assert.Equal(retained ? 0 : 1, result.Deleted);
	}

	[Fact]
	public async Task Deploy_ForgedPreviousReportCannotDeleteOutsideRootAsync()
	{
		using var fixture = new DeploymentFixture();
		var outside = fixture.Write("outside.txt", "must remain");
		var report = Path.Combine(fixture.Root, "forged.json");
		new DeploymentPlan
		{
			Root = fixture.OutputDirectory,
			Succeeded = true,
			Operations = [new DeploymentOperation { Kind = "Copy", Output = outside, Hash = Hash(outside), Status = "Copied" }],
		}.Save(report);
		fixture.Variables["previous"] = report.Replace('\\', '/');
		fixture.Variables["prune"] = "true";
		var result = await fixture.CreateDeployer().DeployAsync((fixture.Manifest("")).Replace('\\', '/'), (fixture.OutputDirectory).Replace('\\', '/'), TestContext.Current.CancellationToken);
		Assert.True(result.Failures > 0);
		Assert.Equal("must remain", File.ReadAllText(outside));
		Assert.Equal(0, result.Deleted);
	}

	private static string Hash(string path)
	{
		using var stream = File.OpenRead(path);
		return Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(stream));
	}
}
