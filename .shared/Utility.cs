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
using System.Text;
using System.Collections.Generic;

using Zongsoft.Common;
using Zongsoft.Components;
using Zongsoft.Text.Templating;
using Zongsoft.Configuration.Profiles;

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

/// <summary>独立制作工具共用的命令与文件辅助方法。</summary>
internal static partial class Utility
{
	#region 命令方法
	/// <summary>将详细信息另起一行并整体缩进，保留已有的多行层级。</summary>
	/// <param name="text">需要缩进的多行文本。</param>
	/// <returns>另起一行并以 Tab 缩进后的文本。</returns>
	internal static string Indent(string text) => Environment.NewLine + "\t" + text.ReplaceLineEndings(Environment.NewLine + "\t");

	/// <summary>将工具的绝对输入适配为 Core 本地搜索，结果保留逻辑名称。</summary>
	/// <param name="path">要搜索的绝对路径或路径表达式。</param>
	/// <param name="files">是否只匹配文件；为假时同时匹配文件和目录。</param>
	/// <param name="sourceDirectory">解析相对搜索模式的基准目录；为空时根据输入路径确定。</param>
	/// <returns>保留逻辑名称的文件系统匹配项。</returns>
	public static IEnumerable<Zongsoft.IO.Searcher.Match> Search(string path, bool files = false, string sourceDirectory = null)
	{
		path = Path.GetFullPath(path);

		var origin = sourceDirectory ?? (path.IndexOfAny(['*', '?']) < 0 ? Path.GetDirectoryName(path) : Path.GetPathRoot(path));
		var directory = new DirectoryInfo(origin ?? path);
		var pattern = Path.GetRelativePath(directory.FullName, path);

		return Zongsoft.IO.Searcher.Search(directory, pattern, files ? Zongsoft.IO.Searcher.Target.Files : Zongsoft.IO.Searcher.Target.Both);
	}

	/// <summary>判断指定的版本号是否为零。</summary>
	/// <param name="version">指定的版本。</param>
	/// <returns>如果版本号为零则返回真(<c>True</c>)，否则返回假(<c>False</c>)。</returns>
	public static bool IsZero(this Version version) => version == null ||
	(
		version.Major == 0 &&
		version.Minor == 0 &&
		version.Build <= 0 &&
		version.Revision <= 0
	);

	internal static string FormatCommand(string command, ReadOnlySpan<string> arguments)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(command);
		var text = new StringBuilder(command);

		foreach(var argument in arguments)
		{
			text.Append(' ');
			var value = argument ?? string.Empty;

			if(value.StartsWith('-'))
			{
				var index = value.IndexOfAny([':', '=']);
				if(index < 0)
				{
					text.Append(value);
					continue;
				}

				text.Append(value.AsSpan(0, index + 1));
				value = value[(index + 1)..];
			}

			// Core 命令行解析器会消耗引号内的反斜杠，必须先转义。
			text.Append('"').Append(value.Replace("\\", "\\\\").Replace("\"", "\\\"")).Append('"');
		}

		return text.ToString();
	}
	#endregion

	#region 模板来源
	/// <summary>使用 Core 评估器及原始变量来源创建本次命令的模板环境。</summary>
	/// <param name="variables">首个原始变量来源；为空时创建可写的内存变量集合。</param>
	/// <returns>启用递归求值和变量回退的独立模板评估器。</returns>
	internal static TemplateEvaluator CreateEvaluator(IVariables variables = null)
	{
		var evaluator = new TemplateEvaluator(new() { Recursive = true, Fallback = true });
		evaluator.Providers.Add(variables ?? new global::Zongsoft.Common.Variables());
		return evaluator;
	}

	/// <summary>组合显式命令参数、配置视图、系统环境和命令选项的默认值。</summary>
	/// <param name="context">提供显式选项和描述符默认值的命令上下文。</param>
	/// <param name="directory">配置搜索目录；为空时不加载配置文件。</param>
	/// <returns>保留选项原始类型并按来源优先级查询的递归模板评估器。</returns>
	internal static TemplateEvaluator CreateEvaluator(CommandContext context, string directory = null)
	{
		ArgumentNullException.ThrowIfNull(context);
		var evaluator = CreateEvaluator(context.Options);
		evaluator.Providers.Add(global::Zongsoft.Common.Variables.Environments());

		if(directory != null)
			LoadEnvironmentProfiles(evaluator, directory);
		return evaluator;
	}

	/// <summary>通过 Core 按 <see cref="TemplateEvaluatorOptions.Fallback"/> 读取原始值；命中 <see langword="null"/> 也终止查找。</summary>
	/// <param name="evaluator">按提供程序顺序查找变量的评估器。</param>
	/// <param name="name">变量全名，可使用命名空间与名称之间的冒号。</param>
	/// <param name="value">找到的原始值；未找到时为空。</param>
	/// <returns>找到时返回 <see langword="true"/>，包括值为 <see langword="null"/> 的情况；未找到时返回 <see langword="false"/>。</returns>
	internal static bool TryGetVariable(this TemplateEvaluator evaluator, string name, out object value)
	{
		var index = name.IndexOf(':');
		return evaluator.Providers.TryGetValue(index < 0 ? null : name[..index], name[(index + 1)..], evaluator.Options.Fallback, out value);
	}

	internal static string GetOption(this TemplateEvaluator evaluator, string name) => evaluator.TryGetOption(name, out var value) ? value : null;
	internal static bool TryGetOption(this TemplateEvaluator evaluator, string name, out string value)
	{
		var found = evaluator.TryGetVariable(NormalizeVariableName(name), out var raw);
		value = raw == null ? null : evaluator.Evaluate(raw.ToString());
		return found;
	}

	internal static object GetVariable(this TemplateEvaluator evaluator, string name) => evaluator.TryGetVariable(name, out var value) ? value : null;
	internal static void SetVariable(this TemplateEvaluator evaluator, string name, object value)
	{
		if(evaluator.Providers.Count == 0 || evaluator.Providers[0] is not global::Zongsoft.Common.Variables)
			evaluator.Providers.Insert(0, new global::Zongsoft.Common.Variables());
		((global::Zongsoft.Common.Variables)evaluator.Providers[0])[NormalizeVariableName(name)] = value;
	}

	internal static string NormalizeVariableName(string name)
	{
		var index = name.IndexOf(':');
		return index < 0 ? name.Replace('.', '_').Replace('-', '_') : name[..(index + 1)] + name[(index + 1)..].Replace('.', '_').Replace('-', '_');
	}

	/// <summary>按从根到目标目录的顺序加载 .env，保留 Profile 的实时变量视图。</summary>
	/// <param name="evaluator">接收配置视图的评估器，首个来源保留给命令参数。</param>
	/// <param name="directory">配置搜索的目标目录。</param>
	internal static void LoadEnvironmentProfiles(TemplateEvaluator evaluator, string directory)
	{
		var paths = new Stack<string>();
		for(var current = new DirectoryInfo(directory); current != null; current = current.Parent)
			paths.Push(Path.Combine(current.FullName, ".env"));

		while(paths.TryPop(out var path))
		{
			FileStream stream;

			try { stream = File.OpenRead(path); }
			catch(FileNotFoundException) { continue; }
			catch(DirectoryNotFoundException) { continue; }

			using(stream)
			{
				var profile = Profile.Load(stream, ConfigureDirectiveEvaluation(new ProfileOptions(), evaluator, 1));
				evaluator.Providers.Insert(1, profile.ToVariables());
			}
		}
	}

	/// <summary>在通用指令回调中评估参数，当前文件仅提供已经读取的条目。</summary>
	/// <param name="options">接收指令处理回调的配置选项。</param>
	/// <param name="evaluator">提供外部变量来源的评估器，可以为空。</param>
	/// <param name="profileIndex">当前配置视图的插入位置；负数表示放在所有外部来源之后。</param>
	/// <returns>已挂接参数评估回调的原选项实例。</returns>
	internal static ProfileOptions ConfigureDirectiveEvaluation(ProfileOptions options, TemplateEvaluator evaluator, int profileIndex = -1)
	{
		options.Directives.Processing += context =>
		{
			var evaluation = new TemplateEvaluator(new()
			{
				Recursive = evaluator?.Options.Recursive ?? true,
				Fallback = evaluator?.Options.Fallback ?? true,
				Culture = evaluator?.Options.Culture,
				MaximumDepth = evaluator?.Options.MaximumDepth ?? 64,
			});
			if(evaluator != null)
			{
				foreach(var provider in evaluator.Providers)
					evaluation.Providers.Add(provider);
			}

			evaluation.Providers.Insert(profileIndex < 0 ? evaluation.Providers.Count : profileIndex, context.Profile.ToVariables());
			context.Argument = evaluation.Evaluate(context.Argument);
		};
		return options;
	}
	#endregion

	#region 文本来源
	internal static string ReadTextSource(string source, string value, Func<string, string> normalize, bool fileOnly, string fileRequiredMessage, string missingMessage)
	{
		if(string.IsNullOrWhiteSpace(value))
			return null;

		if(value.StartsWith("text:", StringComparison.OrdinalIgnoreCase))
		{
			if(fileOnly)
				throw new InvalidDataException(fileRequiredMessage);

			return value[5..];
		}

		var explicitFile = value.StartsWith("file:", StringComparison.OrdinalIgnoreCase);
		if(explicitFile)
			value = value[5..];

		value = normalize(value);
		if(!explicitFile && !fileOnly && (value.Contains('\r') || value.Contains('\n')))
			return value;

		var path = Path.GetFullPath(Path.Combine(source ?? Environment.CurrentDirectory, value));
		if(File.Exists(path))
			return File.ReadAllText(path);

		if(explicitFile || fileOnly || IsPath(value))
			throw new FileNotFoundException(string.Format(missingMessage, path), path);

		return value;
	}
	#endregion

	#region 辅助方法
	private static bool IsPath(string value) => Path.IsPathFullyQualified(value) ||
		value.StartsWith("./") || value.StartsWith("../") ||
		value.StartsWith(@".\") || value.StartsWith(@"..\") ||
		(!value.Contains(' ') && (value.Contains('/') || value.Contains('\\') || value.EndsWith(".sh", StringComparison.OrdinalIgnoreCase)));
	#endregion
}
