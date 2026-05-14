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

public sealed class DebCommand : PackCommand<Package.Deb>
{
	protected override string Extension => Package.Deb.EXTENSION;
	protected override Package.Deb CreatePackage(CommandContext context, IDictionary<string, string> variables)
	{
		var package = new Package.Deb(
			context.Options.GetValue<string>(NAME_OPTION),
			context.Options.GetValue<string>(EDITION_OPTION),
			context.Options.GetValue<Version>(VERSION_OPTION),
			context.Options.GetValue<Platform>(PLATFORM_OPTION),
			context.Options.GetValue<Architecture>(ARCHITECTURE_OPTION))
		{
			Framework = context.Options.GetValue<string>(FRAMEWORK_OPTION),
			Title = Normalizer.NormalizeText(context.Options.GetValue<string>(TITLE_OPTION), variables),
			Summary = Normalizer.NormalizeText(context.Options.GetValue<string>(SUMMARY_OPTION), variables),
			Description = Normalizer.NormalizeText(context.Options.GetValue<string>(DESCRIPTION_OPTION), variables),
			Maintainer = Normalizer.NormalizeValue(context.Options.GetValue<string>(MAINTAINER_OPTION), variables, DEFAULT_MAINTAINER),
			License = Normalizer.NormalizeValue(context.Options.GetValue<string>(LICENSE_OPTION), variables),
			Url = Normalizer.NormalizeValue(context.Options.GetValue<string>(URL_OPTION), variables, DEFAULT_URL),
			Category = Normalizer.NormalizeValue(context.Options.GetValue<string>(CATEGORY_OPTION), variables),
			Dependencies = Normalizer.NormalizeList(context.Options.GetValue<string>(DEPENDENCIES_OPTION), variables),
		};

		package.InstallPath = $"{context.Options.GetValue(INSTALL_PATH_OPTION, DEFAULT_INSTALL_PATH)}/{package.PackageName}";

		return package;
	}
}
