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

namespace Zongsoft.Tools.Packager;

public static partial class Generator
{
	#region 私有方法
	private static string GetSummary(Package package) =>
		!string.IsNullOrWhiteSpace(package.Summary) ? package.Summary :
		!string.IsNullOrWhiteSpace(package.Title) ? package.Title : package.Name;

	private static string GetDescription(Package package) => !string.IsNullOrWhiteSpace(package.Description) ? package.Description : GetSummary(package);
	private static string GetIdentity()
	{
		var name = typeof(Generator).Assembly.GetName();
		return $"{name.Name}@{name.Version}";
	}
	#endregion
}
