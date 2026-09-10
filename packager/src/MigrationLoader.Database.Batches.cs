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
using System.IO;
using System.Linq;
using System.Text;
using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace Zongsoft.Tools.Packager.Migration;

partial class MigrationLoader
{
	private sealed partial class Database
	{
		#region 批次处理
		private static IReadOnlyList<string> Read(string sql, string provider)
		{
			// These drivers handle SQL grammar and statement boundaries themselves.
			if(provider is "postgres" or "duckdb" or "sqlite")
				return string.IsNullOrWhiteSpace(sql) ? [] : [sql];
			if(provider is not ("mysql" or "mssql" or "tdengine"))
				throw new InvalidDataException(Properties.Resources.MigrationProviderUnknown);

			var batches = new List<string>();
			var buffer = new StringBuilder();
			var delimiter = ";";
			var quote = '\0';
			var escapeQuote = false;
			var hasContent = false;
			var block = 0;
			var lineStart = true;
			var lineComment = false;

			for(var i = 0; i < sql.Length;)
			{
				if(lineStart && quote == '\0' && block == 0 && !lineComment)
				{
					var end = sql.IndexOf('\n', i);
					if(end < 0)
						end = sql.Length;

					var line = sql[i..end].Trim();
					if(provider == "mssql" && Regex.IsMatch(line, @"^GO(?:\s*--.*)?$", RegexOptions.IgnoreCase))
					{
						Flush();
						i = Math.Min(end + 1, sql.Length);
						continue;
					}

					if(provider == "mysql" && Regex.IsMatch(line, @"^DELIMITER(?:\s|$)", RegexOptions.IgnoreCase))
					{
						delimiter = line[9..].Trim();
						if(delimiter.Length == 0 || delimiter.Any(char.IsWhiteSpace))
							throw new InvalidDataException(Properties.Resources.MySqlDelimiterInvalid);

						buffer.Append(sql.AsSpan(i, end - i).EndsWith("\r") ? "\r\n" : "\n");
						i = Math.Min(end + 1, sql.Length); continue;
					}

					if(provider == "mssql" && Regex.IsMatch(line, @"^GO\s+\d+\b", RegexOptions.IgnoreCase))
						throw new InvalidDataException(Properties.Resources.SqlServerGoRepeatUnsupported);
				}

				var c = sql[i];
				var next = i + 1 < sql.Length ? sql[i + 1] : '\0';
				lineStart = c == '\n';

				if(lineComment)
				{
					buffer.Append(c); i++;

					if(c == '\n')
						lineComment = false;

					continue;
				}

				if(block > 0)
				{
					buffer.Append(c); i++;

					if(c == '/' && next == '*' && provider != "mysql")
					{
						block++;
						buffer.Append(next);
						i++;
					}
					else if(c == '*' && next == '/')
					{
						block--;
						buffer.Append(next);
						i++;
					}

					continue;
				}

				if(quote != '\0')
				{
					buffer.Append(c);
					i++;

					if(c == '\\' && escapeQuote && next != '\0')
					{
						buffer.Append(next);
						i++;
					}
					else if(c == quote)
					{
						if(next == quote)
						{
							buffer.Append(next);
							i++;
						}
						else
							quote = '\0';
					}

					continue;
				}

				if(provider == "mysql" && delimiter != ";" && sql.AsSpan(i).StartsWith(delimiter))
				{
					buffer.Append(';');
					i += delimiter.Length;
					continue;
				}

				if(provider == "tdengine" && c == ';')
				{
					Flush();
					i++;
					continue;
				}

				if(c == '-' && next == '-' && (provider != "mysql" || i + 2 == sql.Length || char.IsWhiteSpace(sql[i + 2])) || c == '#' && provider == "mysql")
					lineComment = true;
				else if(c == '/' && next == '*')
				{
					block = 1;
					hasContent |= provider == "mysql" && i + 2 < sql.Length && sql[i + 2] == '!';
					buffer.Append("/*");
					i += 2;
					continue;
				}
				else if(c is '\'' or '"' or '`')
				{
					quote = c;
					escapeQuote = provider is "mysql" or "tdengine";
				}
				else if(c == '[' && provider == "mssql")
					quote = ']';
				if(!lineComment && !char.IsWhiteSpace(c))
					hasContent = true;

				buffer.Append(c); i++;
			}

			// SQL syntax errors belong to the database, not a packaging-time lexer.
			if(provider == "mysql")
				return string.IsNullOrWhiteSpace(buffer.ToString()) ? [] : [buffer.ToString()];

			Flush();
			return batches;

			void Flush()
			{
				var text = buffer.ToString().Trim();
				buffer.Clear();

				if(text.Length != 0 && hasContent)
					batches.Add(text);

				hasContent = false;
			}
		}
		#endregion
	}
}
