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
 * Copyright (C) 2015-2026 Zongsoft Corporation <http://www.zongsoft.com>
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

namespace Zongsoft.Tools.Deployer;

/// <summary>验证部署路径的访问边界，并解析源文件及祖先目录链接以获得文件身份。</summary>
/// <remarks>写入路径拒绝链接；源路径的逻辑范围检查与实际读取目标的身份解析分别处理。</remarks>
internal static class DeploymentPath
{
	#region 静态字段
	internal static readonly StringComparer Comparer = OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
	#endregion

	#region 路径验证
	public static string Validate(string root, string path)
	{
		path = ValidateSource(root, path);

		// Refuse links in write paths, including ancestors of the configured root.
		// This also handles dangling links; resolve-and-check alone would miss them.
		for(var current = path; !string.IsNullOrEmpty(current); current = Path.GetDirectoryName(current))
		{
			try
			{
				if((File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
					throw new IOException(string.Format(Properties.Resources.Review_LinkedPath, current));
			}
			catch(FileNotFoundException) { }
			catch(DirectoryNotFoundException) { }
		}

		return path;
	}

	/// <summary>验证显式源的逻辑范围；源链接允许读取目标，不能套用写入路径的链接禁令。</summary>
	public static string ValidateSource(string root, string path)
	{
		root = Path.GetFullPath(root);
		path = Path.GetFullPath(path);

		var relative = Path.GetRelativePath(root, path);

		if(Path.IsPathRooted(relative) || relative == ".." || relative.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal))
			throw new IOException(string.Format(Properties.Resources.Review_OutsideRoot, path, root));

		return path;
	}
	#endregion

	#region 路径标识
	/// <summary>仅将普通路径不存在视为可选缺失，链接失效和访问错误仍交由调用方处理。</summary>
	public static bool IsMissing(string path)
	{
		path = Path.GetFullPath(path);

		for(var current = path; !string.IsNullOrEmpty(current); current = Path.GetDirectoryName(current))
		{
			FileSystemInfo info = Directory.Exists(current) ? new DirectoryInfo(current) : new FileInfo(current);
			if(info.LinkTarget != null && info.ResolveLinkTarget(true) is not { Exists: true })
				throw new IOException(string.Format(Properties.Resources.Review_LinkedPath, current));
		}

		try
		{
			File.GetAttributes(path);
			return false;
		}
		catch(FileNotFoundException) { return true; }
		catch(DirectoryNotFoundException) { return true; }
	}

	public static string Identity(string path)
	{
		path = Path.GetFullPath(path);

		var root = Path.GetPathRoot(path);
		var current = root;

		foreach(var part in path[root.Length..].Split(Path.DirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries))
		{
			current = Path.Combine(current, part);

			FileSystemInfo info = Directory.Exists(current) ? new DirectoryInfo(current) : new FileInfo(current);

			if(info.LinkTarget != null)
				current = info.ResolveLinkTarget(true)?.FullName ?? throw new IOException(string.Format(Properties.Resources.Review_LinkedPath, current));
		}

		return current;
	}
	#endregion
}
