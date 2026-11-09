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

using Zongsoft.Components;

namespace Zongsoft.Tools.Packager;

[CommandOption("provides", typeof(string))]
[CommandOption("replaces", typeof(string))]
[CommandOption("breaks", typeof(string))]
[CommandOption("conflicts", typeof(string))]
[CommandOption("recommends", typeof(string))]
[CommandOption("suggests", typeof(string))]
public sealed class DebCommand : PackCommand<Package.Deb>
{
	#region 重写方法
	protected override Package.Deb CreatePackage(CommandContext context, PackageOptions options)
	{
		var package = new Package.Deb(
			options.Name,
			options.Edition,
			options.Version,
			options.Platform,
			options.Architecture,
			options)
		{
			Provides = ReadRelationship("provides", options),
			Replaces = ReadRelationship("replaces", options),
			Breaks = ReadRelationship("breaks", options),
			Conflicts = ReadRelationship("conflicts", options),
			Recommends = ReadRelationship("recommends", options),
			Suggests = ReadRelationship("suggests", options),
		};

		Configure(package, context);
		return package;
	}

	#endregion

	#region 私有方法
	private static string[] ReadRelationship(string name, PackageOptions options)
	{
		var value = options[name];
		return string.IsNullOrWhiteSpace(value) ? [] : value.Split([';', ','], StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
	}
	#endregion
}
