using System;
using System.IO;
using System.Globalization;
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
		var content = Utility.FormatOutput("{1}：缺少 {0}，请提供 {0}。", CommandOutletColor.DarkRed, "${password}{1}", "redis");
		var segments = GetSegments(content);
		Assert.Equal("redis：缺少 ${password}{1}，请提供 ${password}{1}。", string.Concat(segments.Select(segment => segment.Text)));
		Assert.All(segments.FindAll(segment => segment.Style == CommandOutletStyles.Bold), segment => Assert.Equal(CommandOutletColor.Magenta, segment.ForegroundColor));
		Assert.All(segments.FindAll(segment => segment.Style != CommandOutletStyles.Bold && !string.IsNullOrEmpty(segment.Text)), segment => Assert.Equal(CommandOutletColor.DarkRed, segment.ForegroundColor));
	}

	[Fact]
	public void AdjacentAndFinalParametersRetainTheirStyle()
	{
		var content = Utility.FormatOutput("{0}{1}", "redis", "mysql");
		Assert.Equal("redismysql", string.Concat(GetSegments(content).Select(segment => segment.Text)));
		Assert.All(GetSegments(content), segment =>
		{
			Assert.Equal(CommandOutletStyles.Bold, segment.Style);
			Assert.Equal(CommandOutletColor.Cyan, segment.ForegroundColor);
		});
	}

	[Fact]
	public void WarningsPreserveCompositeFormattingAndKeepArgumentsDistinct()
	{
		const string FORMAT = "{{status}} {1}: {0,8:N2}; {1} / {2}";
		object[] arguments = [1234.5m, "D:\\input\\{0}.env", null];
		var content = Utility.FormatOutput(FORMAT, CommandOutletColor.DarkYellow, arguments);
		var segments = GetSegments(content);

		Assert.Equal(string.Format(FORMAT, arguments), CommandOutletContent.GetFullText(content));
		Assert.Contains(segments, segment => segment.Text == "D:\\input\\{0}.env" && segment.ForegroundColor == CommandOutletColor.Cyan);
		Assert.All(segments.FindAll(segment => segment.Style == CommandOutletStyles.Bold), segment => Assert.Equal(CommandOutletColor.Cyan, segment.ForegroundColor));
		Assert.All(segments.FindAll(segment => segment.Style == CommandOutletStyles.None), segment => Assert.Equal(CommandOutletColor.DarkYellow, segment.ForegroundColor));
	}

	[Fact]
	public void ExceptionOutputKeepsTheMessageLiteralAndStylesItsType()
	{
		var exception = new IOException("D:\\input\\${name}{0}.env");
		var content = Utility.FormatError(exception);
		var segments = GetSegments(content);

		Assert.Equal($"[IOException] {exception.Message}", CommandOutletContent.GetFullText(content));
		Assert.Contains(segments, segment => segment.Text == nameof(IOException) && segment.ForegroundColor == CommandOutletColor.Magenta);
		Assert.Contains(segments, segment => segment.Text == exception.Message && segment.ForegroundColor == CommandOutletColor.DarkRed);
	}

	[Theory]
	[InlineData(false)]
	[InlineData(true)]
	public void ExceptionStackTraceIsIncludedOnlyWhenRequested(bool includeStackTrace)
	{
		Action fail = () => throw new IOException("test failure");
		var exception = Record.Exception(fail);
		Assert.NotEmpty(exception.StackTrace);

		var content = Utility.FormatError(exception, includeStackTrace);
		var expected = $"[IOException] {exception.Message}";
		if(includeStackTrace)
			expected += Environment.NewLine + exception.StackTrace;

		Assert.Equal(expected, CommandOutletContent.GetFullText(content));
		Assert.All(GetSegments(content).FindAll(segment => segment.Style == CommandOutletStyles.None), segment => Assert.Equal(CommandOutletColor.DarkRed, segment.ForegroundColor));
	}

	[Fact]
	public void UnthrownExceptionHasNoEmptyStackTraceLine()
	{
		var content = Utility.FormatError(new IOException("test failure"), includeStackTrace: true);
		Assert.Equal("[IOException] test failure", CommandOutletContent.GetFullText(content));
	}

	[Fact]
	public void CallerOwnedWriterReceivesAllSegmentsAsPlainTextAndStaysOpen()
	{
		using var writer = new StringWriter();
		var content = Utility.FormatOutput("Failed: {0} / {1}", CommandOutletColor.DarkRed, "${name}{0}", "D:\\data");
		Utility.WriteOutput(writer, content);
		writer.Write("tail");

		Assert.Equal($"Failed: ${{name}}{{0}} / D:\\data{Environment.NewLine}tail", writer.ToString());
		Assert.DoesNotContain('\u001b', writer.ToString());
	}

	[Fact]
	public void SyntaxCanBeAppendedToACommandWithoutLosingItsArguments()
	{
		var content = CommandOutletContent.Create("dotnet containerize").Append(Utility.HighlightSyntax(" <components...> [--engine:auto|docker|podman]"));
		Assert.Equal("dotnet containerize <components...> [--engine:auto|docker|podman]", string.Concat(GetSegments(content).Select(segment => segment.Text)));
	}

	[Fact]
	public void HelpStylesOptionNamesAndValuesSeparately()
	{
		var content = Utility.HighlightSyntax("[--engine:auto|docker|podman] --name:<name>");
		Assert.Equal("[--engine:auto|docker|podman] --name:<name>", string.Concat(GetSegments(content).Select(segment => segment.Text)));
		Assert.Contains(GetSegments(content), segment => segment.Text == "--engine" && segment.ForegroundColor == CommandOutletColor.Cyan);
		Assert.Contains(GetSegments(content), segment => segment.Text == "auto|docker|podman" && segment.ForegroundColor == CommandOutletColor.Green);
		Assert.Contains(GetSegments(content), segment => segment.Text == "<name>" && segment.ForegroundColor == CommandOutletColor.Green);
	}

	[Fact]
	public void HelpKeepsEveryOptionAndStylesTheCommandAndMixedCaseValues()
	{
		var content = Utility.FormatHelp("dotnet-pack", "<tar|deb|rpm> [options]",
			"[--source:<directory>]", "[--compilation:Release|Debug]");
		using var writer = new StringWriter();
		Utility.WriteOutput(writer, content);

		Assert.Equal(string.Join(Environment.NewLine,
			"dotnet-pack <tar|deb|rpm> [options]", "",
			"  [--source:<directory>]", "  [--compilation:Release|Debug]", ""), writer.ToString());
		Assert.Equal(CommandOutletColor.Cyan, content.First.ForegroundColor);
		Assert.Equal(CommandOutletStyles.Bold, content.First.Style);
		Assert.Contains(GetSegments(content), segment => segment.Text == "--source" && segment.ForegroundColor == CommandOutletColor.Cyan);
		Assert.Contains(GetSegments(content), segment => segment.Text == "<directory>" && segment.ForegroundColor == CommandOutletColor.Green);
		Assert.Contains(GetSegments(content), segment => segment.Text == "Release|Debug" && segment.ForegroundColor == CommandOutletColor.Green);
		Assert.DoesNotContain('\u001b', writer.ToString());
	}

	[Theory]
	[InlineData("en-US", @"\.")]
	[InlineData("fr-FR", ",")]
	public void MeasurementUsesCallerFormatAndCultureAndWritesOnlyOnFirstDisposal(string culture, string separator)
	{
		var previous = CultureInfo.CurrentCulture;
		var messages = new List<CommandOutletContent>();
		const string STAGE = "build ${name}{1}";

		try
		{
			CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(culture);
			using var measurement = Utility.MeasureOutput(messages.Add, "{1} seconds / {0}", STAGE);
			Assert.Empty(messages);

			measurement.Dispose();
			measurement.Dispose();

			var content = Assert.Single(messages);
			Assert.Matches(@"^[0-9]+" + separator + @"[0-9] seconds / build \$\{name\}\{1\}$", CommandOutletContent.GetFullText(content));
			Assert.Contains(GetSegments(content), segment => segment.Text == STAGE && segment.ForegroundColor == CommandOutletColor.Cyan && segment.Style == CommandOutletStyles.Bold);
		}
		finally { CultureInfo.CurrentCulture = previous; }
	}

	[Fact]
	public void FailedStageStillReportsElapsedTimeWithoutReplacingTheFailure()
	{
		var messages = new List<CommandOutletContent>();
		var failure = new IOException("build failed");

		Action execute = () =>
		{
			using var measurement = Utility.MeasureOutput(messages.Add, "{0}: {1} seconds", "Build");
			throw failure;
		};
		var exception = Assert.Throws<IOException>(execute);

		Assert.Same(failure, exception);
		var content = Assert.Single(messages);
		Assert.StartsWith("Build: ", CommandOutletContent.GetFullText(content));
		Assert.EndsWith(" seconds", CommandOutletContent.GetFullText(content));
	}

	private static List<CommandOutletContent> GetSegments(CommandOutletContent content)
	{
		var result = new List<CommandOutletContent>();
		for(var current = content.First; current != null; current = current.Next)
			result.Add(current);
		return result;
	}
}
