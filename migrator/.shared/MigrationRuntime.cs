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
using System.Text.RegularExpressions;

namespace Zongsoft.Tools.Migrator.Migration;

/// <summary>定义升迁计划支持的目标平台，并按目标平台验证路径和执行环境。</summary>
public static class MigrationRuntime
{
	#region 公共方法
	/// <summary>验证目标 RID；不支持的目标抛出 <see cref="InvalidDataException"/>。</summary>
	public static void Validate(string runtime)
	{
		if(runtime is not ("linux-x64" or "linux-arm64" or "win-x64"))
			throw new InvalidDataException(string.Format(MigrationResources.RuntimeUnsupported_Message, runtime));
	}

	/// <summary>确保计划目标与当前进程的操作系统和架构一致，不一致时拒绝执行。</summary>
	public static void EnsureCurrent(string runtime)
	{
		Validate(runtime);
		var current = (OperatingSystem.IsWindows() ? "win" : OperatingSystem.IsLinux() ? "linux" : "osx") + "-" + RuntimeInformation.ProcessArchitecture.ToString().ToLowerInvariant();
		if(runtime != current)
			throw new InvalidDataException(string.Format(MigrationResources.RuntimeMismatch_Message, runtime, current));
	}

	/// <summary>判断路径是否为目标平台的完整数据库文件路径，不访问文件系统。</summary>
	public static bool IsDatabasePath(string path, string runtime)
	{
		if(string.IsNullOrWhiteSpace(path) || path.Contains('\0'))
			return false;

		if(runtime is "linux-x64" or "linux-arm64")
			return path.StartsWith('/');

		return runtime == "win-x64" && !path.StartsWith(@"\\?\") && !path.StartsWith(@"\\.\") &&
			(Regex.IsMatch(path, @"^[A-Za-z]:[\\/]") || Regex.IsMatch(path, @"^\\\\[^\\/]+[\\/][^\\/]+[\\/]"));
	}
	#endregion
}
