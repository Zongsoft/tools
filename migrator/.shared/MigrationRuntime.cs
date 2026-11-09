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
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;

namespace Zongsoft.Tools.Migrator.Migration;

/// <summary>定义升迁计划支持的目标平台，并按目标平台验证路径和执行环境。</summary>
public static class MigrationRuntime
{
	#region 公共方法
	/// <summary>验证目标 RID；不支持的目标抛出 <see cref="InvalidDataException"/>。</summary>
	/// <param name="runtime">要验证的目标运行时标识符。</param>
	public static void Validate(string runtime)
	{
		if(runtime is not ("linux-x64" or "linux-arm64" or "win-x64"))
			throw new InvalidDataException(string.Format(MigrationResources.RuntimeUnsupported_Message, runtime));
	}

	/// <summary>确保计划目标与当前进程的操作系统和架构一致，不一致时拒绝执行。</summary>
	/// <param name="runtime">必须与当前进程操作系统及架构一致的目标运行时标识符。</param>
	public static void EnsureCurrent(string runtime)
	{
		Validate(runtime);
		var current = (OperatingSystem.IsWindows() ? "win" : OperatingSystem.IsLinux() ? "linux" : "osx") + "-" + RuntimeInformation.ProcessArchitecture.ToString().ToLowerInvariant();
		if(runtime != current)
			throw new InvalidDataException(string.Format(MigrationResources.RuntimeMismatch_Message, runtime, current));
	}

	/// <summary>判断路径是否为目标平台的完整数据库文件路径，不访问文件系统。</summary>
	/// <param name="path">要检查的数据库文件路径。</param>
	/// <param name="runtime">用于确定路径规则的目标运行时标识符。</param>
	/// <returns>路径符合目标平台绝对路径规则时为真，否则为假。</returns>
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
