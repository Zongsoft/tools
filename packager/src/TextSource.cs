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
using System.IO;

namespace Zongsoft.Tools.Packager;

internal static class TextSource
{
	#region 公共方法
	public static string Read(string source, string value, bool fileOnly = false)
	{
		if(string.IsNullOrWhiteSpace(value))
			return null;

		if(value.StartsWith("text:", StringComparison.OrdinalIgnoreCase))
		{
			if(fileOnly)
				throw new InvalidDataException(Properties.Resources.TextSourceFileRequired);

			return value[5..];
		}

		var explicitFile = value.StartsWith("file:", StringComparison.OrdinalIgnoreCase);
		if(explicitFile)
			value = value[5..];

		var result = Normalizer.Normalize(value, Normalizer.Variables);
		if(!result.Succeed)
			throw new InvalidOperationException(string.Format(Properties.Resources.VariableResolutionFailed, result.Value));

		value = result.Value;
		if(!explicitFile && !fileOnly && (value.Contains('\r') || value.Contains('\n')))
			return value;

		var path = Path.GetFullPath(Path.Combine(source ?? Environment.CurrentDirectory, value));
		if(File.Exists(path))
			return File.ReadAllText(path);

		if(explicitFile || fileOnly || IsPath(value))
			throw new FileNotFoundException(string.Format(Properties.Resources.TextSourceMissing, path), path);

		return value;
	}
	#endregion

	#region 私有方法
	private static bool IsPath(string value) =>
		Path.IsPathFullyQualified(value) || value.StartsWith("./") || value.StartsWith("../") ||
		value.StartsWith(@".\") || value.StartsWith(@"..\") ||
		(!value.Contains(' ') && (value.Contains('/') || value.Contains('\\') || value.EndsWith(".sh", StringComparison.OrdinalIgnoreCase)));
	#endregion
}
