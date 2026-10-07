using System.Collections.Generic;
using System.Linq;

using Xunit;
using Zongsoft.Components;

namespace Zongsoft.Tools.Containerizer.Tests;

public sealed class OutputTests
{
	[Fact]
	public void LocalizedMessagesStyleParametersSeparatelyWithoutExpandingTheirContents()
	{
		var content = Output.FormatMessage("{1}：缺少 {0}，请提供 {0}。", CommandOutletColor.Magenta, "$(password){1}", "redis");
		var segments = GetSegments(content);
		Assert.Equal("redis：缺少 $(password){1}，请提供 $(password){1}。", string.Concat(segments.Select(segment => segment.Text)));
		Assert.All(segments.FindAll(segment => segment.Style == CommandOutletStyles.Bold), segment => Assert.Equal(CommandOutletColor.Yellow, segment.ForegroundColor));
		Assert.All(segments.FindAll(segment => segment.Style != CommandOutletStyles.Bold && !string.IsNullOrEmpty(segment.Text)), segment => Assert.Equal(CommandOutletColor.Magenta, segment.ForegroundColor));
	}

	[Fact]
	public void AdjacentAndFinalParametersRetainTheirStyle()
	{
		var content = Output.FormatMessage("{0}{1}", "redis", "mysql");
		Assert.Equal("redismysql", string.Concat(GetSegments(content).Select(segment => segment.Text)));
		Assert.All(GetSegments(content), segment =>
		{
			Assert.Equal(CommandOutletStyles.Bold, segment.Style);
			Assert.Equal(CommandOutletColor.Cyan, segment.ForegroundColor);
		});
	}

	[Fact]
	public void SyntaxCanBeAppendedToACommandWithoutLosingItsArguments()
	{
		var content = CommandOutletContent.Create("dotnet containerize").Append(Output.HighlightSyntax(" <components...> [--engine:auto|docker|podman]"));
		Assert.Equal("dotnet containerize <components...> [--engine:auto|docker|podman]", string.Concat(GetSegments(content).Select(segment => segment.Text)));
	}

	[Fact]
	public void HelpStylesOptionNamesAndValuesSeparately()
	{
		var content = Output.HighlightSyntax("[--engine:auto|docker|podman] --name:<name>");
		Assert.Equal("[--engine:auto|docker|podman] --name:<name>", string.Concat(GetSegments(content).Select(segment => segment.Text)));
		Assert.Contains(GetSegments(content), segment => segment.Text == "--engine" && segment.ForegroundColor == CommandOutletColor.Cyan);
		Assert.Contains(GetSegments(content), segment => segment.Text == "auto|docker|podman" && segment.ForegroundColor == CommandOutletColor.Green);
		Assert.Contains(GetSegments(content), segment => segment.Text == "<name>" && segment.ForegroundColor == CommandOutletColor.Green);
	}

	private static List<CommandOutletContent> GetSegments(CommandOutletContent content)
	{
		var result = new List<CommandOutletContent>();
		for(var current = content.First; current != null; current = current.Next)
			result.Add(current);
		return result;
	}
}
