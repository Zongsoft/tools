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
using System.Linq;
using System.Collections.Generic;

using Microsoft.Extensions.Configuration;

using Zongsoft.Configuration;
using Zongsoft.Text.Templating;
using Zongsoft.Configuration.Xml;
using Zongsoft.Reflection.Expressions;

namespace Zongsoft.Tools.Packager;

partial class BindingExtractor
{
	#region 私有方法
	private static void ProcessInstruction(XmlConfigurationInstructionContext context)
	{
		if(context.Instruction.Name.Equals(INSTRUCTION_NAME, StringComparison.OrdinalIgnoreCase))
		{
			context.Handled = true;
			return;
		}

		if(!context.Instruction.Name.Equals("import", StringComparison.OrdinalIgnoreCase))
			return;

		IReadOnlyDictionary<string, string> attributes;

		try
		{
			attributes = context.GetAttributes();
		}
		catch(FormatException)
		{
			throw Error(context.Instruction, "/", "import.attributes", "syntax");
		}

		if(!attributes.TryGetValue("path", out var path) || string.IsNullOrWhiteSpace(path))
			throw Error(context.Instruction, "/", "import.path", "required");

		var directory = context.FilePath.Contains('/') ? context.FilePath[..(context.FilePath.LastIndexOf('/') + 1)] : "";
		string target;

		try
		{
			target = NormalizePath(path, directory);
		}
		catch(FormatException)
		{
			throw Error(context.Instruction, "/", "import.path", "virtual-path");
		}

		var relative = string.Concat(Enumerable.Repeat("../", directory.Count(character => character == '/'))) + target;

		//改写执行参数，原指令及来源仍由 Core 保留；导入位置、可选缺失、循环和深度均由 Core 检查
		context.Data = string.Join(' ', attributes.Select(attribute => attribute.Key + "=\"" +
			System.Security.SecurityElement.Escape(attribute.Key.Equals("path", StringComparison.OrdinalIgnoreCase) ? relative : attribute.Value) + "\""));
	}
	#endregion

	#region 嵌套类型
	/// <summary>为当前候选项提供只读模板导航及按需解析的 Settings 视图。</summary>
	private sealed class ConfigurationView(IConfigurationProvider provider)
	{
		#region 成员字段
		private readonly Dictionary<string, Settings> _settings = new(StringComparer.OrdinalIgnoreCase);
		#endregion

		#region 公共属性
		public string Path { get; set; }
		public string Reference { get; private set; }
		#endregion

		#region 公共方法
		public string Evaluate(string template)
		{
			this.Reference = null;
			var evaluator = new TemplateEvaluator(new()
			{
				Culture = System.Globalization.CultureInfo.InvariantCulture,
				Recursive = false,
				Fallback = false,
			});

			evaluator.Resolving += (_, context) =>
			{
				this.Reference = context.Expression;
				if(!string.IsNullOrEmpty(context.Namespace) || !context.Name.Equals("this", StringComparison.OrdinalIgnoreCase))
					return;

				context.Value = this.Resolve(MemberExpression.Parse(context.Expression));
				context.Handled = true;
			};

			return evaluator.Evaluate(template);
		}

		public bool Match(string reference, string[] candidates)
		{
			this.Reference = reference;
			var field = ParseField(reference);
			var path = GetPath(this.Path, field.Path);
			var found = provider.TryGet(path, out var value);

			if(found && field.Key != null)
				found = this.GetSettings(path, value).TryGetValue(field.Key, out value);

			return found && (candidates == null || FilterExpression.Matches(value, candidates));
		}

		public static void ValidateField(string reference) => _ = ParseField(reference);
		#endregion

		#region 私有方法
		private object Resolve(IMemberExpression expression)
		{
			if(expression is not IdentifierExpression identifier || !identifier.Name.Equals("this", StringComparison.OrdinalIgnoreCase))
				throw new InvalidOperationException();

			object value = new Node(this.Path, true);

			for(var member = expression.Next; member != null; member = member.Next)
			{
				if(value is not Node node)
					throw new InvalidOperationException();

				if(member is IdentifierExpression property)
					value = new Node(GetPath(node.Path, property.Name));
				else if(member is IndexerExpression indexer && indexer.Arguments.Count == 1)
				{
					var argument = indexer.Arguments[0];
					var key = argument is ConstantExpression constant ? constant.Value : this.Resolve(argument);

					if(key is not string text)
						throw new InvalidOperationException();

					if(!node.Root && provider.TryGet(node.Path, out var content))
					{
						if(!this.GetSettings(node.Path, content).TryGetValue(text, out var setting))
							throw new InvalidOperationException();

						value = setting;
					}
					else
						value = new Node(GetPath(node.Path, text));
				}
				else
					throw new InvalidOperationException();
			}

			if(value is Node final)
			{
				if(provider.TryGet(final.Path, out var content))
					return content;

				throw new InvalidOperationException();
			}

			return value;
		}

		private Settings GetSettings(string path, string value)
		{
			if(!_settings.TryGetValue(path, out var settings))
				_settings.Add(path, settings = new Settings(value));

			return settings;
		}

		private static string GetPath(string current, string reference)
		{
			ValidatePath(reference);
			var key = ConfigurationUtility.GetConfigurationPath(reference);
			return reference.StartsWith('/') || string.IsNullOrEmpty(current) ? key : current + ":" + key;
		}

		private static (string Path, string Key) ParseField(string reference)
		{
			var index = reference.IndexOf('[');
			if(index < 0)
			{
				ValidatePath(reference);
				return (reference, null);
			}

			if(index == 0 || !reference.EndsWith(']') || reference.AsSpan(index + 1, reference.Length - index - 2).IndexOfAny('[', ']') >= 0)
				throw new FormatException();

			var path = reference[..index];
			var key = reference[(index + 1)..^1].Trim();
			ValidatePath(path);

			if(key.Length == 0)
				throw new FormatException();

			return (path, key);
		}

		private static void ValidatePath(string path)
		{
			if(string.IsNullOrWhiteSpace(path) ||
			   path.Contains('*') ||
			   path.Contains(':') ||
			   path.Contains('[') ||
			   path.Contains(']') ||
			   path.Contains('\\') ||
			   path.Any(char.IsControl) ||
			   path.Trim('/').Split('/').Any(part => part is "." or ".." or ""))
				throw new FormatException();
		}
		#endregion

		#region 嵌套类型
		private sealed record Node(string Path, bool Root = false);
		#endregion
	}
	#endregion
}
