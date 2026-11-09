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

namespace Zongsoft.Tools.Packager;

internal static class TextSource
{
	#region 公共方法
	public static string Read(string source, string value, PackageOptions options, bool fileOnly = false)
	{
		ArgumentNullException.ThrowIfNull(options);
		return Utility.ReadTextSource(source, value, text => options.Evaluator.Evaluate(text), fileOnly, Properties.Resources.TextSourceFileRequired_Message, Properties.Resources.TextSourceMissing_Message);
	}
	#endregion
}
