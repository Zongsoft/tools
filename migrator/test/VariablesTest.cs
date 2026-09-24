using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

using Xunit;

namespace Zongsoft.Tools.Migrator.Tests;

public sealed class VariablesTest
{
	#region 变量展开
	[Fact]
	public void Variables_UnusedInvalidVariables_DoesNotBlockUsedValues()
	{
		var variables = new Variables(new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
		{
			["name"] = "zongsoft.daemon",
			["payload"] = "$(name)/bin",
			["unused"] = "$(missing)",
			["loop"] = "$(loop)",
		});

		Assert.Equal("zongsoft.daemon/bin", variables["payload"]);
		Assert.Equal("zongsoft.daemon", variables.Name);
		Assert.Throws<InvalidOperationException>(() => variables["unused"]);
		Assert.Throws<InvalidOperationException>(() => variables["loop"]);
	}

	[Fact]
	public void Normalize_NestedAndRepeatedReferences_ExpandsBothSyntaxes()
	{
		var variables = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
		{
			["name"] = "zongsoft.daemon",
			["scheme"] = "default",
			["root"] = "$(scheme)/%NAME%",
		};

		var result = Normalizer.Normalize("$(root)/$(name)-%name%", variables);

		Assert.True(result.Succeed);
		Assert.Equal("default/zongsoft.daemon/zongsoft.daemon-zongsoft.daemon", result.Value);
		Assert.Equal("$(scheme)/%NAME%", variables["root"]);
	}

	[Fact]
	public void Normalize_OrdinaryDictionaryIgnoresVariableNameCase()
	{
		var variables = new Dictionary<string, string>
		{
			["Root"] = "$(service.name)",
			["Service.Name"] = "worker",
		};

		var result = Normalizer.Normalize("$(ROOT)/%SERVICE.NAME%", variables);
		Assert.True(result.Succeed);
		Assert.Equal("worker/worker", result.Value);
	}

	[Theory]
	[InlineData("0", (Architecture)0)]
	[InlineData("999", (Architecture)999)]
	public void Variables_NumericArchitectureUsesCoreConversionAfterExpansion(string architecture, Architecture expected)
	{
		var variables = new Variables(new Dictionary<string, string>
		{
			["architecture"] = "$(target)",
			["target"] = architecture,
		});
		Assert.Equal(expected, variables.Architecture);
	}

	[Theory]
	[InlineData("$(missing)", "resolved", "missing")]
	[InlineData("$(first)", "resolved", "first")]
	[InlineData("$(second)", "%first%", "second")]
	public void Variables_InvalidReference_ThrowsWhenRead(string first, string second, string failedVariable)
	{
		var variables = new Variables(new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
		{
			["first"] = first,
			["second"] = second,
			["valid"] = "unaffected",
		});

		var error = Assert.Throws<InvalidOperationException>(() => variables["first"]);

		Assert.Contains(failedVariable, error.Message, StringComparison.OrdinalIgnoreCase);
		Assert.Equal("unaffected", variables["valid"]);
	}

	[Theory]
	[InlineData("$(missing)", "valid", "missing")]
	[InlineData("$(first)", "valid", "first")]
	[InlineData("$(second)", "%first%", "first")]
	public void Normalize_InvalidReference_ReturnsFailure(string first, string second, string failedVariable)
	{
		var variables = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
		{
			["first"] = first,
			["second"] = second,
		};

		var result = Normalizer.Normalize("$(first)", variables);

		Assert.False(result.Succeed);
		Assert.Equal(failedVariable, result.Value);
		Assert.Equal(first, variables["first"]);
	}

	[Fact]
	public void Variables_ChangedDependency_UpdatesResolvedValue()
	{
		var variables = new Variables(new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
		{
			["version"] = "1.0.0",
			["migration"] = ".deploy/default/migration/$(version)/*.migration",
		});
		Assert.Equal(".deploy/default/migration/1.0.0/*.migration", variables["migration"]);

		variables["version"] = "1.1.0";

		Assert.Equal(".deploy/default/migration/1.1.0/*.migration", variables["migration"]);
		Assert.Equal(new Version(1, 1, 0), variables.Version);
	}

	[Fact]
	public void Normalize_DepthLimit_AllowsSixtyFourReferencesAndRejectsNext()
	{
		var variables = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
		for(var index = 0; index < 63; index++)
			variables["step" + index] = "$(step" + (index + 1) + ")";
		variables["step63"] = "resolved";

		var withinLimit = Normalizer.Normalize("$(step0)", variables);
		Assert.True(withinLimit.Succeed);
		Assert.Equal("resolved", withinLimit.Value);

		variables["step63"] = "$(step64)";
		variables["step64"] = "resolved";
		var beyondLimit = Normalizer.Normalize("$(step0)", variables);
		Assert.False(beyondLimit.Succeed);
		Assert.Equal("step64", beyondLimit.Value);
	}

	[Fact]
	public void Variables_StructuredNamesAndSameKeysRemainInstanceScoped()
	{
		var first = new Variables(new Dictionary<string, string>
		{
			["channel.name"] = "alpha",
			["settings[0]"] = "one",
			["profile-key"] = "primary",
			["route"] = "$(channel.name)/%settings[0]%/$(profile-key)",
		});
		var second = new Variables(new Dictionary<string, string>
		{
			["channel.name"] = "beta",
			["settings[0]"] = "two",
			["profile-key"] = "secondary",
			["route"] = "$(channel.name)/%settings[0]%/$(profile-key)",
		});

		Assert.Equal("alpha/one/primary", first["route"]);
		Assert.Equal("beta/two/secondary", second["route"]);
		first["channel.name"] = "updated";
		Assert.Equal("updated/one/primary", first["route"]);
		Assert.Equal("beta/two/secondary", second["route"]);
	}
	#endregion
}
