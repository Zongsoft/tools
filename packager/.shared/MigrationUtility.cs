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
using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace Zongsoft.Tools.Packager.Migration;

public static class MigrationUtility
{
	#region 公共方法
	public static string Get(this IReadOnlyDictionary<string, string> parameters, string key, string fallback = null) => parameters.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value) ? value : fallback;

	public static int Seconds(this IReadOnlyDictionary<string, string> parameters, string key, int fallback)
	{
		var value = parameters.Get(key);
		if(value == null)
			return fallback;

		var match = Regex.Match(value, @"^(\d+)(s|m)?$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
		if(!match.Success || !int.TryParse(match.Groups[1].Value, out var number) || number <= 0 || number > 86400)
			throw new InvalidDataException(string.Format(MigrationResources.ParameterInvalid, key));

		var seconds = checked(number * (match.Groups[2].Value.Equals("m", StringComparison.OrdinalIgnoreCase) ? 60 : 1));
		if(seconds > 86400)
			throw new InvalidDataException(string.Format(MigrationResources.ParameterTimeoutExceeded, key));

		return seconds;
	}

	#endregion
}
