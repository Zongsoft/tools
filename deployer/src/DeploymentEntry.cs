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
 * Copyright (C) 2015-2024 Zongsoft Corporation <http://www.zongsoft.com>
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

namespace Zongsoft.Tools.Deployer
{
	public sealed class DeploymentEntry
	{
		#region 私有构造
		private DeploymentEntry(Configuration.Profiles.Profile profile, string name, Target source, Target destination, string requisition = null)
		{
			this.Profile = profile;
			this.Name = name?.Trim();
			this.Source = source;
			this.Destination = destination;
			this.Requisition = requisition?.Trim();
		}
		#endregion

		#region 公共属性
		public string Name { get; }
		public Target Source { get; }
		public Target Destination { get; }
		public string Requisition { get; }
		public Configuration.Profiles.Profile Profile { get; }
		#endregion

		#region 公共方法
		public bool Ignored(IDictionary<string, string> variables) => !Utility.Requisition.IsRequisites(variables, this.Requisition);
		#endregion

		#region 静态方法
		public static DeploymentEntry Get(DeploymentContext context, Configuration.Profiles.ProfileEntry entry)
		{
			var source = Utility.Requisition.GetRequisites(entry.Name, out var sourceRequisite).ToString();
			var destination = Utility.Requisition.GetRequisites(entry.Value, out var destinationRequisite).ToString();
			var requisition = sourceRequisite.IsEmpty ?
				(destinationRequisite.IsEmpty ? null : destinationRequisite.ToString()) :
				(destinationRequisite.IsEmpty ? sourceRequisite.ToString() : $"{sourceRequisite} & {destinationRequisite}");

			var index = source.IndexOf(':');

			var name = index switch
			{
				< 1 => string.Empty,
				_ => source[0..index],
			};

			source = index switch
			{
				< 0 => source,
				_ => source[(index + 1)..],
			};

			var sourceName = context.Normalize(source, variable => context.Deployer.Terminal.UndefinedVariable(variable, source, entry.Profile.FilePath, entry.LineNumber));
			var sourcePath = Path.IsPathRooted(sourceName) ? Path.GetDirectoryName(sourceName) : Path.GetDirectoryName(entry.Profile.FilePath);

			var destinationName = string.IsNullOrWhiteSpace(destination) ? string.Empty : context.Normalize(destination, variable => context.Deployer.Terminal.UndefinedVariable(variable, destination, entry.Profile.FilePath, entry.LineNumber));
			var destinationPath = entry.Section == null ? context.DestinationDirectory : Path.Combine(context.DestinationDirectory,
					context.Normalize(entry.Section.FullName.Replace(' ', Path.DirectorySeparatorChar), variable => context.Deployer.Terminal.UndefinedVariable(variable, $"[{entry.Section.FullName}]", entry.Profile.FilePath, entry.LineNumber)));

			return new(entry.Profile, name, new Target(sourceName, sourcePath), new Target(destinationName, destinationPath), requisition);
		}
		#endregion

		#region 重写方法
		public override string ToString()
		{
			if(this.Destination.IsEmpty)
				return string.IsNullOrEmpty(this.Requisition) ? $"{this.Source}" : $"{this.Source}\t<{this.Requisition}>";
			else
				return string.IsNullOrEmpty(this.Requisition) ? $"{this.Source}={this.Destination}" : $"{this.Source}={this.Destination}\t<{this.Requisition}>";
		}
		#endregion

		#region 嵌套结构
		public readonly struct Target
		{
			#region 构造函数
			public Target(string name, string path)
			{
				this.Name = name?.Trim();
				this.Path = path?.Trim();
			}
			#endregion

			#region 公共字段
			public readonly string Name;
			public readonly string Path;
			#endregion

			#region 公共属性
			public bool IsEmpty => string.IsNullOrEmpty(this.Name);
			public string FullPath => System.IO.Path.Combine(this.Path, this.Name);
			#endregion

			#region 重写方法
			public override string ToString() => this.Name;
			#endregion
		}
		#endregion
	}
}