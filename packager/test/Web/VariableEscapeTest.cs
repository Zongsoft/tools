using System.Collections.Generic;

using Xunit;

namespace Zongsoft.Tools.Packager.Tests.Web;

public class VariableEscapeTest
{
	[Fact]
	public void EscapesRemainLiteralAcrossRecursiveExpansion()
	{
		var variables = new global::Zongsoft.Common.Variables
		{
			["value"] = "value",
			["nested"] = "\\${undefined}-\\${undefined}-${value}",
			["cycle"] = "\\${cycle}",
		};
		var evaluator = Utility.CreateEvaluator(variables);
		Assert.Equal("${undefined}-${undefined}-value/${cycle}/$host/${host}/value/value", evaluator.Evaluate("${nested}/${cycle}/$host/\\${host}/${value}/${value}"));
		Assert.Equal("${undefined}-${undefined}-value", evaluator.Evaluate("${nested}"));
	}

	[Fact]
	public void LegacySyntaxRemainsLiteralAndCoreEscapesApplyToAllText()
	{
		var variables = new global::Zongsoft.Common.Variables { ["path"] = @"C:\\tools\\new", ["name"] = "resolved" };
		var evaluator = Utility.CreateEvaluator(variables);

		Assert.Equal(@"C:\tools\new", evaluator.Evaluate("${path}"));
		Assert.Equal(@"C:\tools\new", evaluator.Evaluate(@"C:\\tools\\new"));
		Assert.Equal("first\nsecond", evaluator.Evaluate(@"first\nsecond"));
		Assert.Equal("$(name)/%name%", evaluator.Evaluate("$(name)/%name%"));
		Assert.Equal("${name}/resolved", evaluator.Evaluate("\\${name}/${name}"));
	}
}
