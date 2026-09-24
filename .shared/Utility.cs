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
using System.Collections;
using System.Collections.Generic;

using Zongsoft.Components;
using Zongsoft.Configuration.Profiles;

#if DEPLOYER
namespace Zongsoft.Tools.Deployer;
#elif PACKAGER
namespace Zongsoft.Tools.Packager;
#elif MIGRATOR
namespace Zongsoft.Tools.Migrator;
#else
namespace Zongsoft.Tools;
#endif

/// <summary>三个独立工具共用的命令与文件辅助方法。</summary>
internal static partial class Utility
{
	#region 命令方法
	/// <summary>将详细信息另起一行并整体缩进，保留已有的多行层级。</summary>
	internal static string Indent(string text) => Environment.NewLine + "\t" + text.ReplaceLineEndings(Environment.NewLine + "\t");

	/// <summary>将工具的绝对输入适配为 Core 本地搜索，结果保留逻辑名称。</summary>
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

	internal static Dictionary<string, string> CreateVariables(CommandContext context, string directory = null)
	{
		ArgumentNullException.ThrowIfNull(context);
		var variables = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

		foreach(var option in context.Descriptor.Options)
			variables[option.Name] = option.DefaultValue?.ToString();

		foreach(DictionaryEntry variable in Environment.GetEnvironmentVariables())
			variables[variable.Key.ToString()] = variable.Value?.ToString();

		if(directory != null)
			LoadEnvironmentVariables(variables, directory);

		foreach(var option in context.Options)
			variables[option.Key] = option.Value?.ToString();

		return variables;
	}
	#endregion

	#region 环境文件
	/// <summary>从文件系统根目录到指定目录依次加载 .env，将段落和条目以下划线拼接为变量名。</summary>
	internal static void LoadEnvironmentVariables(IDictionary<string, string> variables, string directory)
	{
		ArgumentNullException.ThrowIfNull(variables);
		ArgumentException.ThrowIfNullOrWhiteSpace(directory);

		var paths = new Stack<string>();
		for(var current = new DirectoryInfo(directory); current != null; current = current.Parent)
			paths.Push(Path.Combine(current.FullName, ".env"));

		while(paths.TryPop(out var path))
		{
			FileStream stream;

			//只忽略打开阶段的缺失文件；权限、读取及解析错误必须终止初始化。
			try
			{
				stream = File.OpenRead(path);
			}
			catch(FileNotFoundException) { continue; }
			catch(DirectoryNotFoundException) { continue; }

			using(stream)
				Populate(Profile.Load(stream), null);
		}

		void Populate(IEnumerable<ProfileItem> items, string prefix)
		{
			foreach(var item in items)
			{
				switch(item)
				{
					case ProfileEntry entry:
						variables[prefix == null ? entry.Name : $"{prefix}_{entry.Name}"] = entry.Value;
						break;
					case ProfileSection section:
						Populate(section, prefix == null ? section.Name : $"{prefix}_{section.Name}");
						break;
				}
			}
		}
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
