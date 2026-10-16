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
using System.Text.Json;
using System.Collections.Generic;

namespace Zongsoft.Tools.Deployer;

/// <summary>记录部署操作、描述文件摘要和包版本选择，供预览、报告、锁定校验及后续文件清理使用。</summary>
public sealed partial class DeploymentPlan
{
	#region 公共属性
	/// <summary>获取或设置生成计划的工具版本。</summary>
	public string ToolVersion { get; set; } = typeof(Deployer).Assembly.GetName().Version.ToString();
	/// <summary>获取或设置部署目标根目录。</summary>
	public string Root { get; set; }
	/// <summary>获取或设置按执行顺序排列的部署操作。</summary>
	public List<Operation> Operations { get; set; } = [];
	/// <summary>获取或设置计划生成和执行期间的诊断信息。</summary>
	public List<string> Diagnostics { get; set; } = [];
	/// <summary>获取或设置依赖求解选中的包及其引用来源。</summary>
	public List<Package> Packages { get; set; } = [];
	/// <summary>获取或设置部署描述文件路径及对应内容摘要。</summary>
	public Dictionary<string, string> Manifests { get; set; } = new(DeploymentPath.Comparer);
	/// <summary>获取或设置部署是否成功完成。</summary>
	public bool Succeeded { get; set; }
	#endregion

	#region 读写方法
	/// <summary>从指定文件加载部署计划。</summary>
	/// <param name="path">部署计划文件路径。</param>
	/// <returns>读取的部署计划。</returns>
	public static DeploymentPlan Load(string path) => JsonSerializer.Deserialize<DeploymentPlan>(File.ReadAllText(path)) ?? throw new InvalidDataException(path);
	/// <summary>将部署计划保存到指定文件。</summary>
	/// <param name="path">部署计划文件路径。</param>
	public void Save(string path)
	{
		path = Path.GetFullPath(path);
		var content = JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true });

		ArtifactPublisher.Write(path, stream =>
		{
			using var writer = new StreamWriter(stream, new System.Text.UTF8Encoding(false), leaveOpen: true);
			writer.Write(content.Replace("\r\n", "\n").Replace("\n", "\r\n"));
		});
	}
	#endregion
}
