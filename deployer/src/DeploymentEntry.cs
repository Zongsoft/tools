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

namespace Zongsoft.Tools.Deployer;

/// <summary>表示描述文件中的部署条目，保留解析器、源与目标、过滤条件及声明来源。</summary>
public sealed class DeploymentEntry
{
	#region 私有构造
	private DeploymentEntry(Configuration.Profiles.Profile profile, string name, Target source, Target output, string requisition = null)
	{
		this.Profile = profile;
		this.Name = name?.Trim();
		this.Source = source;
		this.Output = output;
		this.Requisition = requisition?.Trim();
	}
	#endregion

	#region 公共属性
	/// <summary>获取解析器名称。</summary>
	public string Name { get; }
	/// <summary>获取源路径信息。</summary>
	public Target Source { get; }
	/// <summary>获取输出路径信息。</summary>
	public Target Output { get; }
	/// <summary>获取过滤表达式，未指定时为空。</summary>
	public string Requisition { get; }
	/// <summary>获取声明本条目的 .deploy 文件。</summary>
	public Configuration.Profiles.Profile Profile { get; }
	#endregion

	#region 静态方法
	/// <summary>从 Profile 条目解析部署源、输出目标及过滤条件。</summary>
	/// <param name="context">本次部署的变量和输出上下文。</param>
	/// <param name="entry">待解析的 .deploy 条目。</param>
	/// <returns>源和目标均已求值的部署条目。</returns>
	public static DeploymentEntry Get(DeploymentContext context, Configuration.Profiles.ProfileEntry entry)
	{
		var source = Utility.Requisition.GetRequisites(entry.Name, out var sourceRequisite).ToString();
		var output = Utility.Requisition.GetRequisites(entry.Value, out var outputRequisite).ToString();
		var requisition = sourceRequisite.IsEmpty ?
			(outputRequisite.IsEmpty ? null : outputRequisite.ToString()) :
			(outputRequisite.IsEmpty ? sourceRequisite.ToString() : $"{sourceRequisite} & {outputRequisite}");

		var index = source.IndexOf(':');
		if(index >= 0 && source.AsSpan(0, index).Contains("${", StringComparison.Ordinal))
			index = -1;

		var name = index switch
		{
			< 0 => "path",
			_ => source[0..index],
		};

		source = index switch
		{
			< 0 => source,
			_ => source[(index + 1)..],
		};

		var sourceName = context.Deployer.Normalize(source);
		var sourcePath = Path.IsPathRooted(sourceName) ? Path.GetDirectoryName(sourceName) : Path.GetDirectoryName(entry.Profile.FilePath);

		var outputName = string.IsNullOrWhiteSpace(output) ? string.Empty : context.Deployer.Normalize(output);
		//Profile 章节是目录层级，不支持模板表达式；拼接后的文件系统路径不能再次作为模板解析。
		var outputPath = entry.Section == null ? context.OutputDirectory :
			Path.Combine(context.OutputDirectory, entry.Section.FullName.Replace(' ', Path.DirectorySeparatorChar));

		return new(entry.Profile, name, new Target(sourceName, sourcePath), new Target(outputName, outputPath), requisition);
	}
	#endregion

	#region 重写方法
	/// <summary>获取条目的文本表示。</summary>
	/// <returns>源、目标及过滤条件组成的文本。</returns>
	public override string ToString()
	{
		if(this.Output.IsEmpty)
			return string.IsNullOrEmpty(this.Requisition) ? $"{this.Source}" : $"{this.Source}\t<{this.Requisition}>";
		else
			return string.IsNullOrEmpty(this.Requisition) ? $"{this.Source}={this.Output}" : $"{this.Source}={this.Output}\t<{this.Requisition}>";
	}
	#endregion

	#region 嵌套结构
	/// <summary>表示逻辑名称及其路径基准。</summary>
	public readonly struct Target
	{
		#region 构造函数
		/// <summary>使用逻辑名称和目录创建路径信息。</summary>
		/// <param name="name">文件名或相对路径；允许为空。</param>
		/// <param name="path">路径基准。</param>
		public Target(string name, string path)
		{
			this.Name = name?.Trim();
			this.Path = path?.Trim();
		}
		#endregion

		#region 公共字段
		/// <summary>保存逻辑名称。</summary>
		public readonly string Name;
		/// <summary>保存路径基准。</summary>
		public readonly string Path;
		#endregion

		#region 公共属性
		/// <summary>获取一个值，指示逻辑名称是否为空。</summary>
		public bool IsEmpty => string.IsNullOrEmpty(this.Name);
		/// <summary>获取合并后的路径。</summary>
		public string FullPath => System.IO.Path.Combine(this.Path, this.Name);
		#endregion

		#region 重写方法
		/// <summary>获取逻辑名称。</summary>
		/// <returns>逻辑名称。</returns>
		public override string ToString() => this.Name;
		#endregion
	}
	#endregion
}