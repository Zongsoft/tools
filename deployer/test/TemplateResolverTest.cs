using System.Text;
using System.Text.Json;
using System.Xml.Linq;

using Xunit;

namespace Zongsoft.Tools.Deployer.Tests;

public class TemplateResolverTest
{
	[Theory]
	[InlineData("template", "plugins zongsoft externals aliyun", "plugins/zongsoft/externals/aliyun")]
	[InlineData("path", "plugins zongsoft externals aliyun", "plugins/zongsoft/externals/aliyun")]
	[InlineData("template", "plugins test network", "plugins/test/network")]
	public async Task SectionPathsAreNotEvaluatedAsTemplates(string resolver, string section, string directory)
	{
		using var fixture = new DeploymentFixture();
		fixture.Write("source/config.option.template", "<config />");
		var manifest = fixture.Manifest($"[{section}]\n{resolver}:config.option.template = config.option");
		var deployer = fixture.CreateDeployer();
		var result = await deployer.DeployAsync(manifest.Replace('\\', '/'), fixture.OutputDirectory.Replace('\\', '/'), TestContext.Current.CancellationToken);

		Assert.True(result.Failures == 0, fixture.Log.ToString());
		Assert.Equal("<config />", File.ReadAllText(Path.Combine(fixture.OutputDirectory, directory, "config.option")));
	}

	[Fact]
	public async Task SourceAnchorsInputsOutputsAndImportsWithoutChangingTheWorkingDirectory()
	{
		using var fixture = new DeploymentFixture();
		fixture.Write(".env", "scheme=wrong\n[redis]\nport=6379\n");
		fixture.Write("project/.env", "#@import ../.deploy/${scheme}/.env\nname=project\nsource=../wrong\nNuGet_Packages=cache\nNuGet_Server=feed\n");
		fixture.Write(".deploy/selected/.env", "name=scheme\n[redis]\nserver=selected.example\n");
		fixture.Write(".deploy/.shared/service.xml.template", "<service name=\"${name}\" endpoint=\"${redis:server}:${redis:port}\" />");
		fixture.Manifest("#@import ../.deploy/selected/app.deploy\n", "project/.deploy");
		fixture.Manifest("template:../.shared/service.xml.template = result.xml\n", ".deploy/selected/app.deploy");
		var current = Environment.CurrentDirectory;
		var evaluator = Deployer.CreateEvaluator(new Dictionary<string, string>
		{
			["source"] = "project",
			["output"] = "../output",
			["scheme"] = "selected",
			["report"] = "reports/result.json",
			["lockFile"] = "reports/lock.json",
		}, fixture.Root);
		var deployer = new Deployer(evaluator, fixture.Log);
		var result = await deployer.DeployAsync(".deploy", TestContext.Current.CancellationToken);

		Assert.Equal(0, result.Failures);
		Assert.Equal(current, Environment.CurrentDirectory);
		Assert.Equal(Path.Combine(fixture.Root, "project").Replace('\\', '/'), evaluator.Evaluate("${source}"));
		Assert.Equal(Path.Combine(fixture.Root, "project", "cache"), NugetUtility.GetPackagesDirectory(evaluator));
		Assert.Equal(Path.Combine(fixture.Root, "project", "feed"), NugetUtility.GetNugetServer(evaluator));
		var element = XElement.Load(Path.Combine(fixture.Root, "output", "result.xml"));
		Assert.Equal("project", element.Attribute("name")?.Value);
		Assert.Equal("selected.example:6379", element.Attribute("endpoint")?.Value);
		Assert.True(File.Exists(Path.Combine(fixture.Root, "project", "reports", "result.json")));
		Assert.True(File.Exists(Path.Combine(fixture.Root, "project", "reports", "lock.json")));
		Assert.False(File.Exists(Path.Combine(fixture.Root, "output", ".env")));
	}

	[Fact]
	public void SourceCannotBeLocatedByTheEnvironmentFileItWouldLoad()
	{
		using var fixture = new DeploymentFixture();
		fixture.Write(".env", "only_in_profile=project\n");
		Assert.Throws<Zongsoft.Text.Templating.TemplateEvaluationException>(() =>
			Deployer.CreateEvaluator(new Dictionary<string, string> { ["source"] = "${only_in_profile}" }, fixture.Root));
	}

	[Fact]
	public async Task TemplatesUseCoreRecursionAndFormatValuesWithoutReparsingTheirEncodedText()
	{
		using var fixture = new DeploymentFixture();
		fixture.Variables["value"] = "a&<\"'\n中文";
		fixture.Variables["nested"] = "${value}";
		fixture.Variables["literal"] = @"\${runtime}";
		fixture.Write("source/parts/config.xml.template", "<x value=\"${nested#xml}\" />\r\n");
		fixture.Write("source/parts/config.json.template", "{\"value\":${nested#json},\"literal\":${literal#json}}\r\n");
		var manifest = fixture.Manifest("template:parts/*.template = generated/\n");
		var deployer = fixture.CreateDeployer();
		var result = await deployer.DeployAsync(manifest.Replace('\\', '/'), fixture.OutputDirectory.Replace('\\', '/'), TestContext.Current.CancellationToken);

		Assert.Equal(0, result.Failures);
		Assert.Equal("a&<\"'\n中文", XElement.Load(Path.Combine(fixture.OutputDirectory, "generated", "config.xml")).Attribute("value")?.Value);
		var bytes = File.ReadAllBytes(Path.Combine(fixture.OutputDirectory, "generated", "config.json"));
		Assert.False(bytes.AsSpan().StartsWith(Encoding.UTF8.GetPreamble()));
		using var json = JsonDocument.Parse(bytes);
		Assert.Equal("a&<\"'\n中文", json.RootElement.GetProperty("value").GetString());
		Assert.Equal("${runtime}", json.RootElement.GetProperty("literal").GetString());
		Assert.All(deployer.Plan.Operations, operation => Assert.Equal("Render", operation.Kind));
	}

	[Fact]
	public async Task NewestComparesRenderedContentAndNeverPreservesExistingFiles()
	{
		using var fixture = new DeploymentFixture();
		fixture.Variables["value"] = "first";
		fixture.Write("source/config.template", "${value}");
		var manifest = fixture.Manifest("template:config.template\n");
		var deployer = fixture.CreateDeployer();
		Assert.Equal(0, (await deployer.DeployAsync(manifest.Replace('\\', '/'), fixture.OutputDirectory.Replace('\\', '/'), TestContext.Current.CancellationToken)).Failures);
		var target = Path.Combine(fixture.OutputDirectory, "config");
		File.SetLastWriteTimeUtc(target, DateTime.UtcNow.AddDays(1));
		var timestamp = File.GetLastWriteTimeUtc(target);

		Assert.Equal(0, (await deployer.DeployAsync(manifest.Replace('\\', '/'), fixture.OutputDirectory.Replace('\\', '/'), TestContext.Current.CancellationToken)).Failures);
		Assert.Equal("Unchanged", Assert.Single(deployer.Plan.Operations).Status);
		Assert.Equal(timestamp, File.GetLastWriteTimeUtc(target));
		fixture.Variables["value"] = "second";
		Assert.Equal(0, (await deployer.DeployAsync(manifest.Replace('\\', '/'), fixture.OutputDirectory.Replace('\\', '/'), TestContext.Current.CancellationToken)).Failures);
		Assert.Equal("second", File.ReadAllText(target));
		fixture.Variables["overwrite"] = "never";
		fixture.Variables["value"] = "third";
		Assert.Equal(0, (await deployer.DeployAsync(manifest.Replace('\\', '/'), fixture.OutputDirectory.Replace('\\', '/'), TestContext.Current.CancellationToken)).Failures);
		Assert.Equal("second", File.ReadAllText(target));
		Assert.Equal("Skipped", Assert.Single(deployer.Plan.Operations).Status);
	}

	[Theory]
	[InlineData("${missing}")]
	[InlineData("${broken")]
	public async Task InvalidTemplateFailsBeforeCopyOrDelete(string content)
	{
		using var fixture = new DeploymentFixture();
		fixture.Write("source/valid.txt", "new");
		fixture.Write("source/invalid.template", content);
		fixture.Write("target/retained.txt", "original");
		var manifest = fixture.Manifest("valid.txt\ndelete:retained.txt\ntemplate:invalid.template\n");
		var deployer = fixture.CreateDeployer();

		Assert.True((await deployer.DeployAsync(manifest.Replace('\\', '/'), fixture.OutputDirectory.Replace('\\', '/'), TestContext.Current.CancellationToken)).Failures > 0);
		Assert.Equal("original", File.ReadAllText(Path.Combine(fixture.OutputDirectory, "retained.txt")));
		Assert.False(File.Exists(Path.Combine(fixture.OutputDirectory, "valid.txt")));
		Assert.Contains("invalid.template", fixture.Log.ToString());
	}

	[Fact]
	public async Task SourceChangedDuringEvaluationCannotPublishTheOldSnapshot()
	{
		using var fixture = new DeploymentFixture();
		fixture.Variables["value"] = "value";
		var source = fixture.Write("source/config.template", "${value}");
		fixture.Evaluator.Resolving += (_, context) =>
		{
			if(context.Name == "value")
				File.WriteAllText(source, "modified");
		};
		var deployer = fixture.CreateDeployer();
		var result = await deployer.DeployAsync(fixture.Manifest("template:config.template").Replace('\\', '/'), fixture.OutputDirectory.Replace('\\', '/'), TestContext.Current.CancellationToken);

		Assert.True(result.Failures > 0);
		Assert.False(File.Exists(Path.Combine(fixture.OutputDirectory, "config")));
	}

	[Fact]
	public async Task RenderedContentParticipatesInLocksAndReportsWithoutSerializingThePayload()
	{
		using var fixture = new DeploymentFixture();
		fixture.Variables["value"] = "private-payload";
		fixture.Variables["lockFile"] = Path.Combine(fixture.Root, "lock.json").Replace('\\', '/');
		fixture.Write("source/config.template", "${value}");
		var manifest = fixture.Manifest("template:config.template");
		var deployer = fixture.CreateDeployer();
		Assert.Equal(0, (await deployer.DeployAsync(manifest.Replace('\\', '/'), fixture.OutputDirectory.Replace('\\', '/'), TestContext.Current.CancellationToken)).Failures);
		Assert.DoesNotContain("private-payload", File.ReadAllText(Path.Combine(fixture.Root, "lock.json")));
		fixture.Variables["locked"] = "true";
		fixture.Variables["value"] = "changed";
		Assert.True((await deployer.DeployAsync(manifest.Replace('\\', '/'), fixture.OutputDirectory.Replace('\\', '/'), TestContext.Current.CancellationToken)).Failures > 0);
		Assert.Equal("private-payload", File.ReadAllText(Path.Combine(fixture.OutputDirectory, "config")));
	}

	[Fact]
	public async Task UnchangedGeneratedFilesRemainOwnedForPruning()
	{
		using var fixture = new DeploymentFixture();
		fixture.Write("source/config.template", "generated");
		var manifest = fixture.Manifest("template:config.template");
		var report = Path.Combine(fixture.Root, "report.json");
		fixture.Variables["report"] = report.Replace('\\', '/');
		var deployer = fixture.CreateDeployer();
		Assert.Equal(0, (await deployer.DeployAsync(manifest.Replace('\\', '/'), fixture.OutputDirectory.Replace('\\', '/'), TestContext.Current.CancellationToken)).Failures);
		Assert.Equal(0, (await deployer.DeployAsync(manifest.Replace('\\', '/'), fixture.OutputDirectory.Replace('\\', '/'), TestContext.Current.CancellationToken)).Failures);
		fixture.Variables.Remove("report");
		fixture.Variables["previous"] = report.Replace('\\', '/');
		fixture.Variables["prune"] = "true";
		File.WriteAllText(manifest, "");
		var result = await deployer.DeployAsync(manifest.Replace('\\', '/'), fixture.OutputDirectory.Replace('\\', '/'), TestContext.Current.CancellationToken);
		Assert.Equal(0, result.Failures);
		Assert.False(File.Exists(Path.Combine(fixture.OutputDirectory, "config")));
	}

	[Fact]
	public async Task EnvironmentFilesAreNotCopiedAndDryRunDoesNotCreateGeneratedFiles()
	{
		using var fixture = new DeploymentFixture();
		fixture.Write("source/assets/.env", "private=value");
		fixture.Write("source/assets/file.txt", "data");
		fixture.Write("source/config.template", "rendered");
		var deployer = fixture.CreateDeployer();
		var manifest = fixture.Manifest("assets/*\ntemplate:config.template");
		fixture.Variables["dry_run"] = "true";
		Assert.Equal(0, (await deployer.DeployAsync(manifest.Replace('\\', '/'), fixture.OutputDirectory.Replace('\\', '/'), TestContext.Current.CancellationToken)).Failures);
		Assert.Empty(Directory.GetFiles(fixture.OutputDirectory));
		fixture.Variables.Remove("dry_run");
		Assert.Equal(0, (await deployer.DeployAsync(manifest.Replace('\\', '/'), fixture.OutputDirectory.Replace('\\', '/'), TestContext.Current.CancellationToken)).Failures);
		Assert.Equal("data", File.ReadAllText(Path.Combine(fixture.OutputDirectory, "file.txt")));
		Assert.Equal("rendered", File.ReadAllText(Path.Combine(fixture.OutputDirectory, "config")));
		Assert.False(File.Exists(Path.Combine(fixture.OutputDirectory, ".env")));
	}
}
