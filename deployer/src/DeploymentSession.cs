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
using System.Security.Cryptography;

namespace Zongsoft.Tools.Deployer;

/// <summary>保存一次部署调用的计划、计数、活动描述文件和 NuGet 请求及求解结果。</summary>
/// <remarks>每次调用独立创建；临时资产展开缓冲与最终计划分开，避免在依赖求解期间执行部署操作。</remarks>
internal sealed class DeploymentSession
{
	#region 构造函数
	public DeploymentSession(string root, DeploymentCounter counter)
	{
		this.Plan = new DeploymentPlan { Root = root };
		this.Counter = counter;
	}
	#endregion

	#region 公共属性
	public DeploymentPlan Plan { get; }
	public DeploymentCounter Counter { get; }
	public HashSet<string> Active { get; } = new(DeploymentPath.Comparer);
	public List<string> Stack { get; } = [];
	public List<(NugetUtility.PackageMetadata Metadata, string Framework)> Roots { get; } = [];
	public Dictionary<string, NugetUtility.PackageMetadata> Packages { get; set; }
	public DeploymentPlan LockedPlan { get; set; }
	public Dictionary<string, NugetUtility.PackageMetadata> Requested { get; } = new(StringComparer.OrdinalIgnoreCase);
	/// <summary>获取或设置包占位展开时的临时输出集合；为空时直接向最终计划登记操作。</summary>
	public List<DeploymentPlan.Operation> Output { get; set; }
	#endregion

	#region 公共方法
	public void Add(DeploymentPlan.Operation operation) => (this.Output ?? this.Plan.Operations).Add(operation);
	public string Validate(string path) => DeploymentPath.Validate(this.Plan.Root, path);
	public static string Hash(string path)
	{
		using var stream = File.OpenRead(path);

		return Convert.ToHexString(SHA256.HashData(stream));
	}
	#endregion
}
