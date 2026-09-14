using Xunit;

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
	public void Normalize_UrlPreservesSlashesDuringVariableExpansion(string endpoint)
	{
		var variables = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
		{
			["ApiEndpoint"] = endpoint,
			["Resource"] = "orders",
		};
		Assert.Equal(endpoint + "/v1/orders?next=/page/2", Normalizer.Normalize("%apiendpoint%/v1/$(Resource)?next=/page/2", variables));
		Assert.Equal("https://example.test/orders", Normalizer.Normalize("https://example.test/$(Resource)", variables));
	}

	[Theory]
	[InlineData("$(Database.Name)-%Items[0]%-$(items[1].name)")]
	[InlineData("%database.name%-$(items[0])-%Items[1].Name%")]
	public void Normalize_ConfigurationPathsAndMixedCaseExpand(string text)
	{
		var variables = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
		{
			["Database.Name"] = "business", ["Items[0]"] = "first", ["Items[1].Name"] = "second",
		};
		var failures = new List<string>();
		Assert.Equal("business-first-second", Normalizer.Normalize(text, variables, failures.Add));
		Assert.Empty(failures);
	}

	[Fact]
	public void Normalize_MissingVariableReportsName()
	{
		var failures = new List<string>();
		Assert.Equal("before-$(Missing)-after", Normalizer.Normalize("before-$(Missing)-after", new Dictionary<string, string>(), failures.Add));
		Assert.Equal(["Missing"], failures);
	}

	[Fact]
	public void GetFiles_GlobMatchesZeroOrMoreDirectories()
	{
		using var fixture = new DeploymentFixture();
		fixture.Write("content/root.txt", "root");
		fixture.Write("content/nested/child.txt", "child");
		fixture.Write("content/nested/deep/grandchild.txt", "grandchild");
		var files = DeploymentUtility.GetFiles(Path.Combine(fixture.Root, "content", "**", "*.txt"), fixture.Variables, cancellation: TestContext.Current.CancellationToken).ToArray();
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
		var files = DeploymentUtility.GetFiles(Path.Combine(fixture.Root, "content", pattern.Replace('/', Path.DirectorySeparatorChar)), fixture.Variables, cancellation: TestContext.Current.CancellationToken);
		Assert.Equal(names.Split(';', StringSplitOptions.RemoveEmptyEntries).Order(), files.Select(file => Path.GetFileName(file.Path)).Order());
	}
}
