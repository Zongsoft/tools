/*
 *   _____                                ______
 *  /_   /  ____  ____  ____  _________  / __/ /_
 *    / /  / __ \/ __ \/ __ \/ ___/ __ \/ /_/ __/
 *   / /__/ /_/ / / / / /_/ /\_ \/ /_/ / __/ /_
 *  /____/\____/_/ /_/\__  /____/\____/_/  \__/
 *                   /____/
 *
 * Authors:
 *   钟峰(Popeye Zhong) <zongsoft@qq.com>
 *
 * Copyright (c) Zongsoft Studio. All rights reserved.
 * The above copyright notice and this permission notice shall be included in all
 * AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
 *
 *
 * Licensed under the MIT License (see LICENSE).
 */

using System;
using System.Text;
using System.Collections.Generic;

namespace Zongsoft.Tools.Packager.Web;

partial class Configurator
{
	partial class Nginx
	{
		internal sealed class Writer(bool template)
		{
			private readonly StringBuilder _text = new();
			private readonly List<Result.ContentPart> _parts = [];

			private void Write(Directive directive, int depth)
			{
				_text.Append('\t', depth).Append(directive.Name);

				foreach(var argument in directive.Arguments)
				{
					_text.Append(' ');

					if(argument.Value?.Contains("{{zongsoft:", StringComparison.Ordinal) == true)
						throw DefinitionException.Create("Conflict", default, "{{zongsoft:");

					if(template && argument.Kind == ArgumentKind.Binding)
					{
						var scheme = argument.Value.IndexOf("://", StringComparison.Ordinal);
						_text.Append(scheme < 0 ? "" : argument.Value[..(scheme + 3)]).Append("{{zongsoft:binding:").Append(argument.Binding).Append(':').Append(argument.Endpoint).Append("}}");
					}
					else if(template && argument.Kind == ArgumentKind.Resource)
					{
						var path = (argument.Relative ? "./" : "") + argument.Value;
						_text.Append('"').Append("{{zongsoft:file:").Append(Convert.ToBase64String(Encoding.UTF8.GetBytes(path))).Append("}}\"");
					}
					else if(argument.Kind is ArgumentKind.Raw or ArgumentKind.Expression)
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

			internal static Result.Content Generate(IReadOnlyList<Directive> directives, bool template = false) => new Writer(template).Render(directives);
		}
	}
}
