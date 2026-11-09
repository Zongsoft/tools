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
using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace Zongsoft.Tools.Migrator.Migration;

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
			throw new InvalidDataException(string.Format(MigrationResources.ParameterInvalid_Message, key));

		var seconds = checked(number * (match.Groups[2].Value.Equals("m", StringComparison.OrdinalIgnoreCase) ? 60 : 1));
		if(seconds > 86400)
			throw new InvalidDataException(string.Format(MigrationResources.ParameterTimeoutExceeded_Message, key));

		return seconds;
	}
	#endregion
}
