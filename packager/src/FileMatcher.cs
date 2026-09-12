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
using System.IO.Enumeration;
using System.Linq;
using System.Collections.Generic;

namespace Zongsoft.Tools.Packager;

internal static class FileMatcher
{
	#region 公共方法
	public static string GetBaseDirectory(string pattern)
	{
		var path = Path.GetFullPath(pattern);
		var index = path.IndexOfAny(['*', '?']);
		if(index < 0)
			return Path.GetDirectoryName(path);

		var separator = path.LastIndexOfAny([Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar], index);
		return path[..(separator + 1)];
	}

	public static string[] GetFiles(string pattern) => GetEntries(pattern).Where(File.Exists).ToArray();

	public static string[] GetEntries(string pattern)
	{
		var path = Path.GetFullPath(pattern);
		if(path.IndexOfAny(['*', '?']) < 0)
		{
			if(!File.Exists(path) && !Directory.Exists(path))
				return [];

			CheckLink(path);
			return [path];
		}

		var root = GetBaseDirectory(path);
		var parts = Path.GetRelativePath(root, path).Replace('\\', '/').Split('/');
		var matches = new HashSet<string>(OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);
		var visited = new HashSet<string>(OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);
		Visit(root, 0);
		return matches.OrderBy(item => Path.GetRelativePath(root, item).Replace('\\', '/'), StringComparer.Ordinal).ToArray();

		void Visit(string directory, int index)
		{
			if(!Directory.Exists(directory) || !visited.Add(directory + "|" + index))
				return;

			CheckLink(directory);
			if(index == parts.Length)
			{
				matches.Add(Path.TrimEndingDirectorySeparator(directory));
				return;
			}

			if(parts[index] == "**")
			{
				Visit(directory, index + 1);
				foreach(var entry in Directory.EnumerateFileSystemEntries(directory))
				{
					CheckLink(entry);
					if(Directory.Exists(entry))
						Visit(entry, index);
					else if(index == parts.Length - 1)
						matches.Add(entry);
				}
				return;
			}

			foreach(var entry in Directory.EnumerateFileSystemEntries(directory))
			{
				if(!FileSystemName.MatchesSimpleExpression(parts[index], Path.GetFileName(entry), OperatingSystem.IsWindows()))
					continue;

				CheckLink(entry);
				if(index == parts.Length - 1)
					matches.Add(entry);
				else if(Directory.Exists(entry))
					Visit(entry, index + 1);
			}
		}
	}

	public static void CheckLink(string path)
	{
		if((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
			throw new InvalidDataException(string.Format(Properties.Resources.PackageLinkUnsupported, path));
	}
	#endregion
}
