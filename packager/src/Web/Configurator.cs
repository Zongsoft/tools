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

namespace Zongsoft.Tools.Packager.Web;

internal static partial class Configurator
{
	internal static IConfigurator Get(string name) => name?.ToLowerInvariant() switch
	{
		"nginx" => new Nginx(),
		_ => throw DefinitionException.Create("Hoster", default, name),
	};

	internal static Options Parse(string value, string source, Variables variables)
	{
		var expanded = VariableEvaluator.Evaluate(value, variables.Raw, allowEscapes: true);
		if(!expanded.Succeed)
			throw DefinitionException.Create("Variable", new(source, Entry: "--web"), expanded.Variable);

		value = expanded.Value.Trim();
		if(value.Length == 0)
			return default;

		var colon = value.IndexOf(':');
		var hoster = (colon < 0 ? value : value[..colon]).Trim().ToLowerInvariant();
		var path = colon < 0 ? string.Empty : value[(colon + 1)..].Trim();

		if(hoster is "" or "none")
		{
			if(path.Length > 0 || hoster.Length == 0)
				throw DefinitionException.Create("Option", new(source, Entry: "--web"), value);

			return default;
		}

		if(hoster != "nginx")
			throw DefinitionException.Create("Hoster", new(source, Entry: "--web"), hoster);

		path = path.Length == 0 ? "web.profile" : path;
		if(path.IndexOfAny(['*', '?', '|', '\r', '\n']) >= 0)
			throw DefinitionException.Create("Option", new(source, Entry: "--web"), path);

		path = Path.GetFullPath(Path.Combine(source, path));
		if(Directory.Exists(path) || !File.Exists(path))
			throw DefinitionException.Create("Option", new(source, Entry: "--web"), path);

		return new(hoster, path);
	}

	internal readonly record struct Options(string Hoster, string FilePath)
	{
		internal bool Enabled => this.Hoster != null;
	}
}
