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
using System.IO;

namespace Zongsoft.Tools.Packager.Web;

internal static partial class Configurator
{
	internal static IConfigurator Get(string name) => name?.ToLowerInvariant() switch
	{
		"nginx" => new Nginx(),
		_ => throw DefinitionException.Create("Hoster", default, name),
	};

	internal static Options Parse(string value, string source, PackageOptions options)
	{
		if(!options.Evaluator.TryEvaluate(value, out var text, out var error))
			throw DefinitionException.Create("Variable", new(source, Entry: "--web"), error.Expression, error);

		value = text.Trim();
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
