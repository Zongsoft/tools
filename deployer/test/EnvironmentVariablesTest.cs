using Xunit;

using Zongsoft.Common;
using Zongsoft.Configuration.Profiles;
using Zongsoft.Text.Templating;

namespace Zongsoft.Tools.Deployer.Tests;

public sealed class EnvironmentVariablesTest
{
	[Theory]
	[InlineData(false, null, false, "net8.0")]
	[InlineData(true, null, false, "")]
	[InlineData(true, "", false, "")]
	[InlineData(false, null, true, "net9.0")]
	[InlineData(true, null, true, "")]
	[InlineData(true, "", true, "")]
	[InlineData(true, "net10.0", true, "net10.0")]
	[InlineData(true, "   ", true, "   ")]
	public void CreateEvaluator_FrameworkUsesFirstFoundValueIncludingExplicitEmpty(bool specified, string option, bool environmentFile, string expected)
	{
		using var fixture = new DeploymentFixture();
		var previous = Environment.GetEnvironmentVariable("framework");

		try
		{
			Environment.SetEnvironmentVariable("framework", "net8.0");
			if(environmentFile)
			{
				fixture.Write(".env", "framework=net8.0\n");
				fixture.Write("source/.env", "FRAMEWORK=net9.0\n");
			}

			var options = new Dictionary<string, string> { ["destination"] = "${framework}" };
			if(specified)
				options["FrAmEwOrK"] = option;

			var source = Path.Combine(fixture.Root, "source");
			var variables = Deployer.CreateEvaluator(options, source);

			Assert.Equal(expected, variables.Evaluate("${framework}"));
			Assert.Equal(Path.GetFullPath(string.IsNullOrWhiteSpace(expected) ? source : Path.Combine(source, expected)), Path.GetFullPath(variables.Evaluate("${destination}")));
			Assert.Equal("net8.0", Environment.GetEnvironmentVariable("framework"));
		}
		finally
		{
			Environment.SetEnvironmentVariable("framework", previous);
		}
	}

	[Theory]
	[InlineData(null)]
	[InlineData("")]
	public void CreateEvaluator_ExplicitEmptyFrameworkBlocksApplicationConfiguration(string option)
	{
		using var fixture = new DeploymentFixture();
		fixture.Write(".env", "framework=net8.0\n");
		fixture.Write("target/appsettings.json", "{\"Framework\":\"net9.0\"}");
		var options = new Dictionary<string, string>
		{
			["destination"] = fixture.Destination.Replace('\\', '/'),
			["framework"] = option,
		};

		var variables = Deployer.CreateEvaluator(options, fixture.Root);

		Assert.True(variables.Providers.TryGetValue("framework", out var value));
		Assert.Equal(option, value);
		Assert.Equal(string.Empty, variables.Evaluate("${framework}"));
	}

	[Theory]
	[InlineData(null, "framework")]
	[InlineData("", "framework=")]
	public void CreateEvaluator_ExplicitFrameworkRetainsNullAndEmptyValues(string option, string entry)
	{
		using var fixture = new DeploymentFixture();
		fixture.Write(".env", "framework=net8.0\n");
		fixture.Write("source/.env", entry + "\n");
		var variables = Deployer.CreateEvaluator(new Dictionary<string, string> { ["framework"] = option }, Path.Combine(fixture.Root, "source"));

		Assert.True(variables.Providers.TryGetValue("framework", out var value));
		Assert.Equal(option, value);
		Assert.Equal(string.Empty, variables.Evaluate("${framework}"));
	}

	[Fact]
	public void CreateEvaluator_EnvironmentHierarchyPreservesNamespacesAndIsolatesLoads()
	{
		using var fixture = new DeploymentFixture();
		fixture.Write(".env", "zongsoft_env_root=ancestor\nzongsoft_env_shared=far\nzongsoft_env_parent=retained\nzongsoft_env_reference=${zongsoft_env_shared}/${zongsoft_env_root}\n[io rustfs]\naccess_key=ancestor-access\nsecret_key=RustFS@2025\n");
		fixture.Write("source/missing/leaf/.env", "ZONGSOFT_ENV_SHARED=near\nzongsoft_env_empty=\nzongsoft_env_unused=${zongsoft_env_missing}\n[io rustfs]\naccess_key=local-access\n");
		fixture.Write("source/missing/leaf/child/.env", "zongsoft_env_shared=child\nzongsoft_env_child=must-not-load\n");
		var previous = Environment.GetEnvironmentVariable("zongsoft_env_shared");
		var first = Deployer.CreateEvaluator(new Dictionary<string, string>(), Path.Combine(fixture.Root, "source", "missing", "leaf"));

		Assert.Equal("ancestor", first.Evaluate("${zongsoft_env_root}"));
		Assert.Equal("retained", first.Evaluate("${zongsoft_env_parent}"));
		Assert.Equal("near", first.Evaluate("${zongsoft_env_shared}"));
		Assert.Equal("near/ancestor", first.Evaluate("${zongsoft_env_reference}"));
		Assert.Equal("local-access", first.Evaluate("${IO.RUSTFS:ACCESS_KEY}"));
		Assert.Equal("RustFS@2025", first.Evaluate("${io.rustfs:secret_key}"));
		Assert.Equal(string.Empty, first.Evaluate("${zongsoft_env_empty}"));
		Assert.DoesNotContain(first.Providers, provider => provider.TryGetValue("zongsoft_env_child", out _));
		Assert.Throws<TemplateEvaluationException>(() => first.Evaluate("${zongsoft_env_unused}"));
		Assert.Equal(previous, Environment.GetEnvironmentVariable("zongsoft_env_shared"));

		fixture.Write("source/missing/leaf/.env", "zongsoft_env_shared=updated\n");
		var second = Deployer.CreateEvaluator(new Dictionary<string, string>(), Path.Combine(fixture.Root, "source", "missing", "leaf"));
		Assert.Equal("updated/ancestor", second.Evaluate("${zongsoft_env_reference}"));
		Assert.Equal("ancestor-access", second.Evaluate("${io.rustfs:access_key}"));
		Assert.DoesNotContain(second.Providers, provider => provider.TryGetValue("zongsoft_env_empty", out _));
		Assert.Equal("near/ancestor", first.Evaluate("${zongsoft_env_reference}"));
	}

	[Fact]
	public void CreateEvaluator_EnvironmentFilesRespectConfigurationAndExplicitOptionPriority()
	{
		using var fixture = new DeploymentFixture();
		const string KEY = "zongsoft_env_priority";
		var previous = Environment.GetEnvironmentVariable(KEY);

		try
		{
			Environment.SetEnvironmentVariable(KEY, "process");
			fixture.Write(".env", KEY + "=ancestor\nzongsoft_env_workspace=" + fixture.Root.Replace('\\', '/') + "\n");
			fixture.Write("source/.env", KEY + "=source\nzongsoft_env_target=${zongsoft_env_workspace}/${zongsoft_env_stage}\n");
			fixture.Write("target/appsettings.json", "{\"zongsoft_env_priority\":\"configuration\",\"zongsoft_env_configuration\":\"target-only\"}");
			var options = new Dictionary<string, string>
			{
				["destination"] = "${zongsoft_env_target}",
				["zongsoft_env_stage"] = "target",
			};
			var source = Path.Combine(fixture.Root, "source");
			var environment = Deployer.CreateEvaluator(new Dictionary<string, string>(), source);
			Assert.Equal("source", environment.Evaluate("${" + KEY + "}"));
			var configured = Deployer.CreateEvaluator(options, source);
			Assert.Equal(fixture.Destination, Path.GetFullPath(configured.Evaluate("${destination}")));
			Assert.Equal("configuration", configured.Evaluate("${" + KEY + "}"));
			Assert.Equal("target-only", configured.Evaluate("${zongsoft_env_configuration}"));

			options[KEY] = string.Empty;
			var explicitEmpty = Deployer.CreateEvaluator(options, source);
			Assert.Equal(string.Empty, explicitEmpty.Evaluate("${" + KEY + "}"));
			Assert.Equal("process", Environment.GetEnvironmentVariable(KEY));
		}
		finally
		{
			Environment.SetEnvironmentVariable(KEY, previous);
		}
	}

	[Fact]
	public void CreateEvaluator_ApplicationNullBlocksEnvironmentFallbackAndAllowsExplicitOverride()
	{
		using var fixture = new DeploymentFixture();
		fixture.Write("source/.env", "zongsoft_json_value=environment\nzongsoft_json_label=environment-label\n");
		fixture.Write("target/appsettings.json", "{\"Zongsoft_Json\":{\"Value\":null,\"Label\":\"configuration\"}}");
		var options = new Dictionary<string, string> { ["destination"] = fixture.Destination.Replace('\\', '/') };
		var source = Path.Combine(fixture.Root, "source");

		var configured = Deployer.CreateEvaluator(options, source);

		Assert.Contains(configured.Providers, provider => provider.TryGetValue("zongsoft_json_value", out var value) && Equals(value, "environment"));
		Assert.True(configured.Providers.TryGetValue("zongsoft_json_value", configured.Options.Fallback, out var configuredValue));
		Assert.Null(configuredValue);
		Assert.Equal(string.Empty, configured.Evaluate("${zongsoft_json_value}"));
		Assert.Equal("configuration", configured.Evaluate("${zongsoft_json_label}"));

		options["zongsoft_json_value"] = "command";
		var overridden = Deployer.CreateEvaluator(options, source);

		Assert.True(overridden.Providers.TryGetValue("zongsoft_json_value", overridden.Options.Fallback, out var explicitValue));
		Assert.Equal("command", explicitValue);
		Assert.Equal("command/configuration", overridden.Evaluate("${zongsoft_json_value}/${zongsoft_json_label}"));
		Assert.True(configured.Providers.TryGetValue("zongsoft_json_value", configured.Options.Fallback, out configuredValue));
		Assert.Null(configuredValue);
	}

	[Fact]
	public void CreateEvaluator_EnvironmentImportPreservesCoreOrderingAndNullValues()
	{
		using var fixture = new DeploymentFixture();
		fixture.Write(".env", "zongsoft_env_flag=ancestor-value\n");
		fixture.Write("source/.env", "#@import settings/defaults.ini\nzongsoft_env_value=local\nzongsoft_env_flag\n");
		fixture.Write("source/settings/defaults.ini", "#@import absent.ini\nzongsoft_env_value=imported\n[io rustfs]\nsecret_key=import-secret\n");

		var variables = Deployer.CreateEvaluator(new Dictionary<string, string>(), Path.Combine(fixture.Root, "source"));

		Assert.Equal("local", variables.Evaluate("${zongsoft_env_value}"));
		Assert.Equal("import-secret", variables.Evaluate("${io.rustfs:secret_key}"));
		Assert.Contains(variables.Providers, provider => provider.TryGetValue("zongsoft_env_flag", out _));
		Assert.Contains(variables.Providers, provider => provider.TryGetValue("zongsoft_env_flag", out var value) && value == null);
		Assert.Contains(variables.Providers, provider => provider.TryGetValue("zongsoft_env_flag", out var value) && Equals(value, "ancestor-value"));
		Assert.Equal(string.Empty, variables.Evaluate("${zongsoft_env_flag}"));
		using var exclusive = File.Open(Path.Combine(fixture.Root, "source", ".env"), FileMode.Open, FileAccess.ReadWrite, FileShare.None);
		Assert.True(exclusive.CanWrite);
	}

	[Theory]
	[InlineData(false)]
	[InlineData(true)]
	public void CreateEvaluator_InvalidEnvironmentFileFailsBeforeDeployment(bool unreadable)
	{
		using var fixture = new DeploymentFixture();
		var path = Path.Combine(fixture.Root, ".env");

		if(unreadable)
			Directory.CreateDirectory(path);
		else
			fixture.Write(".env", "#@import .env\n");

		if(unreadable)
			Assert.Throws<UnauthorizedAccessException>(() => Deployer.CreateEvaluator(new Dictionary<string, string>(), fixture.Root));
		else
			Assert.Throws<ProfileException>(() => Deployer.CreateEvaluator(new Dictionary<string, string>(), fixture.Root));
		Assert.Empty(Directory.GetFiles(fixture.Destination));
	}

	[Fact]
	public void CreateEvaluator_EnvironmentUpdatesAndRemovalRemainVisible()
	{
		using var fixture = new DeploymentFixture();
		var name = "ZongsoftToolsLive_" + Guid.NewGuid().ToString("N");

		try
		{
			Environment.SetEnvironmentVariable(name, "first");
			var evaluator = Deployer.CreateEvaluator(new Dictionary<string, string>(), fixture.Root);

			Assert.Equal("first", evaluator.Evaluate("${" + name + "}"));

			Environment.SetEnvironmentVariable(name, "updated");

			Assert.Equal("updated", evaluator.Evaluate("${" + name + "}"));

			Environment.SetEnvironmentVariable(name, null);
			var error = Assert.Throws<TemplateEvaluationException>(() => evaluator.Evaluate("${" + name + "}"));

			Assert.Equal("MissingVariable", error.Code);
		}
		finally
		{
			Environment.SetEnvironmentVariable(name, null);
		}
	}

	[Fact]
	public void CreateEvaluator_ExplicitProductSelectsEnvironmentImport()
	{
		using var fixture = new DeploymentFixture();
		fixture.Write(".env", "product=ancestor\n");
		fixture.Write("source/.env", "product=local\n#@import ../.shared/${product}.env\n");
		fixture.Write(".shared/erp.env", "selected=erp-settings\n");
		fixture.Write(".shared/local.env", "selected=wrong-settings\n");

		var variables = Deployer.CreateEvaluator(new Dictionary<string, string> { ["product"] = "erp" }, Path.Combine(fixture.Root, "source"));

		Assert.Equal("erp", variables.Evaluate("${product}"));
		Assert.Equal("erp-settings", variables.Evaluate("${selected}"));
	}

	[Fact]
	public void CreateEvaluator_EarlierEntriesAndCompletedImportsSelectLaterPaths()
	{
		using var fixture = new DeploymentFixture();
		fixture.Write("source/.env", "selected=first\n#@import settings/${selected}.env\n#@import settings/${next}.env\n");
		fixture.Write("source/settings/first.env", "next=second\nfirst_value=retained\n");
		fixture.Write("source/settings/second.env", "second_value=selected\n");

		var variables = Deployer.CreateEvaluator(new Dictionary<string, string>(), Path.Combine(fixture.Root, "source"));

		Assert.Equal("retained", variables.Evaluate("${first_value}"));
		Assert.Equal("selected", variables.Evaluate("${second_value}"));
		Assert.Equal("second", variables.Evaluate("${next}"));
	}

	[Fact]
	public void CreateEvaluator_NamespacesAndNormalizedEntryNamesRemainDistinct()
	{
		using var fixture = new DeploymentFixture();
		fixture.Write("source/.env", "customer-name=customer\ndb_name=root\n[database default]\ndb-name=business\naccess.key=secret\n");
		var options = new Dictionary<string, string> { ["release.label"] = "stable", ["deploy-region"] = "east" };

		var variables = Deployer.CreateEvaluator(options, Path.Combine(fixture.Root, "source"));

		Assert.Equal("customer/business/secret/root/stable/east", variables.Evaluate("${customer_name}/${DATABASE.DEFAULT:DB_NAME}/${database.default:access_key}/${db_name}/${release_label}/${deploy_region}"));
		Assert.True(variables.Options.Fallback);
		Assert.Equal("root", variables.Evaluate("${database:db_name}"));

		variables.Options.Fallback = false;

		var error = Assert.Throws<TemplateEvaluationException>(() => variables.Evaluate("${database:db_name}"));
		Assert.Equal("MissingVariable", error.Code);
		Assert.Equal("database:db_name", error.Expression);
		Assert.Equal("business/root", variables.Evaluate("${database.default:db_name}/${db_name}"));
	}

	[Theory]
	[InlineData("db-name=first\ndb.name=second\n", "db_name")]
	[InlineData("[database default]\nname=first\n[database.default]\nname=second\n", "database.default:name")]
	public void CreateEvaluator_AmbiguousNamesFailOnlyWhenQueried(string declarations, string name)
	{
		using var fixture = new DeploymentFixture();
		fixture.Write("source/.env", "safe=retained\n" + declarations);

		var variables = Deployer.CreateEvaluator(new Dictionary<string, string>(), Path.Combine(fixture.Root, "source"));

		Assert.Equal("retained", variables.Evaluate("${safe}"));
		var error = Assert.Throws<TemplateEvaluationException>(() => variables.Evaluate("${" + name + "}"));
		Assert.Equal("ProviderFailed", error.Code);
		Assert.IsType<ProfileException>(error.InnerException);
		Assert.Equal("retained", variables.Evaluate("${safe}"));
	}

	[Fact]
	public void CreateEvaluator_ImportCannotUseAnEntryDeclaredLater()
	{
		using var fixture = new DeploymentFixture();
		fixture.Write("source/.env", "#@import ${zongsoft_later_import}\nzongsoft_later_import=selected.env\n");
		fixture.Write("source/selected.env", "unexpected=loaded\n");

		var error = Assert.Throws<TemplateEvaluationException>(() => Deployer.CreateEvaluator(new Dictionary<string, string>(), Path.Combine(fixture.Root, "source")));

		Assert.Equal("MissingVariable", error.Code);
		Assert.Empty(Directory.GetFiles(fixture.Destination));
	}

	[Fact]
	public void CreateEvaluator_ExpandedImportIsOneCompletePath()
	{
		using var fixture = new DeploymentFixture();
		fixture.Write("source/.env", "#@import ${selected}\n");
		fixture.Write("source/first.env second.env", "selected_value=combined-file\n");
		fixture.Write("source/first.env", "split_first=must-not-load\n");
		fixture.Write("source/second.env", "split_second=must-not-load\n");
		var options = new Dictionary<string, string> { ["selected"] = "first.env second.env" };

		var variables = Deployer.CreateEvaluator(options, Path.Combine(fixture.Root, "source"));

		Assert.Equal("combined-file", variables.Evaluate("${selected_value}"));
		Assert.DoesNotContain(variables.Providers, provider => provider.TryGetValue("split_first", out _));
		Assert.DoesNotContain(variables.Providers, provider => provider.TryGetValue("split_second", out _));
	}
}
