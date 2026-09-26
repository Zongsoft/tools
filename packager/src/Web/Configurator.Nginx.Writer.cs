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
 * Copyright (C) 2020-2026 Zongsoft Corporation <http://www.zongsoft.com>
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
using System.Text;
using System.Collections.Generic;

namespace Zongsoft.Tools.Packager.Web;

partial class Configurator
{
	partial class Nginx
	{
		internal sealed class Writer
		{
			private readonly StringBuilder _text = new();
			private readonly List<Result.ContentPart> _parts = [];

			private void Write(Directive directive, int depth)
			{
				_text.Append('\t', depth).Append(directive.Name);

				foreach(var argument in directive.Arguments)
				{
					_text.Append(' ');

					if(argument.Kind is ArgumentKind.Raw or ArgumentKind.Expression)
						_text.Append(argument.Value);
					else if(argument.Kind == ArgumentKind.Pattern)
						_text.Append('"').Append(argument.Value.Replace("\\", "\\\\").Replace("\"", "\\\"")).Append('"');
					else if(argument.Relative)
					{
						_text.Append('"');
						this.Flush();
						_parts.Add(new(null, true));
						_text.Append('/').Append(Escape(argument.Value)).Append('"');
					}
					else
					{
						var value = argument.Value ?? string.Empty;
						if(value.Length > 0 && value.IndexOfAny(['$', '"', '\'', '\\', ' ', '\t', '\r', '\n', '\0', '{', '}', ';', '#']) < 0)
							_text.Append(value);
						else
							_text.Append('"').Append(Escape(value)).Append('"');
					}
				}

				if(directive.Children == null)
				{
					_text.Append(";\r\n");
					return;
				}

				_text.Append(" {\r\n");

				foreach(var child in directive.Children)
					this.Write(child, depth + 1);

				_text.Append('\t', depth).Append("}\r\n");
			}

			private void Flush()
			{
				if(_text.Length > 0)
				{
					_parts.Add(new(_text.ToString()));
					_text.Clear();
				}
			}

			private Result.Content Render(IReadOnlyList<Directive> directives)
			{
				foreach(var directive in directives)
				{
					this.Write(directive, 0);
					_text.Append("\r\n");
				}

				this.Flush();
				return new(_parts.AsReadOnly());
			}

			internal static string Escape(string value)
			{
				if(value.Contains('$') || value.IndexOfAny(['\r', '\n', '\0']) >= 0)
					throw DefinitionException.Create("Capability", default, value);

				return value.Replace("\\", "\\\\").Replace("\"", "\\\"");
			}

			private Writer() { }
			internal static Result.Content Generate(IReadOnlyList<Directive> directives) => new Writer().Render(directives);
		}
	}
}
