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
 * Copyright (C) 2015-2026 Zongsoft Corporation <http://www.zongsoft.com>
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

using NuGet.RuntimeModel;

namespace Zongsoft.Tools.Deployer;

/// <summary>使用内嵌的固定 RID 图谱，将部署平台和架构转换为有序的运行时回退链。</summary>
/// <remarks>托管与原生资产可分别使用回退链中的不同层级；图谱不依赖运行机器安装的 SDK。</remarks>
internal static class NugetRuntime
{
	#region 常量定义
	private const string RUNTIME_GRAPH_RESOURCE = "Deployer.RuntimeGraph.json";
	#endregion

	#region 私有变量
	private static readonly Lazy<RuntimeGraph> _runtimeGraph = new(LoadRuntimeGraph);
	#endregion

	#region 公共方法
	/// <summary>根据部署平台和架构获取有序 RID 回退链；缺少任一配置时不选择运行时专属资产。</summary>
	public static IEnumerable<string> GetIdentifiers(IDictionary<string, string> variables)
	{
		if(variables == null || !variables.TryGetValue("platform", out var platform) || string.IsNullOrWhiteSpace(platform)
			|| !variables.TryGetValue("architecture", out var architecture) || string.IsNullOrWhiteSpace(architecture))
			return [];

		return GetRuntimeIdentifiers(platform.Trim(), architecture.Trim());
	}
	#endregion

	#region 私有方法
	private static RuntimeGraph LoadRuntimeGraph()
	{
		using var stream = typeof(NugetRuntime).Assembly.GetManifestResourceStream(RUNTIME_GRAPH_RESOURCE)
			?? throw new InvalidDataException($"Missing embedded runtime graph: {RUNTIME_GRAPH_RESOURCE}.");

		return JsonRuntimeFormat.ReadRuntimeGraph(stream);
	}

	private static IEnumerable<string> GetRuntimeIdentifiers(string platform, string architecture)
	{
		platform = platform.ToLowerInvariant() switch
		{
			"windows" => "win",
			"mac" or "macos" => "osx",
			var value => value,
		};

		architecture = architecture.ToLowerInvariant() switch
		{
			"x32" => "x86",
			var value => value,
		};

		return _runtimeGraph.Value.ExpandRuntime($"{platform}-{architecture}");
	}
	#endregion
}
