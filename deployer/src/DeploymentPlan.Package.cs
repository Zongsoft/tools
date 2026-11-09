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

using System.Collections.Generic;

namespace Zongsoft.Tools.Deployer;

partial class DeploymentPlan
{
	#region 嵌套类型
	/// <summary>记录选中包的身份、目标框架、内容摘要及引用原因，供部署计划和锁定校验使用。</summary>
	public sealed class Package
	{
		#region 公共属性
		/// <summary>获取或设置包标识。</summary>
		public string Id { get; set; }
		/// <summary>获取或设置选中的包版本。</summary>
		public string Version { get; set; }
		/// <summary>获取或设置包资产的目标框架。</summary>
		public string Framework { get; set; }
		/// <summary>获取或设置包内容的 SHA-256 摘要。</summary>
		public string Hash { get; set; }
		/// <summary>获取或设置引用该包的部署声明或依赖来源。</summary>
		public List<string> RequiredBy { get; set; } = [];
		#endregion
	}
	#endregion
}
