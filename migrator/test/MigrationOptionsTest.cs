using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

using Xunit;

using Zongsoft.Text.Templating;

namespace Zongsoft.Tools.Migrator.Tests;

public sealed class MigrationOptionsTest
{
	#region 变量展开
	[Fact]
	public void Options_UnusedInvalidVariables_DoesNotBlockUsedValues()
	{
		var options = new MigrationOptions(Utility.CreateEvaluator(new global::Zongsoft.Common.Variables
		{
			["name"] = "zongsoft.daemon",
			["payload"] = "${name}/bin",
			["unused"] = "${missing}",
			["loop"] = "${loop}",
		}));

		Assert.Equal("zongsoft.daemon/bin", options["payload"]);
		Assert.Equal("zongsoft.daemon", options.Name);
		Assert.Throws<TemplateEvaluationException>(() => options["unused"]);
		Assert.Throws<TemplateEvaluationException>(() => options["loop"]);
	}

	[Fact]
	public void Evaluate_NestedAndRepeatedReferences_ExpandsTemplates()
	{
		var variables = new global::Zongsoft.Common.Variables()
		{
			["name"] = "zongsoft.daemon",
			["scheme"] = "default",
			["root"] = "${scheme}/${NAME}",
		};

		var result = Utility.CreateEvaluator(variables).Evaluate("${root}/${name}-${name}");
		Assert.Equal("default/zongsoft.daemon/zongsoft.daemon-zongsoft.daemon", result);
		Assert.Equal("${scheme}/${NAME}", variables["root"]);
	}

	[Fact]
	public void Evaluate_CoreVariablesIgnoreVariableNameCase()
	{
		var variables = new global::Zongsoft.Common.Variables
		{
			["Root"] = "${service_name}",
			["Service_Name"] = "worker",
		};

		var result = Utility.CreateEvaluator(variables).Evaluate("${ROOT}/${SERVICE_NAME}");
		Assert.Equal("worker/worker", result);
	}

	[Theory]
	[InlineData("0", (Architecture)0)]
	[InlineData("999", (Architecture)999)]
	public void Options_NumericArchitectureUsesCoreConversionAfterExpansion(string architecture, Architecture expected)
	{
		var options = new MigrationOptions(Utility.CreateEvaluator(new global::Zongsoft.Common.Variables
		{
			["architecture"] = "${target}",
			["target"] = architecture,
		}));
		Assert.Equal(expected, options.Architecture);
	}

	[Theory]
	[InlineData("${missing}", "resolved")]
	[InlineData("${first}", "resolved")]
	[InlineData("${second}", "${first}")]
	public void Options_InvalidReference_ThrowsWhenRead(string first, string second)
	{
		var options = new MigrationOptions(Utility.CreateEvaluator(new global::Zongsoft.Common.Variables
		{
			["first"] = first,
			["second"] = second,
			["valid"] = "unaffected",
		}));

		Assert.Throws<TemplateEvaluationException>(() => options["first"]);

		Assert.Equal("unaffected", options["valid"]);
	}

	[Theory]
	[InlineData("${missing}", "valid", "MissingVariable")]
	[InlineData("${first}", "valid", "DepthExceeded")]
	[InlineData("${second}", "${first}", "DepthExceeded")]
	public void Evaluate_InvalidReference_ReturnsFailure(string first, string second, string code)
	{
		var variables = new global::Zongsoft.Common.Variables()
		{
			["first"] = first,
			["second"] = second,
		};

		var evaluator = Utility.CreateEvaluator(variables);

		Assert.False(evaluator.TryEvaluate("${first}", out var result, out var error));
		Assert.Null(result);
		Assert.Equal(code, error.Code);
		Assert.Equal(first, variables["first"]);
	}

	[Fact]
	public void Options_ChangedDependency_UpdatesResolvedValue()
	{
		var options = new MigrationOptions(Utility.CreateEvaluator(new global::Zongsoft.Common.Variables
		{
			["version"] = "1.0.0",
			["migration"] = ".deploy/default/migration/${version}/*.migration",
		}));
		Assert.Equal(".deploy/default/migration/1.0.0/*.migration", options["migration"]);

		options["version"] = "1.1.0";

		Assert.Equal(".deploy/default/migration/1.1.0/*.migration", options["migration"]);
		Assert.Equal(new Version(1, 1, 0), options.Version);
	}

	[Fact]
	public void Evaluate_DepthLimit_AllowsSixtyFourLevelsIncludingRootAndRejectsNext()
	{
		var variables = new global::Zongsoft.Common.Variables();
		for(var index = 0; index < 62; index++)
			variables["step" + index] = "${step" + (index + 1) + "}";
		variables["step62"] = "resolved";

		var evaluator = Utility.CreateEvaluator(variables);
		Assert.Equal("resolved", evaluator.Evaluate("${step0}"));

		variables["step62"] = "${step63}";
		variables["step63"] = "resolved";
		Assert.False(evaluator.TryEvaluate("${step0}", out var result, out var error));
		Assert.Null(result);
		Assert.Equal("DepthExceeded", error.Code);
	}

	[Fact]
	public void Options_StructuredNamesAndSameKeysRemainInstanceScoped()
	{
		var first = new MigrationOptions(Utility.CreateEvaluator(new global::Zongsoft.Common.Variables
		{
			["channel_name"] = "alpha",
			["settings_0"] = "one",
			["profile_key"] = "primary",
			["route"] = "${channel_name}/${settings_0}/${profile_key}",
		}));
		var second = new MigrationOptions(Utility.CreateEvaluator(new global::Zongsoft.Common.Variables
		{
			["channel_name"] = "beta",
			["settings_0"] = "two",
			["profile_key"] = "secondary",
			["route"] = "${channel_name}/${settings_0}/${profile_key}",
		}));

		Assert.Equal("alpha/one/primary", first["route"]);
		Assert.Equal("beta/two/secondary", second["route"]);
		first["channel_name"] = "updated";
		Assert.Equal("updated/one/primary", first["route"]);
		Assert.Equal("beta/two/secondary", second["route"]);
	}
	#endregion
}
