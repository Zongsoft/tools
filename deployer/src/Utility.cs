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

using Zongsoft.Text.Templating;

namespace Zongsoft.Tools.Deployer;

/// <summary>提供部署语法所需的路径、框架过滤及条件表达式辅助功能。</summary>
internal static partial class Utility
{
	#region 常量定义
	internal const string FRAMEWORK_VARIABLE = "Framework";

	public static readonly char[] TARGET_SEPARATORS = [',', ';'];
	public static readonly char[] PATH_SEPARATORS = [Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar];
	#endregion

	#region 路径处理
	public static bool IsDirectory(string path) => !string.IsNullOrEmpty(path) && IsDirectorySeparator(path[^1]);
	public static bool IsDirectorySeparator(char chr) => chr == Path.DirectorySeparatorChar || chr == Path.AltDirectorySeparatorChar;
	#endregion

	#region 框架匹配
	/// <summary>获取求值后的目标框架选项。</summary>
	/// <param name="evaluator">提供框架选项的模板评估器。</param>
	/// <returns>目标框架；没有有效选项时为空。</returns>
	public static string GetTargetFramework(TemplateEvaluator evaluator) => TryGetTargetFramework(evaluator, out var value) ? value : null;
	/// <summary>尝试获取非空的目标框架选项。</summary>
	/// <param name="evaluator">提供框架选项的评估器，可以为空。</param>
	/// <param name="value">目标框架求值结果。</param>
	/// <returns>是否找到非空的目标框架。</returns>
	public static bool TryGetTargetFramework(TemplateEvaluator evaluator, out string value)
	{
		if(evaluator == null || evaluator.Providers.Count == 0)
		{
			value = null;

			return false;
		}

		return evaluator.TryGetOption(FRAMEWORK_VARIABLE, out value) && !string.IsNullOrEmpty(value);
	}

	/// <summary>判断目标框架是否满足指定筛选条件。</summary>
	/// <param name="evaluator">提供目标框架的模板评估器。</param>
	/// <param name="targets">以逗号或分号分隔的框架条件。</param>
	/// <returns>没有筛选条件或至少满足一项条件时为真。</returns>
	public static bool IsTargetFramework(TemplateEvaluator evaluator, string targets)
	{
		if(string.IsNullOrEmpty(targets))
			return true;

		return TryGetTargetFramework(evaluator, out var framework) &&
			IsTargetFramework(framework, targets.Split(TARGET_SEPARATORS, StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries));
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
	#endregion

	#region 部署文件
	public static bool IsDeploymentFile(string filePath)
	{
		if(string.IsNullOrWhiteSpace(filePath))
			return false;

		//如果指定的文件的扩展名为.deploy，则判断为部署文件
		return string.Equals(Path.GetExtension(filePath), ".deploy", StringComparison.OrdinalIgnoreCase);
	}
	#endregion

	#region 条件解析
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

		/// <summary>根据当前变量来源求值部署项的条件表达式。</summary>
		/// <param name="evaluator">提供条件变量的模板评估器。</param>
		/// <param name="requisites">包含逻辑组合的条件文本。</param>
		/// <returns>条件为空或条件满足时为真。</returns>
		public static bool IsRequisites(TemplateEvaluator evaluator, ReadOnlySpan<char> requisites)
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
					var matched = IsRequisite(evaluator, requisite);
					result = GetResult(result, matched, combiner);

					combiner = requisites[i];
					position = i + 1;
				}
			}

			if(position < requisites.Length)
			{
				var matched = IsRequisite(evaluator, requisites[position..].Trim());

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

		private static bool IsRequisite(TemplateEvaluator evaluator, ReadOnlySpan<char> requisite)
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
					result = evaluator.TryGetVariable(Utility.NormalizeVariableName(name.ToString()), out _);
					return requisite[0] == '!' ? !result : result;
				default:
					name = requisite[0] == '!' ? requisite[1..index].Trim() : requisite[0..index].Trim();
					value = requisite[(index + 1)..].Trim();

					if(value.IsEmpty)
					{
						result = evaluator.TryGetVariable(Utility.NormalizeVariableName(name.ToString()), out _);

						return requisite[0] == '!' ? !result : result;
					}

					if(name.Equals(FRAMEWORK_VARIABLE, StringComparison.OrdinalIgnoreCase))
					{
						result = IsTargetFramework(evaluator, value.ToString());

						return requisite[0] == '!' ? !result : result;
					}

					if(evaluator.TryGetOption(name.ToString(), out var variable))
					{
						var parts = value.ToString().Split(',', StringSplitOptions.TrimEntries);
						result = parts.Contains(variable?.Trim(), StringComparer.OrdinalIgnoreCase);

						return requisite[0] == '!' ? !result : result;
					}

					return requisite[0] == '!';
			}
		}
	}
	#endregion
}
