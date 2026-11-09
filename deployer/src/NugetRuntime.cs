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

using NuGet.RuntimeModel;

using Zongsoft.Text.Templating;

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
	/// <param name="evaluator">包含目标平台和架构的部署变量。</param>
	/// <returns>从最具体到回退项排列的运行时标识符；未指定平台或架构时为空集合。</returns>
	public static IEnumerable<string> GetIdentifiers(TemplateEvaluator evaluator)
	{
		if(evaluator == null || !evaluator.TryGetOption("platform", out var platform) || string.IsNullOrWhiteSpace(platform)
			|| !evaluator.TryGetOption("architecture", out var architecture) || string.IsNullOrWhiteSpace(architecture))
			return [];

		return GetRuntimeIdentifiers(platform.Trim(), architecture.Trim());
	}
	#endregion

	#region 私有方法
	private static RuntimeGraph LoadRuntimeGraph()
	{
		using var stream = typeof(NugetRuntime).Assembly.GetManifestResourceStream(RUNTIME_GRAPH_RESOURCE)
			?? throw new InvalidDataException(string.Format(Properties.Resources.RuntimeGraphMissing_Message, RUNTIME_GRAPH_RESOURCE));

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
