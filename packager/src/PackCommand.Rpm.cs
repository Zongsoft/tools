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

[CommandOption(PROVIDES_OPTION, typeof(string))]
[CommandOption(CONFLICTS_OPTION, typeof(string))]
public sealed class RpmCommand : PackCommand<Package.Rpm>
{
	private const string PROVIDES_OPTION = "provides";
	private const string CONFLICTS_OPTION = "conflicts";

	protected override Package.Rpm CreatePackage(CommandContext context, PackageOptions options)
	{
		var package = new Package.Rpm(
			options.Name,
			options.Edition,
			options.Version,
			options.Platform,
			options.Architecture,
			options)
		{
			Provides = options[PROVIDES_OPTION]?.Split([',', ';'], StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries),
			Conflicts = options[CONFLICTS_OPTION]?.Split([',', ';'], StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries),
		};

		Configure(package, context);
		return package;
	}
}
