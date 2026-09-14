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

/// <summary>描述一项部署操作的源、目标、来源和执行状态，或等待依赖求解的包占位操作。</summary>
public sealed class DeploymentOperation
{
	#region 公共属性
	public string Kind { get; set; }
	public string Source { get; set; }
	public string ResolvedSource { get; set; }
	public string Destination { get; set; }
	public string Manifest { get; set; }
	public string Package { get; set; }
	public string Framework { get; set; }
	public string Runtime { get; set; }
	public string Hash { get; set; }
	public string SourceHash { get; set; }
	public string Status { get; set; } = "Planned";
	public string Reason { get; set; }
	#endregion

	#region 内部属性
	/// <summary>
	/// 获取或设置包占位操作的延迟展开回调；只在本次部署会话中使用，不写入报告。
	/// </summary>
	[System.Text.Json.Serialization.JsonIgnore]
	internal Func<CancellationToken, Task> Expand { get; set; }
	#endregion
}
