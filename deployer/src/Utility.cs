/*
 *   _____                                ______
 *  /_   /  ____  ____  ____  _________  / __/ /_
 *    / /  / __ \/ __ \/ __ \/ ___/ __ \/ /_/ __/
 *   / /__/ /_/ / / / / /_/ /\_ \/ /_/ / __/ /_
 *  /____/\____/_/ /_/\__  /____/\____/_/  \__/
 *                   /____/
 *
 * Authors:
 *   钟峰(Popeye Zhong) <zongsoft@gmail.com>
 *
 * The MIT License (MIT)
 *
 * Copyright (C) 2015-2025 Zongsoft Corporation <http://www.zongsoft.com>
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
using System.Linq;
using System.Collections.Generic;

namespace Zongsoft.Tools.Deployer;

/// <summary>提供部署语法所需的路径、框架过滤及条件表达式辅助功能。</summary>
internal static class Utility
{
	internal const string FRAMEWORK_VARIABLE = "Framework";

	public static readonly char[] TARGET_SEPARATORS = [',', ';'];
	public static readonly char[] PATH_SEPARATORS = [Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar];

	public static bool IsDirectory(string path) => !string.IsNullOrEmpty(path) && IsDirectorySeparator(path[^1]);
	public static bool IsDirectorySeparator(char chr) => chr == Path.DirectorySeparatorChar || chr == Path.AltDirectorySeparatorChar;
	public static string GetTargetFramework(IDictionary<string, string> variables) => TryGetTargetFramework(variables, out var value) ? value : null;
	public static bool TryGetTargetFramework(IDictionary<string, string> variables, out string value)
	{
		if(variables == null || variables.Count == 0)
		{
			value = null;

			return false;
		}

		return variables.TryGetValue(FRAMEWORK_VARIABLE, out value) && !string.IsNullOrEmpty(value);
	}

	public static bool IsTargetFramework(IDictionary<string, string> variables, string targets)
	{
		if(string.IsNullOrEmpty(targets))
			return true;

		return TryGetTargetFramework(variables, out var framework) &&
			IsTargetFramework(framework, string.IsNullOrEmpty(targets) ? [] : targets.Split(TARGET_SEPARATORS, StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries));
	}

	public static bool IsTargetFramework(string value, params string[] targets)
	{
		if(targets == null || targets.Length == 0)
			return true;

		var framework = NuGet.Frameworks.NuGetFramework.Parse(value.ToLowerInvariant());

		if(framework.IsUnsupported)
			throw new FormatException(string.Format(Properties.Resources.Review_InvalidOption, "Framework", value));

		foreach(var item in targets)
		{
			var uplook = item.EndsWith('^');
			var target = NuGet.Frameworks.NuGetFramework.Parse((uplook ? item[..^1] : item).ToLowerInvariant());

			if(target.IsUnsupported)
				throw new FormatException(string.Format(Properties.Resources.Review_InvalidFilter, item));

			if(StringComparer.OrdinalIgnoreCase.Equals(framework.Framework, target.Framework) && StringComparer.OrdinalIgnoreCase.Equals(framework.Platform, target.Platform)
				&& (uplook ? framework.Version >= target.Version && framework.PlatformVersion >= target.PlatformVersion : framework.Version == target.Version && framework.PlatformVersion == target.PlatformVersion))
				return true;
		}

		return false;
	}

	public static bool IsDeploymentFile(string filePath)
	{
		if(string.IsNullOrWhiteSpace(filePath))
			return false;

		//如果指定的文件的扩展名为.deploy，则判断为部署文件
		return string.Equals(Path.GetExtension(filePath), ".deploy", StringComparison.OrdinalIgnoreCase);
	}

	/// <summary>提供部署项必须条件处理的工具类。</summary>
	public static class Requisition
	{
		public static ReadOnlySpan<char> GetRequisites(ReadOnlySpan<char> text, out ReadOnlySpan<char> requisites)
		{
			requisites = default;

			if(text.IsEmpty)
				return text;

			var index = text.IndexOf("<");

			if(index >= 0)
			{
				if(text.TrimEnd()[^1] != '>' || text[(index + 1)..^1].IndexOfAny(['<', '>']) >= 0)
					throw new FormatException(string.Format(Properties.Resources.Review_InvalidFilter, text.ToString()));

				requisites = text[(index + 1)..].Trim();
				text = text[..index].Trim();

				index = requisites.IndexOf('>');

				if(index > 0)
					requisites = requisites[..index].Trim();
				else
					throw new FormatException(string.Format(Properties.Resources.Review_InvalidFilter, text.ToString()));
			}

			return text.Trim();
		}

		public static bool IsRequisites(IDictionary<string, string> variables, ReadOnlySpan<char> requisites)
		{
			if(requisites.IsEmpty)
				return true;

			var combiner = '|';
			var position = 0;
			bool? result = null;

			for(int i = 0; i < requisites.Length; i++)
			{
				if(requisites[i] == '|' || requisites[i] == '&')
				{
					var requisite = requisites[position..i].Trim();
					var matched = IsRequisite(variables, requisite);
					result = GetResult(result, matched, combiner);

					combiner = requisites[i];
					position = i + 1;
				}
			}

			if(position < requisites.Length)
			{
				var matched = IsRequisite(variables, requisites[position..].Trim());

				return GetResult(result, matched, combiner);
			}

			throw new FormatException(string.Format(Properties.Resources.Review_InvalidFilter, requisites.ToString()));

			static bool GetResult(bool? result, bool value, char combiner)
			{
				if(result == null)
					return value;

				if(combiner == '|')
					return result.Value || value;
				else
					return result.Value && value;
			}
		}

		private static bool IsRequisite(IDictionary<string, string> variables, ReadOnlySpan<char> requisite)
		{
			if(requisite.IsEmpty || requisite.SequenceEqual("!"))
				throw new FormatException(string.Format(Properties.Resources.Review_InvalidFilter, requisite.ToString()));

			bool result;
			ReadOnlySpan<char> name, value;
			var index = requisite.IndexOf(':');

			switch(index)
			{
				case 0:
					return false;
				case < 0:
					name = requisite[0] == '!' ? requisite[1..].Trim() : requisite.Trim();
					result = variables.ContainsKey(name.ToString());
					return requisite[0] == '!' ? !result : result;
				default:
					name = requisite[0] == '!' ? requisite[1..index].Trim() : requisite[0..index].Trim();
					value = requisite[(index + 1)..].Trim();

					if(value.IsEmpty)
					{
						result = variables.ContainsKey(name.ToString());

						return requisite[0] == '!' ? !result : result;
					}

					if(name.Equals(FRAMEWORK_VARIABLE, StringComparison.OrdinalIgnoreCase))
					{
						result = IsTargetFramework(variables, value.ToString());

						return requisite[0] == '!' ? !result : result;
					}

					if(variables.TryGetValue(name.ToString(), out var variable))
					{
						var parts = value.ToString().Split(',', StringSplitOptions.TrimEntries);
						result = parts.Contains(variable.Trim(), StringComparer.OrdinalIgnoreCase);

						return requisite[0] == '!' ? !result : result;
					}

					return requisite[0] == '!';
			}
		}
	}
}
