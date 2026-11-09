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

namespace Zongsoft.Tools.Packager.Web;

internal readonly record struct Diagnostic(string Code, string Message, Diagnostic.Location Source, Diagnostic.Severity Level = Diagnostic.Severity.Error, Diagnostic.Location? Related = null)
{
	internal enum Severity { Warning, Error, }
	internal readonly record struct Location(string File, string Section = null, string Entry = null, int Line = 0)
	{
		public override string ToString() => $"{this.File}:{this.Line} [{this.Section}] {this.Entry}";
	}

	public override string ToString() => $"{this.Source}: {this.Code}: {this.Message}" + (this.Related is { } related ? $" ({related})" : string.Empty);
}
