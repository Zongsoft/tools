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
using System.Linq;
using System.Collections.Generic;
using System.Text.RegularExpressions;

using Zongsoft.Terminals;
using Zongsoft.Components;

using Zongsoft.Tools.Containerizer.Protocol;

namespace Zongsoft.Tools.Containerizer;

internal static partial class MigrationInput
{
	#region 公共方法
	public static Entry Parse(string path)
	{
		var match = MigrationArchiveRegex().Match(Path.GetFileName(path));

		if(!match.Success)
			throw new ContainerizationException(2, string.Format(Properties.Resources.MigrationInput_1_Message, Path.GetFileName(path)));

		var version = match.Groups["version"].Value;
		return new(ContainerManifest.GetVersionNumber(version), version, match.Groups["rid"].Value, $"{path[..^7]}.sh");
	}

	public static void Validate(IReadOnlyList<string> paths, string architecture)
	{
		var versions = new HashSet<Versioning.Version.Number>();
		Versioning.Version.Number? previous = null;

		foreach(var path in paths)
		{
			var info = Parse(path);

			if(info.Runtime != $"linux-{architecture}" || !versions.Add(info.Version) || previous != null && info.Version < previous.Value)
				throw new ContainerizationException(2, Properties.Resources.MigrationInput_2_Message);

			if(!File.Exists(path) || !File.Exists(info.Script))
				throw new ContainerizationException(2, string.Format(Properties.Resources.MigrationInput_3_Message, path));

			previous = info.Version;
		}
	}

	public static IEnumerable<string> Select(string directory, string name, string architecture)
	{
		if(!Directory.Exists(directory))
			throw new ContainerizationException(2, string.Format(Properties.Resources.MigrationInput_4_Message, directory));

		var candidates = Directory.EnumerateFiles(directory, "*.tar.gz").Where(path => Path.GetFileName(path).StartsWith(name, StringComparison.OrdinalIgnoreCase) && Path.GetFileName(path).EndsWith($"_linux-{architecture}.tar.gz", StringComparison.Ordinal)).OrderBy(path => Parse(path).Version).ToArray();

		foreach(var candidate in candidates)
		{
			if(!File.Exists(Parse(candidate).Script))
				throw new ContainerizationException(2, string.Format(Properties.Resources.MigrationInput_5_Message, Parse(candidate).Script));
		}

		if(candidates.Length == 0)
			Terminal.WriteLine(CommandOutletColor.DarkYellow, Properties.Resources.MigrationInput_NoMigrations);

		return ComponentSelector.Choose(candidates, true);
	}
	#endregion

	#region 私有方法
	[GeneratedRegex(@"^.+\(migrate\)@(?<version>\d+(?:\.\d+){1,3})_(?<rid>linux-(?:x64|arm64))\.tar\.gz$", RegexOptions.CultureInvariant)]
	private static partial Regex MigrationArchiveRegex();
	#endregion

	#region 嵌套类型
	internal readonly record struct Entry(Versioning.Version.Number Version, string VersionText, string Runtime, string Script);
	#endregion
}
