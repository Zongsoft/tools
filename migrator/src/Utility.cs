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
using System.Collections.Generic;

namespace Zongsoft.Tools.Migrator;

internal static class Utility
{
	#region 公共方法
	/// <summary>将工具的绝对输入适配为 Core 本地搜索，结果保留逻辑名称。</summary>
	public static IEnumerable<Zongsoft.IO.Searcher.Match> Search(string path, bool files = false, string sourceDirectory = null)
	{
		path = Path.GetFullPath(path);
		var origin = sourceDirectory ?? (path.IndexOfAny(['*', '?']) < 0 ? Path.GetDirectoryName(path) : Path.GetPathRoot(path));
		var directory = new DirectoryInfo(origin ?? path);
		var pattern = Path.GetRelativePath(directory.FullName, path);

		return Zongsoft.IO.Searcher.Search(directory, pattern, files ? Zongsoft.IO.Searcher.Target.Files : Zongsoft.IO.Searcher.Target.Both);
	}

	/// <summary>判断指定的版本号是否为零。</summary>
	/// <param name="version">指定的版本。</param>
	/// <returns>如果版本号为零则返回真(<c>True</c>)，否则返回假(<c>False</c>)。</returns>
	public static bool IsZero(this Version version) => version == null ||
	(
		version.Major == 0 &&
		version.Minor == 0 &&
		version.Build <= 0 &&
		version.Revision <= 0
	);

	#endregion

	#region 嵌套类型
	public static class Unix
	{
		public const UnixFileMode Mode644 = UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.GroupRead | UnixFileMode.OtherRead;
		public const UnixFileMode Mode755 = Mode644 | UnixFileMode.UserExecute | UnixFileMode.GroupExecute | UnixFileMode.OtherExecute;

		public static long GetTimestamp(DateTime value) => new DateTimeOffset(value).ToUnixTimeSeconds();
	}
	#endregion
}
