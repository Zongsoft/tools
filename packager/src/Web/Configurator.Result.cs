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
using System.Linq;
using System.Text;
using System.Collections.Generic;

namespace Zongsoft.Tools.Packager.Web;

partial class Configurator
{
	internal sealed record Result(string Hoster, IReadOnlyList<Result.File> Files, IReadOnlyList<Diagnostic> Diagnostics)
	{
		internal BindingDocument Bindings { get; init; } = new();

		internal sealed record File(string Path, Content Content, UnixFileMode Mode);
		internal sealed class Content(IReadOnlyList<ContentPart> parts)
		{
			internal IReadOnlyList<ContentPart> Parts { get; } = parts.ToArray();
			internal bool Relocatable => this.Parts.Any(part => part.InstallRoot);

			internal string Render(string installPath)
			{
				var text = new StringBuilder();

				foreach(var part in this.Parts)
					text.Append(part.InstallRoot ? Nginx.Writer.Escape(installPath.TrimEnd('/')) : part.Text);

				return text.ToString();
			}
		}

		internal readonly record struct ContentPart(string Text, bool InstallRoot = false);
	}
}
