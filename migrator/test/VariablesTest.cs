using System;
using System.Collections.Generic;

using Xunit;

namespace Zongsoft.Tools.Migrator.Tests;

public sealed class VariablesTest
{
	#region 变量展开
	[Fact]
	public void Initialize_UnusedInvalidVariables_DoesNotBlockUsedValues()
	{
		Normalizer.Initialize(new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
		{
			["name"] = "zongsoft.daemon",
			["payload"] = "$(name)/bin",
			["unused"] = "$(missing)",
			["loop"] = "$(loop)",
		});

		Assert.Equal("zongsoft.daemon/bin", Normalizer.Variables["payload"]);
		Assert.Equal("zongsoft.daemon", Normalizer.Variables.Name);
		Assert.Throws<InvalidOperationException>(() => Normalizer.Variables["unused"]);
		Assert.Throws<InvalidOperationException>(() => Normalizer.Variables["loop"]);
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
	#endregion
}
