/*
 *   _____                                ______
 *  /_   /  ____  ____  ____  _________  / __/ /_
 *    / /  / __ \/ __ \/ __ \/ ___/ __ \/ /_/ __/
 *   / /__/ /_/ / / / /_/ /\_ \/ /_/ / __/ /_
 *  /____/\____/_/ /_/\__  /____/\____/_/  \__/
 *                   /____/
 *
 * Authors:
 *   钟峰(Popeye Zhong) <zongsoft@gmail.com>
 *
 * The MIT License (MIT)
 *
 * Copyright (C) 2026 Zongsoft Corporation <http://www.zongsoft.com>
 *
 * Permission is hereby granted, free of charge, to any person obtaining a copy
 * of this software and associated documentation files (the "Software"), to deal
 * in the Software without restriction, including without limitation the rights
 * to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
 * copies of the Software, and to permit persons to whom the Software is
 * furnished to do so, subject to the following conditions:
 *
 * The above copyright notice and this permission notice shall be included in all
 * copies or substantial portions of the Software.
 *
 * THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
 * IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
 * FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
 * AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
 * LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
 * OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE SOFTWARE.
 */

using System;
using System.IO;
using System.Threading;
using System.Diagnostics;
using System.Globalization;
using System.Text.RegularExpressions;

using Zongsoft.Terminals;
using Zongsoft.Components;

#if DEPLOYER
namespace Zongsoft.Tools.Deployer;
#elif PACKAGER
namespace Zongsoft.Tools.Packager;
#elif MIGRATOR
namespace Zongsoft.Tools.Migrator;
#elif CONTAINERIZER
namespace Zongsoft.Tools.Containerizer;
#else
namespace Zongsoft.Tools;
#endif

partial class Utility
{
	#region 格式方法
	/// <summary>将默认颜色的资源文本与格式化参数组合为终端内容。</summary>
	/// <param name="format">使用复合格式占位符的资源文本。</param>
	/// <param name="arguments">占位符对应的参数。</param>
	/// <returns>默认颜色的正文和粗体青色参数组成的内容链。</returns>
	internal static CommandOutletContent FormatOutput(string format, params object[] arguments) => FormatOutput(format, null, arguments);

	/// <summary>将资源文本与格式化参数组合为分别着色的终端内容；参数中的字符保持原义。</summary>
	/// <param name="format">使用复合格式占位符的资源文本。</param>
	/// <param name="color">普通文本颜色；为空时使用终端默认颜色。</param>
	/// <param name="arguments">占位符对应的参数。</param>
	/// <returns>正文和参数分段保存的内容链；错误正文中的参数使用粗体紫色，其余参数使用粗体青色。</returns>
	internal static CommandOutletContent FormatOutput(string format, CommandOutletColor? color, params object[] arguments)
	{
		CommandOutletContent content = null;
		var position = 0;

		foreach(Match match in GetOutputParameterRegex().Matches(format))
		{
			Append(format[position..match.Index], false);
			// 复合格式的值转换交给 BCL；双大括号是普通文本，不是参数。
			Append(string.Format(match.Value, arguments), match.Value is not "{{" and not "}}");
			position = match.Index + match.Length;
		}

		Append(format[position..], false);
		return content?.First ?? CommandOutletContent.Create();

		void Append(string text, bool parameter)
		{
			if(string.IsNullOrEmpty(text))
				return;

			var style = parameter ? CommandOutletStyles.Bold : CommandOutletStyles.None;
			content = content == null ? CommandOutletContent.Create(style, text) : content.Append(style, text);
			content.ForegroundColor = parameter ? color == CommandOutletColor.DarkRed ? CommandOutletColor.Magenta : CommandOutletColor.Cyan : color;
		}
	}

	/// <summary>组合命令名称、调用语法和选项列表，并统一高亮语法内容。</summary>
	/// <param name="command">需要强调显示的命令名称。</param>
	/// <param name="syntax">命令名称之后的调用语法。</param>
	/// <param name="options">按行显示的选项语法。</param>
	/// <returns>命令名称为粗体青色、选项和值分别高亮的帮助内容。</returns>
	internal static CommandOutletContent FormatHelp(string command, string syntax, params string[] options)
	{
		var content = CommandOutletContent.Create(CommandOutletStyles.Bold, CommandOutletColor.Cyan, command);
		content.Append(HighlightSyntax(" " + syntax));

		if(options.Length > 0)
			content.Last.AppendLine();

		foreach(var option in options)
			content.Last.AppendLine().Append(HighlightSyntax("  " + option));

		return content;
	}

	/// <summary>将异常类型与消息分别着色；异常消息和可选堆栈保持原文。</summary>
	/// <param name="exception">需要输出的异常。</param>
	/// <param name="includeStackTrace">是否在消息后附加已有的异常堆栈。</param>
	/// <returns>紫色异常类型及暗红色消息、堆栈组成的内容链。</returns>
	internal static CommandOutletContent FormatError(Exception exception, bool includeStackTrace = false)
	{
		var content = FormatOutput("[{0}] ", CommandOutletColor.DarkRed, exception.GetType().Name)
			.Last.Append(CommandOutletColor.DarkRed, exception.Message);

		if(includeStackTrace && !string.IsNullOrEmpty(exception.StackTrace))
			content.Append(CommandOutletColor.DarkRed, Environment.NewLine + exception.StackTrace);

		return content.First;
	}

	/// <summary>高亮命令语法中的选项名、占位符和候选值，保留原始文本。</summary>
	/// <param name="text">需要显示的命令语法文本。</param>
	/// <returns>选项名为青色、占位符和候选值为绿色的内容链。</returns>
	internal static CommandOutletContent HighlightSyntax(string text)
	{
		CommandOutletContent content = null;
		var position = 0;

		foreach(Match match in GetOutputSyntaxRegex().Matches(text))
		{
			Append(text[position..match.Index]);
			Append(match.Value, match.Value.StartsWith("--", StringComparison.Ordinal) ? CommandOutletColor.Cyan : CommandOutletColor.Green);
			position = match.Index + match.Length;
		}

		Append(text[position..]);
		return content?.First ?? CommandOutletContent.Create();

		void Append(string value, CommandOutletColor? color = null)
		{
			if(string.IsNullOrEmpty(value))
				return;

			content = content == null ? CommandOutletContent.Create(value) : content.Append(value);
			content.ForegroundColor = color;
		}
	}
	#endregion

	#region 输出方法
	/// <summary>通过终端显示控制台内容，或向调用方的写入器输出纯文本。</summary>
	/// <param name="writer">由调用方持有的写入器，不在此释放；错误重定向保留原通道。</param>
	/// <param name="content">需要输出的内容链。</param>
	internal static void WriteOutput(TextWriter writer, CommandOutletContent content)
	{
		if(ReferenceEquals(writer, Console.Out) || ReferenceEquals(writer, Console.Error) && !Console.IsOutputRedirected && !Console.IsErrorRedirected)
			Terminal.WriteLine(content);
		else
			writer.WriteLine(CommandOutletContent.GetFullText(content));
	}

	/// <summary>向当前终端的错误通道输出内容，沿用共享的终端与纯文本分流规则。</summary>
	/// <param name="content">需要输出的内容链。</param>
	internal static void WriteError(CommandOutletContent content) => WriteOutput(Terminal.Default.Error, content);

	/// <summary>输出尚未处理的命令异常，并标记已处理以避免执行器重复输出。</summary>
	/// <param name="args">命令失败事件参数；工具自身负责决定退出码。</param>
	internal static void HandleFailure(CommandExecutorFailureEventArgs args)
	{
		if(args.ExceptionHandled || args.Exception == null)
			return;

		args.Context.Output.WriteLine(FormatError(args.Exception));
		args.ExceptionHandled = true;
	}

	/// <summary>开始阶段计时，在首次释放时通过指定回调输出耗时。</summary>
	/// <param name="output">接收格式化终端内容的回调。</param>
	/// <param name="format">调用方提供的本地化复合格式，{0} 为阶段名称，{1} 为保留一位小数的秒数。</param>
	/// <param name="stage">阶段名称，按参数原文显示。</param>
	/// <returns>由调用方释放的计时对象；重复释放不重复输出。</returns>
	internal static IDisposable MeasureOutput(Action<CommandOutletContent> output, string format, string stage)
	{
		ArgumentNullException.ThrowIfNull(output);
		ArgumentNullException.ThrowIfNull(format);
		return new OutputMeasurement(output, format, stage);
	}
	#endregion

	#region 私有方法
	[GeneratedRegex(@"\{\{|\}\}|\{[0-9]+(?:,-?[0-9]+)?(?::[^{}]*)?\}")]
	private static partial Regex GetOutputParameterRegex();

	[GeneratedRegex(@"--[\w-]+|<[^>]+>|(?<=:)[A-Za-z0-9|]+")]
	private static partial Regex GetOutputSyntaxRegex();
	#endregion

	#region 嵌套类型
	private sealed class OutputMeasurement(Action<CommandOutletContent> output, string format, string stage) : IDisposable
	{
		private Action<CommandOutletContent> _output = output;
		private readonly long _timestamp = Stopwatch.GetTimestamp();

		/// <summary>输出阶段耗时；重复释放不重复调用输出回调。</summary>
		public void Dispose()
		{
			var callback = Interlocked.Exchange(ref _output, null);
			callback?.Invoke(FormatOutput(format, stage, Stopwatch.GetElapsedTime(_timestamp).TotalSeconds.ToString("0.0", CultureInfo.CurrentCulture)));
		}
	}
	#endregion
}
