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
	[InlineData("en", "File or package not found: ")]
	[InlineData("zh-Hans", "文件或包不存在：")]
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

			Assert.Equal(1, result.Failures);
			Assert.Equal(0, result.Successes);
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
	public async Task Deploy_DetailLogPreservesDiagnosticTextInWriterPlanAndReportAsync()
	{
		using var fixture = new DeploymentFixture();
		fixture.Variables["verbosity"] = "detail";
		var report = Path.Combine(fixture.Root, "failed-report.json");
		fixture.Variables["report"] = report;
		var manifest = Path.Combine(fixture.Root, "token=synthetic-token", "password=synthetic-password", "missing.deploy");
		var deployer = fixture.CreateDeployer();

		var result = await deployer.DeployAsync(manifest, fixture.Destination, TestContext.Current.CancellationToken);

		Assert.Equal(1, result.Failures);
		Assert.Equal(0, result.Successes);
		var diagnostic = Assert.Single(deployer.Plan.Diagnostics);
		Assert.Contains(manifest, diagnostic, StringComparison.Ordinal);
		Assert.Equal(diagnostic + Environment.NewLine, fixture.Log.ToString());
		var saved = DeploymentPlan.Load(report);
		Assert.False(saved.Succeeded);
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
	[InlineData("missing.txt", "missing.txt")]
	public async Task Deploy_UnknownResolverOrMissingSourceCountsFailureAsync(string entry, string diagnostic)
	{
		using var fixture = new DeploymentFixture();
		var result = await fixture.CreateDeployer().DeployAsync(fixture.Manifest(entry), fixture.Destination, TestContext.Current.CancellationToken);
		Assert.Equal(1, result.Failures);
		Assert.Equal(0, result.Successes);
		Assert.Empty(Directory.GetFiles(fixture.Destination));
		Assert.Contains(diagnostic, fixture.Log.ToString(), StringComparison.OrdinalIgnoreCase);
	}

	[Fact]
	public async Task Deploy_MissingManifestCountsFailureAsync()
	{
		using var fixture = new DeploymentFixture();
		var result = await fixture.CreateDeployer().DeployAsync(Path.Combine(fixture.Root, "absent.deploy"), fixture.Destination, TestContext.Current.CancellationToken);
		Assert.Equal(1, result.Failures);
		Assert.Equal(0, result.Successes);
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
