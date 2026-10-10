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
 * Copyright (C) 2015-2025 Zongsoft Corporation <http://www.zongsoft.com>
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
using System.Text.Json;

using Zongsoft.Common;

namespace Zongsoft.Tools.Deployer;

/// <summary>读取目标应用的 appsettings.json，将嵌套对象和数组展开为部署变量。</summary>
internal static class AppSettingsUtility
{
	#region 公共方法
	/// <summary>读取指定目录的应用配置；文件缺失时不添加变量。</summary>
	/// <param name="variables">接收扁平化配置值的集合；为空时不执行加载。</param>
	/// <param name="directory">包含 <c>appsettings.json</c> 的目录。</param>
	/// <remarks>显式 <see langword="null"/> 保留为已定义变量，以阻止查询时回退到较低优先级来源。</remarks>
	public static void Load(Variables variables, string directory)
	{
		if(variables == null)
			return;

		var path = Path.Combine(directory, "appsettings.json");

		if(!File.Exists(path))
			return;

		using var stream = File.OpenRead(path);
		using var document = JsonDocument.Parse(stream, new JsonDocumentOptions
		{
			AllowTrailingCommas = true,
			CommentHandling = JsonCommentHandling.Skip,
		});

		foreach(var property in document.RootElement.EnumerateObject())
			Populate(variables, property.Name, property.Value);

		if(variables.TryGetValue("ApplicationName", out var application))
			variables["Application"] = application;
	}
	#endregion

	#region 私有方法
	private static void Populate(Variables variables, string path, JsonElement element)
	{
		switch(element.ValueKind)
		{
			case JsonValueKind.Object:
				foreach(var property in element.EnumerateObject())
					Populate(variables, $"{path}.{property.Name}", property.Value);
				break;
			case JsonValueKind.Array:
				var index = 0;
				foreach(var item in element.EnumerateArray())
					Populate(variables, $"{path}[{index++}]", item);
				break;
			case JsonValueKind.Undefined:
				break;
			default:
				variables[path.Replace('.', '_').Replace('-', '_').Replace("[", "_").Replace("]", "")] = element.ValueKind == JsonValueKind.Null ? null : element.ToString();
				break;
		}
	}
	#endregion
}
