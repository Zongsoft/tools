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

public sealed class TarCommand : PackCommand<Package.Tar>
{
	protected override Package.Tar CreatePackage(CommandContext context, PackageOptions options)
	{
		var package = new Package.Tar(
			options.Name,
			options.Edition,
			options.Version,
			options.Platform,
			options.Architecture,
			options);

		Configure(package, context);
		return package;
	}
}
