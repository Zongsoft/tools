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

using Zongsoft.Terminals;
using Zongsoft.Components;

namespace Zongsoft.Tools.Packager;

internal class Dumper
{
	#region 公共方法
	public static void Splash()
	{
		const string SPLASH = @"
     _____                                ___ __
    /_   /  ____  ____  ____  ____ ____  / __/ /_
      / /  / __ \/ __ \/ __ \/ ___/ __ \/ /_/ __/
     / /__/ /_/ / / / / /_/ /\_ \/ /_/ / __/ /_
    /____/\____/_/ /_/\__  /____/\____/_/  \__/
                     /____/
";

		Terminal.WriteLine(SPLASH);
	}

	public static void HostLocateFailed(CommandOutletColor color = CommandOutletColor.DarkRed) => Terminal.WriteLine(color, Properties.Resources.HostLocateFailed_Message);
	public static void PathNotExist(string path) => PathNotExist(CommandOutletColor.DarkYellow, path);
	public static void PathNotExist(CommandOutletColor color, string path) => Terminal.WriteLine(Utility.FormatOutput(Properties.Resources.PathNotExist_Message, color, path));
	public static void DirectoryNotExist(CommandOutletColor color, string path) => Terminal.WriteLine(Utility.FormatOutput(Properties.Resources.DirectoryNotExist_Message, color, path));
	public static void PackageEntryConflicted(string path, string entry) => PackageEntryConflicted(CommandOutletColor.DarkYellow, path, entry);
	public static void PackageEntryConflicted(CommandOutletColor color, string path, string entry) => Terminal.WriteLine(Utility.FormatOutput(Properties.Resources.PackageEntryConflicted_Message, color, path, entry));
	#endregion
}
