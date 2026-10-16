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
using System.Threading;
using System.Threading.Tasks;

namespace Zongsoft.Tools.Deployer;

partial class DeploymentPlan
{
	#region 嵌套类型
	/// <summary>描述一项部署操作的源、目标、来源和执行状态，或等待依赖求解的包占位操作。</summary>
	public sealed class Operation
	{
		#region 公共属性
		/// <summary>获取或设置操作类型：Copy、Render、Delete 或包占位类型。</summary>
		public string Kind { get; set; }
		/// <summary>获取或设置逻辑源文件路径。</summary>
		public string Source { get; set; }
		/// <summary>获取或设置校验时源文件的实际路径。</summary>
		public string ResolvedSource { get; set; }
		/// <summary>获取或设置规范化的输出路径。</summary>
		public string Output { get; set; }
		/// <summary>获取或设置声明该操作的 .deploy 文件路径。</summary>
		public string Manifest { get; set; }
		/// <summary>获取或设置来源包标识，本地文件为空。</summary>
		public string Package { get; set; }
		/// <summary>获取或设置目标框架。</summary>
		public string Framework { get; set; }
		/// <summary>获取或设置目标运行时标识。</summary>
		public string Runtime { get; set; }
		/// <summary>获取或设置成功执行后目标内容的 SHA-256 摘要。</summary>
		public string Hash { get; set; }
		/// <summary>获取或设置计划校验时源内容的 SHA-256 摘要。</summary>
		public string SourceHash { get; set; }
		/// <summary>获取或设置模板求值后 UTF-8 输出的内容哈希。</summary>
		public string ContentHash { get; set; }
		/// <summary>获取或设置执行状态，初始值为 Planned。</summary>
		public string Status { get; set; } = "Planned";
		/// <summary>获取或设置跳过或失败原因。</summary>
		public string Reason { get; set; }
		#endregion

		#region 内部属性
		/// <summary>获取或设置计划阶段生成的内容；不写入报告或锁定文件。</summary>
		[System.Text.Json.Serialization.JsonIgnore]
		internal byte[] Content { get; set; }

		/// <summary>获取或设置包占位操作的延迟展开回调；只在本次部署会话中使用，不写入报告。</summary>
		[System.Text.Json.Serialization.JsonIgnore]
		internal Func<CancellationToken, Task> Expand { get; set; }
		#endregion
	}
	#endregion
}
