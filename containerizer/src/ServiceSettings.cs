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
using System.Data.Common;
using System.Collections.Generic;
using System.Text.RegularExpressions;

using Zongsoft.Configuration;
using Zongsoft.Tools.Containerizer.Protocol;

namespace Zongsoft.Tools.Containerizer;

internal static partial class ServiceSettings
{
	public static Dictionary<string, string> Parse(string text)
	{
		var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

		text = text?.Trim() ?? "";
		if(text.Contains('\r') || text.Contains('\n'))
			throw new ContainerizationException(2, Properties.Resources.Settings_Invalid_Message);

		var position = 0;

		foreach(Match match in GetEntryRegex().Matches(text))
		{
			if(match.Index != position)
				throw new ContainerizationException(2, Properties.Resources.Settings_Invalid_Message);

			position += match.Length;
			var entry = match.Groups["entry"].Value.Trim();

			if(entry.Length == 0)
				continue;

			var separator = entry.IndexOf('=');
			if(separator < 1)
				throw new ContainerizationException(2, Properties.Resources.Settings_Invalid_Message);

			var name = entry[..separator].Trim();
			ContainerManifest.ValidateIdentity(name);
			var value = entry[(separator + 1)..].Trim();

			if(result.ContainsKey(name))
				throw new ContainerizationException(2, Properties.Resources.Settings_Invalid_Message);

			if(value.StartsWith('"') || value.StartsWith('\''))
			{
				var builder = new DbConnectionStringBuilder { ConnectionString = entry };
				result[name] = (string)builder[name];
			}
			else
				result[name] = new ConnectionSettings(entry)[name] ?? "";
		}

		if(position != text.Length)
			throw new ContainerizationException(2, Properties.Resources.Settings_Invalid_Message);

		return result;
	}

	public static string Format(IEnumerable<KeyValuePair<string, string>> values) => string.Join(';', values.Select(pair => $"{pair.Key}={Quote(pair.Value ?? "")}"));
	private static string Quote(string value)
	{
		if(value.Contains('\r') || value.Contains('\n'))
			throw new ContainerizationException(2, Properties.Resources.Settings_Invalid_Message);

		return value.IndexOfAny([';', '\"', '\'']) < 0 && value.Trim().Length == value.Length ?
			value : $"\"{value.Replace("\"", "\"\"", StringComparison.Ordinal)}\"";
	}

	[GeneratedRegex("(?<entry>[^;=\\r\\n]+=[ \\t]*(?:\"(?:[^\"]|\"\")*\"|'(?:[^']|'')*'|[^;\\r\\n]*))[ \\t]*(?:;|$)|[ \\t]*;", RegexOptions.CultureInvariant)]
	private static partial Regex GetEntryRegex();
}
