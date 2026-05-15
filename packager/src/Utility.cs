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
using System.Runtime.InteropServices;

namespace Zongsoft.Tools.Packager;

internal static class Utility
{
	/// <summary>判断指定的版本号是否为零。</summary>
	/// <param name="version">指定的版本。</param>
	/// <returns>如果版本号为零则返回真(<c>True</c>)，否则返回假(<c>False</c>)。</returns>
	public static bool IsZero(this Version version) => version == null ||
	(
		version.Major == 0 &&
		version.Minor == 0 &&
		version.Build == 0 &&
		version.Revision == 0
	);

	public static string GetRuntimeIdentifier(Platform platform, Architecture? architecture) => platform == Platform.Windows ?
		(!architecture.HasValue ? "win" : $"win-{architecture.ToString().ToLowerInvariant()}") :
		(!architecture.HasValue ? platform.ToString().ToLowerInvariant() : $"{platform.ToString().ToLowerInvariant()}-{architecture.ToString().ToLowerInvariant()}");

	public static bool IsExternal(string source, string path)
	{
		return Path.IsPathFullyQualified(path) &&
			!Path.GetFullPath(path).StartsWith(source, GetComparison());

		static StringComparison GetComparison() => OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
	}

	public static string NormalizePath(string value)
	{
		if(string.IsNullOrWhiteSpace(value))
			return string.Empty;

		return value
			.Replace(Path.DirectorySeparatorChar, '/')
			.Replace(Path.AltDirectorySeparatorChar, '/')
			.TrimStart('/');
	}

	public static class Unix
	{
		public static string GetInstallPath(string name)
		{
			if(name.IsWhiteSpace())
				return "/opt";

			var index = name.IndexOf('.');
			if(index > 0)
				return $"/opt/{name[..index].ToLowerInvariant()}";
			else
				return $"/opt/{name.ToLowerInvariant()}";
		}

		public static int GetFileMode(string path)
		{
			if(!OperatingSystem.IsWindows())
			{
				var mode = (int)(File.GetUnixFileMode(path) & (UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute | UnixFileMode.GroupRead | UnixFileMode.GroupWrite | UnixFileMode.GroupExecute | UnixFileMode.OtherRead | UnixFileMode.OtherWrite | UnixFileMode.OtherExecute));

				if(mode > 0)
					return mode;
			}

			return IsExecutable(path) ? 0755 : 0644;

			static bool IsExecutable(string path)
			{
				var extension = Path.GetExtension(path);

				return string.IsNullOrEmpty(extension) ||
					extension.Equals(".sh", StringComparison.OrdinalIgnoreCase) ||
					extension.Equals(".dll", StringComparison.OrdinalIgnoreCase) ||
					extension.Equals(".exe", StringComparison.OrdinalIgnoreCase);
			}
		}

		public static long GetTimestamp(DateTime value) => new DateTimeOffset(value).ToUnixTimeSeconds();
	}
}
