using Xunit;

using Zongsoft.Text.Templating;

namespace Zongsoft.Tools.Deployer.Tests;

public class ParsingAndPathTest
{
	[Theory]
	[InlineData(null)]
	[InlineData("")]
	[InlineData(" ")]
	[InlineData("\t")]
	[InlineData(" \t\r\n")]
	public void GetResolver_AbsentOrWhitespaceNameReturnsDefaultSingleton(string name)
	{
		var resolver = DeploymentResolverManager.GetResolver(name);
		Assert.Same(DeploymentResolverManager.DefaultResolver.Instance, resolver);
		Assert.Equal(string.Empty, resolver.Name);
	}

	[Fact]
	public void GetResolver_UnknownNameDoesNotResolveDefault()
	{
		Assert.Null(DeploymentResolverManager.GetResolver("unknown-resolver"));
	}

	[Theory]
	[InlineData("path")]
	[InlineData("PATH")]
	[InlineData("PaTh")]
	public void GetResolver_PathNameReturnsDefaultSingleton(string name)
	{
		var resolver = DeploymentResolverManager.GetResolver(name);
		Assert.Same(DeploymentResolverManager.DefaultResolver.Instance, resolver);
		Assert.Equal(string.Empty, resolver.Name);
	}

	[Theory]
	[InlineData("https://example.test/api")]
	[InlineData("http://example.test:8080/api")]
	public void Evaluate_UrlPreservesSlashesDuringVariableExpansion(string endpoint)
	{
		var variables = new global::Zongsoft.Common.Variables
		{
			["ApiEndpoint"] = endpoint,
			["Resource"] = "orders",
		};
		var evaluator = new TemplateEvaluator(new() { Recursive = true }) { Providers = { variables } };
		Assert.Equal(endpoint + "/v1/orders?next=/page/2", evaluator.Evaluate("${apiendpoint}/v1/${Resource}?next=/page/2"));
		Assert.Equal("https://example.test/orders", evaluator.Evaluate("https://example.test/${Resource}"));
	}

	[Theory]
	[InlineData("${Database_Name}-${Items_0}-${items_1_name}")]
	[InlineData("${database_name}-${items_0}-${Items_1_Name}")]
	public void Evaluate_CanonicalNamesAndMixedCaseExpand(string text)
	{
		var variables = new global::Zongsoft.Common.Variables
		{
			["Database_Name"] = "business",
			["Items_0"] = "first",
			["Items_1_Name"] = "second",
		};
		var evaluator = new TemplateEvaluator(new() { Recursive = true }) { Providers = { variables } };
		Assert.Equal("business-first-second", evaluator.Evaluate(text));
	}

	[Fact]
	public void Evaluate_MissingVariableReportsNameAndThrows()
	{
		var evaluator = new TemplateEvaluator(new() { Recursive = true });
		var error = Assert.Throws<TemplateEvaluationException>(() => evaluator.Evaluate("before-${Missing}-after"));
		Assert.Equal("MissingVariable", error.Code);
		Assert.Equal("Missing", error.Expression);
	}

	[Fact]
	public void Evaluate_CoreVariablesIgnoreVariableNameCase()
	{
		var variables = new global::Zongsoft.Common.Variables
		{
			["Root"] = "${service_name}",
			["Service_Name"] = "worker",
		};

		var evaluator = new TemplateEvaluator(new() { Recursive = true }) { Providers = { variables } };
		Assert.Equal("worker/worker", evaluator.Evaluate("${ROOT}/${SERVICE_NAME}"));
	}

	[Fact]
	public void GetFiles_GlobMatchesZeroOrMoreDirectories()
	{
		using var fixture = new DeploymentFixture();
		fixture.Write("content/root.txt", "root");
		fixture.Write("content/nested/child.txt", "child");
		fixture.Write("content/nested/deep/grandchild.txt", "grandchild");
		var files = DeploymentUtility.GetFiles(Path.Combine(fixture.Root, "content", "**", "*.txt"), fixture.Evaluator, cancellation: TestContext.Current.CancellationToken).ToArray();
		Assert.Equal(["child.txt", "grandchild.txt", "root.txt"], files.Select(file => Path.GetFileName(file.Path)).Order().ToArray());
		Assert.Equal("nested", files.Single(file => file.Path.EndsWith("child.txt") && !file.Path.EndsWith("grandchild.txt")).Suffix);
		Assert.Equal(Path.Combine("nested", "deep"), files.Single(file => file.Path.EndsWith("grandchild.txt")).Suffix?.Replace('/', Path.DirectorySeparatorChar));
	}

	[Theory]
	[InlineData("dir?/*.txt", "one.txt;two.txt")]
	[InlineData("dir*/sub?/*.txt", "three.txt;four.txt")]
	[InlineData("dir?/**/sub?/*.txt", "three.txt;four.txt;five.txt")]
	[InlineData("absent?/*.txt", "")]
	public void GetFiles_MultipleWildcardSegmentsMatch(string pattern, string names)
	{
		using var fixture = new DeploymentFixture();
		fixture.Write("content/dir1/one.txt", "one");
		fixture.Write("content/dir2/two.txt", "two");
		fixture.Write("content/dir1/sub1/three.txt", "three");
		fixture.Write("content/dir2/sub2/four.txt", "four");
		fixture.Write("content/dir2/deep/sub3/five.txt", "five");
		var files = DeploymentUtility.GetFiles(Path.Combine(fixture.Root, "content", pattern.Replace('/', Path.DirectorySeparatorChar)), fixture.Evaluator, cancellation: TestContext.Current.CancellationToken);
		Assert.Equal(names.Split(';', StringSplitOptions.RemoveEmptyEntries).Order(), files.Select(file => Path.GetFileName(file.Path)).Order());
	}
}
