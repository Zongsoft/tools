using System.Globalization;

using Xunit;

namespace Zongsoft.Tools.Deployer.Tests;

public class DeploymentTest
{
	[Theory]
	[InlineData(false)]
	[InlineData(true)]
	public async Task Deploy_EmptyResolverPrefixCopiesExistingRelativeAndAbsoluteSourceAsync(bool absoluteSource)
	{
		using var fixture = new DeploymentFixture();
		fixture.Write("source/ordinary.txt", "ordinary source");
		var source = fixture.Write("source/default.txt", "default resolver source");
		var argument = absoluteSource ? source : "default.txt";
		var deployer = fixture.CreateDeployer();

		var result = await deployer.DeployAsync(fixture.Manifest($"ordinary.txt\n:{argument}"), fixture.Destination, TestContext.Current.CancellationToken);

		Assert.Equal(0, result.Failures);
		Assert.Equal(2, result.Successes);
		Assert.Equal(0, result.Skipped);
		Assert.True(deployer.Plan.Succeeded);
		Assert.Equal("ordinary source", File.ReadAllText(Path.Combine(fixture.Destination, "ordinary.txt")));
		Assert.Equal("default resolver source", File.ReadAllText(Path.Combine(fixture.Destination, "default.txt")));
		Assert.Equal(2, Directory.GetFiles(fixture.Destination).Length);
		Assert.Empty(deployer.Plan.Diagnostics);
	}

	[Theory]
	[InlineData("", false, false)]
	[InlineData("path:", false, false)]
	[InlineData("PATH:", false, false)]
	[InlineData("path:", true, false)]
	[InlineData("PATH:", true, false)]
	[InlineData("", false, true)]
	[InlineData("", true, true)]
	[InlineData("path:", true, true)]
	[InlineData("PATH:", false, true)]
	public async Task Deploy_PathResolverSyntaxCopiesRelativeAbsoluteAndExpandedSourceAsync(string prefix, bool absoluteSource, bool expandVariable)
	{
		using var fixture = new DeploymentFixture();
		var source = fixture.Write("source/file.txt", "path resolver content");
		var argument = absoluteSource ? source : "file.txt";

		if(expandVariable)
		{
			fixture.Variables["Input"] = argument;
			argument = "$(Input)";
		}

		var deployer = fixture.CreateDeployer();
		var result = await deployer.DeployAsync(fixture.Manifest(prefix + argument), fixture.Destination, TestContext.Current.CancellationToken);

		Assert.Equal(0, result.Failures);
		Assert.Equal(1, result.Successes);
		Assert.Equal(0, result.Skipped);
		Assert.Equal("path resolver content", File.ReadAllText(Path.Combine(fixture.Destination, "file.txt")));
		Assert.Equal(source, Assert.Single(deployer.Plan.Operations).Source);
		Assert.Empty(deployer.Plan.Diagnostics);
	}

	[Theory]
	[InlineData("en", "Warning: file not found; skipped: ")]
	[InlineData("zh-Hans", "警告：文件不存在，已跳过：")]
	public async Task Deploy_MissingManifestUsesLocalizedTemplateAndPathAsync(string culture, string expectedPrefix)
	{
		var originalCulture = CultureInfo.CurrentCulture;
		var originalUICulture = CultureInfo.CurrentUICulture;

		try
		{
			CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
			CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo(culture);
			using var fixture = new DeploymentFixture();
			var manifest = Path.Combine(fixture.Root, "missing-localization.deploy");
			var deployer = fixture.CreateDeployer();

			var result = await deployer.DeployAsync(manifest, fixture.Destination, TestContext.Current.CancellationToken);

			Assert.Equal(0, result.Failures);
			Assert.Equal(0, result.Successes);
			Assert.Equal(1, result.Skipped);
			Assert.True(deployer.Plan.Succeeded);
			Assert.Equal(expectedPrefix + manifest, Assert.Single(deployer.Plan.Diagnostics));
			Assert.Equal(expectedPrefix + manifest + Environment.NewLine, fixture.Log.ToString());
			Assert.Empty(Directory.GetFiles(fixture.Destination));
		}
		finally
		{
			CultureInfo.CurrentCulture = originalCulture;
			CultureInfo.CurrentUICulture = originalUICulture;
		}
	}

	[Fact]
	public async Task Deploy_ImportCycleFailsWithoutWritingAsync()
	{
		using var fixture = new DeploymentFixture();
		var manifest = fixture.Manifest("#@import child.deploy\nfile.txt");
		fixture.Manifest("#@import .deploy", "source/child.deploy");
		fixture.Write("source/file.txt", "source");
		using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
		cancellation.CancelAfter(TimeSpan.FromSeconds(10));
		var result = await fixture.CreateDeployer().DeployAsync(manifest, fixture.Destination, cancellation.Token);
		Assert.True(result.Failures > 0);
		Assert.Equal(0, result.Successes);
		Assert.Empty(Directory.GetFiles(fixture.Destination));
		Assert.Contains("child.deploy", fixture.Log.ToString());
	}

	[Fact]
	public async Task Deploy_ImportMergesEntriesAndKeepsOptionalMissingImportAsync()
	{
		using var fixture = new DeploymentFixture();
		fixture.Write("source/imported.txt", "imported source");
		fixture.Write("source/local.txt", "local source");
		fixture.Manifest("[plugins]\nimported.txt", "source/child.deploy");
		var manifest = fixture.Manifest("#@import child.deploy missing.deploy\n[plugins]\nlocal.txt");
		var result = await fixture.CreateDeployer().DeployAsync(manifest, fixture.Destination, TestContext.Current.CancellationToken);
		Assert.Equal(0, result.Failures);
		Assert.Equal(2, result.Successes);
		Assert.Equal("imported source", File.ReadAllText(Path.Combine(fixture.Destination, "plugins", "imported.txt")));
		Assert.Equal("local source", File.ReadAllText(Path.Combine(fixture.Destination, "plugins", "local.txt")));
	}

	[Fact]
	public async Task Deploy_DryRunPlansCopyAndDeleteWithoutChangingTargetAsync()
	{
		using var fixture = new DeploymentFixture();
		fixture.Write("source/file.txt", "new content");
		fixture.Write("target/obsolete.txt", "original content");
		fixture.Variables["dry-run"] = "true";
		var deployer = fixture.CreateDeployer();
		var result = await deployer.DeployAsync(fixture.Manifest("file.txt\ndelete:obsolete.txt"), fixture.Destination, TestContext.Current.CancellationToken);
		Assert.Equal(0, result.Failures);
		Assert.Equal(0, result.Successes);
		Assert.Equal(0, result.Deleted);
		Assert.False(File.Exists(Path.Combine(fixture.Destination, "file.txt")));
		Assert.Equal("original content", File.ReadAllText(Path.Combine(fixture.Destination, "obsolete.txt")));
		Assert.Equal(2, deployer.Plan.Operations.Count);
		Assert.Contains(deployer.Plan.Operations, operation => operation.Destination == Path.Combine(fixture.Destination, "file.txt"));
		Assert.Contains(deployer.Plan.Operations, operation => operation.Destination == Path.Combine(fixture.Destination, "obsolete.txt"));
	}

	[Fact]
	public async Task Deploy_LaterInvalidEntryPreventsEarlierCopyAsync()
	{
		using var fixture = new DeploymentFixture();
		fixture.Write("source/file.txt", "source");
		var result = await fixture.CreateDeployer().DeployAsync(fixture.Manifest("file.txt\nunknown:missing"), fixture.Destination, TestContext.Current.CancellationToken);
		Assert.True(result.Failures > 0);
		Assert.Equal(0, result.Successes);
		Assert.Empty(Directory.GetFiles(fixture.Destination));
	}

	[Fact]
	public void CreateVariables_LoadsConfigurationFromDestinationAndOptionsWin()
	{
		using var fixture = new DeploymentFixture();
		fixture.Write("source/appsettings.json", "{\"ApplicationName\":\"WrongApplication\"}");
		fixture.Write("target/appsettings.json", """
			{"ApplicationName":"TargetApplication","Database":{"Name":"database","Users":["first","second"],"Groups":["admin"]},"Items":[{"Name":"nested"}]}
			""");
		var options = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
		{
			["destination"] = fixture.Destination,
			["DATABASE.NAME"] = "option-database",
		};
		var variables = Deployer.CreateVariables(options, Path.Combine(fixture.Root, "source"));
		Assert.Equal("TargetApplication", variables["application"]);
		Assert.Equal("option-database", variables["Database.Name"]);
		Assert.Equal("first", variables["Database.Users[0]"]);
		Assert.Equal("second", variables["Database.Users[1]"]);
		Assert.Equal("admin", variables["Database.Groups[0]"]);
		Assert.Equal("nested", variables["Items[0].Name"]);
		Assert.Equal("option-database-admin-nested", Normalizer.Normalize("$(database.name)-%Database.Groups[0]%-$(Items[0].Name)", variables));
		using var exclusive = File.Open(Path.Combine(fixture.Destination, "appsettings.json"), FileMode.Open, FileAccess.ReadWrite, FileShare.None);
		Assert.True(exclusive.CanWrite);
	}

	[Fact]
	public void CreateVariables_DestinationForwardMultiHop_UsesTargetAppSettings()
	{
		using var fixture = new DeploymentFixture();
		fixture.Write("source/appsettings.json", "{\"ApplicationName\":\"WrongApplication\"}");
		fixture.Write("target/appsettings.json", "{\"ApplicationName\":\"TargetApplication\",\"Nested\":\"$(tool_label)\"}");
		var options = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
		{
			["destination"] = "$(tool_root)/$(tool_stage)",
			["tool_root"] = "$(tool_workspace)",
			["tool_stage"] = "target",
			["tool_workspace"] = fixture.Root,
			["tool_label"] = "%application%-$(tool_stage)",
		};

		var variables = Deployer.CreateVariables(options, Path.Combine(fixture.Root, "source"));

		Assert.Equal(fixture.Destination, variables["destination"]);
		Assert.Equal("TargetApplication", variables["application"]);
		Assert.Equal("TargetApplication-target", variables["Nested"]);
		Assert.Equal("TargetApplication-target", Normalizer.Normalize("$(Nested)", variables));
	}

	[Fact]
	public void CreateVariables_NestedValuesResolveLazilyAndTrackMutations()
	{
		using var fixture = new DeploymentFixture();
		var options = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
		{
			["destination"] = fixture.Destination,
			["tool_value"] = "$(tool_middle)/%tool_suffix%",
			["tool_middle"] = "$(tool_leaf)",
			["tool_leaf"] = "first",
			["tool_suffix"] = "ready",
			["tool_unused"] = "$(tool_missing)",
			["tool_cycle"] = "$(tool_cycle)",
		};

		var variables = Deployer.CreateVariables(options, fixture.Root);

		Assert.Equal("first/ready", variables["tool_value"]);
		variables["tool_leaf"] = "second";
		Assert.Equal("second/ready", variables["tool_value"]);
		Assert.Contains("tool_missing", Assert.Throws<FormatException>(() => variables["tool_unused"]).Message, StringComparison.OrdinalIgnoreCase);
		Assert.Contains("tool_cycle", Assert.Throws<FormatException>(() => variables["tool_cycle"]).Message, StringComparison.OrdinalIgnoreCase);
	}

	[Fact]
	public async Task Deploy_DetailLogPreservesDiagnosticTextInWriterPlanAndReportAsync()
	{
		using var fixture = new DeploymentFixture();
		fixture.Variables["verbosity"] = "detail";
		var report = Path.Combine(fixture.Root, "warning-report.json");
		fixture.Variables["report"] = report;
		var manifest = Path.Combine(fixture.Root, "token=synthetic-token", "password=synthetic-password", "missing.deploy");
		var deployer = fixture.CreateDeployer();

		var result = await deployer.DeployAsync(manifest, fixture.Destination, TestContext.Current.CancellationToken);

		Assert.Equal(0, result.Failures);
		Assert.Equal(0, result.Successes);
		Assert.Equal(1, result.Skipped);
		var diagnostic = Assert.Single(deployer.Plan.Diagnostics);
		Assert.Contains(manifest, diagnostic, StringComparison.Ordinal);
		Assert.Equal(diagnostic + Environment.NewLine, fixture.Log.ToString());
		var saved = DeploymentPlan.Load(report);
		Assert.True(saved.Succeeded);
		Assert.Equal([diagnostic], saved.Diagnostics);
		Assert.Empty(Directory.GetFiles(fixture.Destination));
	}

	[Fact]
	public async Task Deploy_DetailLogPreservesSyntheticSourcePathAsync()
	{
		using var fixture = new DeploymentFixture();
		fixture.Variables["verbosity"] = "detail";
		var source = fixture.Write("source/token=synthetic-token/file.txt", "synthetic log fixture");
		var manifest = fixture.Manifest("file.txt", "source/token=synthetic-token/.deploy");
		var deployer = fixture.CreateDeployer();

		var result = await deployer.DeployAsync(manifest, fixture.Destination, TestContext.Current.CancellationToken);

		Assert.Equal(0, result.Failures);
		Assert.Equal(1, result.Successes);
		Assert.Contains(source, fixture.Log.ToString(), StringComparison.Ordinal);
		Assert.Equal(source, Assert.Single(deployer.Plan.Operations).Source);
		Assert.Equal("synthetic log fixture", File.ReadAllText(Path.Combine(fixture.Destination, "file.txt")));
		Assert.Empty(deployer.Plan.Diagnostics);
	}

	[Theory]
	[InlineData(null, -1, "target", 0, 1)]
	[InlineData("newest", -1, "target", 0, 1)]
	[InlineData("newest", 0, "source", 1, 0)]
	[InlineData("newest", 1, "source", 1, 0)]
	[InlineData("never", 1, "target", 0, 1)]
	[InlineData("alway", -1, "source", 1, 0)]
	public async Task Deploy_OverwritePolicyPreservesOrReplacesContentAsync(string overwrite, int offset, string expected, int copies, int skipped)
	{
		using var fixture = new DeploymentFixture();
		var source = fixture.Write("source/file.txt", "source");
		var target = fixture.Write("target/file.txt", "target");
		var timestamp = new DateTime(2025, 1, 1, 12, 0, 0, DateTimeKind.Utc);
		File.SetLastWriteTimeUtc(source, timestamp.AddHours(offset));
		File.SetLastWriteTimeUtc(target, timestamp);
		if(overwrite != null)
			fixture.Variables["overwrite"] = overwrite;
		var result = await fixture.CreateDeployer().DeployAsync(fixture.Manifest("file.txt"), fixture.Destination, TestContext.Current.CancellationToken);
		Assert.Equal(expected, File.ReadAllText(target));
		Assert.Equal(copies, result.Successes);
		Assert.Equal(skipped, result.Skipped);
		Assert.Equal(0, result.Failures);
	}

	[Fact]
	public async Task Deploy_InvalidOverwriteFailsAsync()
	{
		using var fixture = new DeploymentFixture();
		fixture.Write("source/file.txt", "source");
		fixture.Write("target/file.txt", "target");
		fixture.Variables["overwrite"] = "typo";
		var result = await fixture.CreateDeployer().DeployAsync(fixture.Manifest("file.txt"), fixture.Destination, TestContext.Current.CancellationToken);
		Assert.True(result.Failures > 0);
		Assert.Equal("target", File.ReadAllText(Path.Combine(fixture.Destination, "file.txt")));
	}

	[Theory]
	[InlineData("<x>", false, false)]
	[InlineData("<x>", true, true)]
	[InlineData("<!x>", false, true)]
	[InlineData("<!x>", true, false)]
	[InlineData("<application & x>", false, false)]
	[InlineData("<application & x>", true, true)]
	[InlineData("<preview:a,b & x>", true, true)]
	public async Task Deploy_FilterOnlyDestinationAndSingleCharacterConditionAsync(string condition, bool hasX, bool copies)
	{
		using var fixture = new DeploymentFixture();
		fixture.Write("source/file.txt", "filtered source");
		fixture.Variables["application"] = "Business";
		fixture.Variables["preview"] = "B";
		if(hasX)
			fixture.Variables["x"] = "";
		var result = await fixture.CreateDeployer().DeployAsync(fixture.Manifest($"file.txt = {condition}"), fixture.Destination, TestContext.Current.CancellationToken);
		Assert.Equal(0, result.Failures);
		Assert.Equal(copies ? 1 : 0, result.Successes);
		Assert.Equal(copies, File.Exists(Path.Combine(fixture.Destination, "file.txt")));
		Assert.Equal(copies ? ["file.txt"] : [], Directory.GetFiles(fixture.Destination).Select(Path.GetFileName).ToArray());
	}

	[Theory]
	[InlineData("<>")]
	[InlineData("<application")]
	[InlineData("<application &>")]
	public async Task Deploy_InvalidFilterFailsAsync(string condition)
	{
		using var fixture = new DeploymentFixture();
		fixture.Write("source/file.txt", "source");
		var result = await fixture.CreateDeployer().DeployAsync(fixture.Manifest($"file.txt = {condition}"), fixture.Destination, TestContext.Current.CancellationToken);
		Assert.True(result.Failures > 0);
		Assert.Empty(Directory.GetFiles(fixture.Destination));
	}

	[Theory]
	[InlineData("unknown:input", "unknown")]
	public async Task Deploy_UnknownResolverCountsFailureAsync(string entry, string diagnostic)
	{
		using var fixture = new DeploymentFixture();
		var result = await fixture.CreateDeployer().DeployAsync(fixture.Manifest(entry), fixture.Destination, TestContext.Current.CancellationToken);
		Assert.Equal(1, result.Failures);
		Assert.Equal(0, result.Successes);
		Assert.Empty(Directory.GetFiles(fixture.Destination));
		Assert.Contains(diagnostic, fixture.Log.ToString(), StringComparison.OrdinalIgnoreCase);
	}

	[Theory]
	[InlineData(false)]
	[InlineData(true)]
	public async Task Deploy_MissingManifestsDoNotPreventRemainingManifestsAsync(bool dryRun)
	{
		using var fixture = new DeploymentFixture();
		fixture.Variables["dry-run"] = dryRun.ToString();
		fixture.Write("source/file.txt", "required content");
		var emptyDirectory = Path.Combine(fixture.Root, "empty");
		Directory.CreateDirectory(emptyDirectory);
		var missing = Path.Combine(fixture.Root, "absent.deploy");
		var deployer = fixture.CreateDeployer();

		var result = await deployer.DeployManyAsync([missing, fixture.Manifest("file.txt"), emptyDirectory], fixture.Destination, TestContext.Current.CancellationToken);

		Assert.Equal(0, result.Failures);
		Assert.Equal(dryRun ? 0 : 1, result.Successes);
		Assert.Equal(2, result.Skipped);
		Assert.True(deployer.Plan.Succeeded);
		Assert.Single(deployer.Plan.Manifests);
		Assert.Single(deployer.Plan.Operations);
		Assert.Equal(2, deployer.Plan.Diagnostics.Count);
		Assert.Contains(missing, fixture.Log.ToString(), StringComparison.Ordinal);
		Assert.Contains(Path.Combine(emptyDirectory, ".deploy"), fixture.Log.ToString(), StringComparison.Ordinal);
		Assert.Equal(!dryRun, File.Exists(Path.Combine(fixture.Destination, "file.txt")));
	}

	[Theory]
	[InlineData("missing.txt")]
	[InlineData("missing/directory/file.txt")]
	[InlineData("optional.deploy")]
	public async Task Deploy_MissingSourceWarnsAndContinuesAsync(string missing)
	{
		using var fixture = new DeploymentFixture();
		fixture.Write("source/before.txt", "before");
		fixture.Write("source/after.txt", "after");
		fixture.Write("target/retained.txt", "preserved");
		var report = Path.Combine(fixture.Root, "result.json");
		fixture.Variables["report"] = report;
		var deployer = fixture.CreateDeployer();

		var result = await deployer.DeployAsync(fixture.Manifest($"before.txt\n{missing} = retained.txt\nafter.txt"), fixture.Destination, TestContext.Current.CancellationToken);

		Assert.Equal(0, result.Failures);
		Assert.Equal(2, result.Successes);
		Assert.Equal(1, result.Skipped);
		Assert.Equal("before", File.ReadAllText(Path.Combine(fixture.Destination, "before.txt")));
		Assert.Equal("after", File.ReadAllText(Path.Combine(fixture.Destination, "after.txt")));
		Assert.Equal("preserved", File.ReadAllText(Path.Combine(fixture.Destination, "retained.txt")));
		var plan = DeploymentPlan.Load(report);
		Assert.True(plan.Succeeded);
		Assert.Equal(2, plan.Operations.Count);
		Assert.Contains(Path.GetFullPath(Path.Combine(fixture.Root, "source", missing)), Assert.Single(plan.Diagnostics), StringComparison.Ordinal);
		Assert.Contains(plan.Diagnostics[0], fixture.Log.ToString(), StringComparison.Ordinal);
	}

	[Fact]
	public async Task Deploy_PackageManifestMissingOptionalSourceContinuesAsync()
	{
		using var fixture = new DeploymentFixture();
		fixture.Package("Optional.Assets");
		fixture.Manifest("artifacts/optional.option\nlib/$(Framework)/Optional.Assets.dll", "packages/optional.assets/1.0.0/.deploy");
		var deployer = fixture.CreateDeployer();

		var result = await deployer.DeployAsync(fixture.Manifest("nuget:Optional.Assets@1.0.0"), fixture.Destination, TestContext.Current.CancellationToken);

		Assert.Equal(0, result.Failures);
		Assert.Equal(1, result.Successes);
		Assert.Equal(1, result.Skipped);
		Assert.True(deployer.Plan.Succeeded);
		Assert.Equal("Optional.Assets@1.0.0", File.ReadAllText(Path.Combine(fixture.Destination, "Optional.Assets.dll")));
		Assert.Contains("optional.option", Assert.Single(deployer.Plan.Diagnostics), StringComparison.Ordinal);
	}

	[Theory]
	[InlineData("delete:../outside.txt")]
	[InlineData("file.txt = ../outside.txt")]
	public async Task Deploy_TargetEscapeDoesNotWriteOrDeleteAsync(string entry)
	{
		using var fixture = new DeploymentFixture();
		fixture.Write("source/file.txt", "source");
		var outside = fixture.Write("outside.txt", "untouched");
		var result = await fixture.CreateDeployer().DeployAsync(fixture.Manifest(entry), fixture.Destination, TestContext.Current.CancellationToken);
		Assert.Equal("untouched", File.ReadAllText(outside));
		Assert.True(result.Failures > 0);
		Assert.Equal(0, result.Successes);
	}

	[Fact]
	public async Task Deploy_SectionEscapeDoesNotCreateDirectoryAsync()
	{
		using var fixture = new DeploymentFixture();
		fixture.Write("source/file.txt", "source");
		var result = await fixture.CreateDeployer().DeployAsync(fixture.Manifest("[../outside]\nfile.txt"), fixture.Destination, TestContext.Current.CancellationToken);
		Assert.True(result.Failures > 0);
		Assert.False(Directory.Exists(Path.Combine(fixture.Root, "outside")));
	}

	[Fact]
	public async Task Deploy_CopyThenDeleteCountsDeletionAsync()
	{
		using var fixture = new DeploymentFixture();
		fixture.Write("source/file.txt", "source");
		var result = await fixture.CreateDeployer().DeployAsync(fixture.Manifest("file.txt\ndelete:file.txt"), fixture.Destination, TestContext.Current.CancellationToken);
		Assert.Equal(1, result.Successes);
		Assert.Equal(1, result.Deleted);
		Assert.Equal(0, result.Failures);
		Assert.False(File.Exists(Path.Combine(fixture.Destination, "file.txt")));
	}

	[Fact]
	public async Task Deploy_RecursiveManifestFailsAsync()
	{
		using var fixture = new DeploymentFixture();
		var manifest = fixture.Manifest("second.deploy");
		fixture.Manifest("[deeper]\n.deploy", "source/second.deploy");
		using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(10));
		var result = await fixture.CreateDeployer().DeployAsync(manifest, fixture.Destination, cancellation.Token);
		Assert.True(result.Failures > 0);
		Assert.Equal(0, result.Successes);
		Assert.Contains(".deploy", fixture.Log.ToString());
		Assert.Contains("second.deploy", fixture.Log.ToString());
	}

	[Fact]
	public async Task Deploy_RepeatedManifestToDifferentTargetsSucceedsAsync()
	{
		using var fixture = new DeploymentFixture();
		fixture.Write("source/file.txt", "shared");
		fixture.Manifest("file.txt", "source/child.deploy");
		var result = await fixture.CreateDeployer().DeployAsync(fixture.Manifest("[first]\nchild.deploy\n[second]\nchild.deploy"), fixture.Destination, TestContext.Current.CancellationToken);
		Assert.Equal(0, result.Failures);
		Assert.Equal(2, result.Successes);
		Assert.Equal("shared", File.ReadAllText(Path.Combine(fixture.Destination, "first", "file.txt")));
		Assert.Equal("shared", File.ReadAllText(Path.Combine(fixture.Destination, "second", "file.txt")));
	}

	[Fact]
	public async Task Deploy_CancellationDoesNotCopyAsync()
	{
		using var fixture = new DeploymentFixture();
		fixture.Write("source/file.txt", "source");
		using var cancellation = new CancellationTokenSource();
		cancellation.Cancel();
		await Assert.ThrowsAnyAsync<OperationCanceledException>(() => fixture.CreateDeployer().DeployAsync(fixture.Manifest("file.txt"), fixture.Destination, cancellation.Token));
		Assert.Empty(Directory.GetFiles(fixture.Destination));
	}
}
