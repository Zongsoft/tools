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
using System.Collections.Generic;

using Zongsoft.Configuration;
using Zongsoft.Tools.Containerizer.Protocol;

namespace Zongsoft.Tools.Containerizer;

/// <summary>通过 Core Settings 读写服务参数，并校验服务名称、重复项及单行值。</summary>
internal static class ServiceSettings
{
	#region 公共方法
	public static Dictionary<string, string> Parse(string text)
	{
		ValidateValue(text);
		return new ParsedSettings(text).ToDictionary(entry => entry.Key, entry => entry.Value ?? string.Empty, StringComparer.OrdinalIgnoreCase);
	}

	public static string Format(IEnumerable<KeyValuePair<string, string>> values)
	{
		ISettings settings = new Settings();

		foreach(var pair in values)
		{
			ContainerManifest.ValidateIdentity(pair.Key);
			ValidateValue(pair.Value);
			// Add 保留显式空值和首尾空白；索引器会删除空值并裁剪空白。
			settings.Add(pair.Key, pair.Value ?? string.Empty);
		}

		return settings.Value;
	}
	#endregion

	#region 私有方法
	private static void ValidateValue(string value)
	{
		if(value != null && (value.Contains('\r') || value.Contains('\n')))
			throw new ContainerizationException(2, Properties.Resources.Settings_Invalid_Message);
	}
	#endregion

	#region 嵌套类型
	private sealed class ParsedSettings(string text) : Settings(text)
	{
		protected override void OnEntryChanged(string key, string value)
		{
			ContainerManifest.ValidateIdentity(key);
			ValidateValue(value);

			// Core 默认允许后值覆盖；服务参数必须在覆盖前拒绝重复声明。
			if(this.ContainsKey(key))
				throw new ContainerizationException(2, Properties.Resources.Settings_Invalid_Message);

			base.OnEntryChanged(key, value);
		}
	}
	#endregion
}
