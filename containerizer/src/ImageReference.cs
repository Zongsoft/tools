/*
 *   _____                                ______
 *  /_   /  ____  ____  ____  _________  / __/ /_
 *    / /  / __ \/ __ \/ __ \/ ___/ __ \/ /_/ __/
 *   / /__/ /_/ / / / /_/ /\_ \/ /_/ / __/ /_
 *  /____/\____/_/ /_/\__  /____/\____/_/  \__/
 *                   /____/
 *
 * Authors:
 *   钟峰(Popeye Zhong) <zongsoft@gmail.com>
 *
 * The MIT License (MIT)
 *
 * Copyright (C) 2026 Zongsoft Corporation <http://www.zongsoft.com>
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
using System.Linq;
using System.Text.RegularExpressions;

using Zongsoft.Common;
using Zongsoft.Tools.Containerizer.Protocol;

namespace Zongsoft.Tools.Containerizer;

internal static partial class ImageReference
{
	public static string GetRepository(string image)
	{
		if(string.IsNullOrWhiteSpace(image) || image.StartsWith('-') || image.Any(char.IsWhiteSpace))
			throw new ContainerizationException(2, Properties.Resources.Engine_8_Message);

		var value = image.Split('@')[0];
		var colon = value.LastIndexOf(':');

		if(colon > value.LastIndexOf('/'))
			value = value[..colon];

		if(!value.Contains('/'))
			value = $"docker.io/library/{value}";
		else if(!value.Split('/')[0].Contains('.') && !value.Split('/')[0].Contains(':') && !value.StartsWith("localhost/", StringComparison.Ordinal))
			value = $"docker.io/{value}";

		return value;
	}

	public static void ValidateTag(string value)
	{
		if(value == null || !GetTagRegex().IsMatch(value))
			throw new ContainerizationException(2, Properties.Resources.Engine_8_Message);
	}

	public static void ValidateRepository(string value)
	{
		if(value == null || GetRepository(value) != value || !GetRepositoryRegex().IsMatch(value))
			throw new ContainerizationException(2, Properties.Resources.Engine_8_Message);
	}

	internal static bool TryParseDigest(string text, out Checksum checksum)
	{
		checksum = default;
		if(text?.StartsWith("sha256:", StringComparison.Ordinal) != true)
			return false;

		try
		{
			// Core handles algorithm/hex validation; OCI references require the canonical lowercase form.
			if(Checksum.TryParse(text, out var value) && !value.IsEmpty && text == value.ToString().ToLowerInvariant())
			{
				checksum = value;
				return true;
			}
		}
		catch(FormatException) { } // Core TryParse can throw for malformed hexadecimal input.

		return false;
	}

	[GeneratedRegex(@"^[A-Za-z0-9_][A-Za-z0-9_.-]{0,127}$")]
	private static partial Regex GetTagRegex();
	[GeneratedRegex(@"^[a-z0-9][a-z0-9.-]*(?::[0-9]+)?/[a-z0-9]+(?:[._-]+[a-z0-9]+)*(?:/[a-z0-9]+(?:[._-]+[a-z0-9]+)*)*$")]
	private static partial Regex GetRepositoryRegex();
}
