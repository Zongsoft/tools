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
	/// <summary>
	/// 获取或设置包占位展开时的临时输出集合；为空时直接向最终计划登记操作。
	/// </summary>
	public List<DeploymentOperation> Output { get; set; }
	#endregion

	#region 公共方法
	public void Add(DeploymentOperation operation) => (this.Output ?? this.Plan.Operations).Add(operation);
	public string Validate(string path) => DeploymentPath.Validate(this.Plan.Root, path);
	public static string Hash(string path)
	{
		using var stream = File.OpenRead(path);

		return Convert.ToHexString(SHA256.HashData(stream));
	}
	#endregion
}
