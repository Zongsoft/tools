using System.Collections.Generic;

using Xunit;

namespace Zongsoft.Tools.Packager.Tests.Web;

public class VariableEscapeTest
{
	[Fact]
	public void EscapesRemainLiteralAcrossRecursiveExpansion()
	{
		var variables = new Dictionary<string, string>
		{
			["value"] = "value",
			["nested"] = "$$(undefined)-%%undefined%%-$(value)",
			["cycle"] = "$$(cycle)",
		};
		var result = VariableEvaluator.Evaluate("$(nested)/$(cycle)/$host/${host}/$(value)/%value%", variables, allowEscapes: true);
		Assert.True(result.Succeed);
		Assert.Equal("$(undefined)-%undefined%-value/$(cycle)/$host/${host}/value/value", result.Value);
		Assert.False(VariableEvaluator.Evaluate("$(nested)", variables).Succeed);
	}
}
