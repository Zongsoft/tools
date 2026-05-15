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
using System.Collections.Generic;
using System.Runtime.InteropServices;

using Zongsoft.Components;

namespace Zongsoft.Tools.Packager;

[CommandOption(PROVIDES_OPTION, typeof(string))]
[CommandOption(CONFLICTS_OPTION, typeof(string))]
public sealed class RpmCommand : PackCommand<Package.Rpm>
{
	private const string PROVIDES_OPTION = "provides";
	private const string CONFLICTS_OPTION = "conflicts";

	protected override Package.Rpm CreatePackage(CommandContext context)
	{
		var package = new Package.Rpm(
			context.Options.GetValue<string>(NAME_OPTION),
			context.Options.GetValue<string>(EDITION_OPTION),
			context.Options.GetValue<Version>(VERSION_OPTION),
			context.Options.GetValue<Platform>(PLATFORM_OPTION),
			context.Options.GetValue<Architecture>(ARCHITECTURE_OPTION))
		{
			Provides = Normalizer.NormalizeList(context.Options.GetValue<string>(PROVIDES_OPTION)),
			Conflicts = Normalizer.NormalizeList(context.Options.GetValue<string>(CONFLICTS_OPTION)),
		};

		Configure(package, context);
		return package;
	}
}
