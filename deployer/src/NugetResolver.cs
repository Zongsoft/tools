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
using System.Threading;
using System.Threading.Tasks;
using System.Collections.Generic;

namespace Zongsoft.Tools.Deployer
{
	public class NugetResolver : DeploymentResolverBase
	{
		#region 单例字段
		public static readonly NugetResolver Instance = new();
		#endregion

		#region 私有构造
		private NugetResolver() : base("Nuget") { }
		#endregion

		#region 重写方法
		protected override async Task<IEnumerable<DeploymentUtility.PathToken>> GetSourcesAsync(DeploymentContext context, DeploymentEntry deployment, CancellationToken cancellation)
		{
			if(!Utility.TryGetTargetFramework(context.Variables, out var framework) || string.IsNullOrEmpty(framework))
			{
				context.Deployer.Terminal.UnspecifiedVariable(Utility.FRAMEWORK_VARIABLE);
				return Array.Empty<DeploymentUtility.PathToken>();
			}

			var argument = Argument.Parse(deployment.Source.Name);
			if(argument.IsEmpty)
			{
				context.Deployer.Terminal.IllegalArgument(deployment.Source.Name, deployment.Profile.FilePath);
				return Array.Empty<DeploymentUtility.PathToken>();
			}

			var metadata = await NugetUtility.GetPackageMetadataAsync(context.Variables, argument.Name, argument.Version, cancellation);
			if(metadata == null)
			{
				context.Deployer.Terminal.NotFound(argument.Name, argument.Version);
				return Array.Empty<DeploymentUtility.PathToken>();
			}

			var path = await NugetUtility.DownloadPackageAsync(context.Variables, argument.Name, metadata.Identity.Version, cancellation);
			if(string.IsNullOrEmpty(path))
			{
				context.Deployer.Terminal.DownloadFailed(argument.Name, argument.Version);
				return Array.Empty<DeploymentUtility.PathToken>();
			}

			//下载依赖的包
			var dependents = await NugetUtility.DownloadDependentPackageAsync(context.Variables, metadata, framework, cancellation);

			//如果未指定路径参数
			if(string.IsNullOrEmpty(argument.Path))
			{
				//如果包目录有默认的“.deploy”部署文件，则将它作为返回的部署源文件
				if(File.Exists(Path.Combine(path, Deployer.DEFAULT_DEPLOYMENT_FILENAME)))
					return DeploymentUtility.GetFiles(Path.Combine(path, Deployer.DEFAULT_DEPLOYMENT_FILENAME), context.Variables);

				//从当前包的库目录中查找最适用的框架版本，如果没有找到则返回
				var nearestLibrary = NugetUtility.GetNearestLibraryPath(path, framework);
				if(string.IsNullOrEmpty(nearestLibrary))
				{
					context.Deployer.Terminal.UnmatchPackage(metadata.Identity.ToString(), framework);
					return Array.Empty<DeploymentUtility.PathToken>();
				}

				var directories = new HashSet<string>();

				//将当前包的最合适的库目录加入到源路径中
				directories.Add(nearestLibrary);

				//将依赖包的库目录加入到部署源中
				foreach(var dependent in dependents)
				{
					var direcotry = NugetUtility.GetNearestLibraryPath(dependent, framework);

					if(!string.IsNullOrEmpty(direcotry))
						directories.Add(direcotry);
				}

				var result = new List<DeploymentUtility.PathToken>();
				foreach(var directory in directories)
					result.AddRange(DeploymentUtility.GetFiles(Path.Combine(directory, "*"), context.Variables));
				return result;
			}

			return DeploymentUtility.GetFiles(Path.Combine(path, argument.Path), context.Variables);
		}
		#endregion

		#region 嵌套结构
		public readonly struct Argument
		{
			#region 静态常量
			static readonly char[] SEPARATORS = new char[] { '/', '\\'};
			#endregion

			#region 构造函数
			private Argument(string name, string version = null, string path = null)
			{
				this.Name = name;
				this.Version = version?.Trim();
				this.Path = path?.Trim();
			}
			#endregion

			#region 公共字段
			public readonly string Name;
			public readonly string Version;
			public readonly string Path;
			#endregion

			#region 公共属性
			public bool IsEmpty => string.IsNullOrEmpty(this.Name);
			#endregion

			#region 重写方法
			public override string ToString()
			{
				if(string.IsNullOrEmpty(this.Version))
					return string.IsNullOrEmpty(this.Path) ? this.Name : $"{this.Name}/{this.Path}";
				else
					return string.IsNullOrEmpty(this.Path) ? $"{this.Name}@{this.Version}" : $"{this.Name}@{this.Version}/{this.Path}";
			}
			#endregion

			#region 解析方法
			public static Argument Parse(ReadOnlySpan<char> text)
			{
				if(text.IsEmpty)
					return default;

				var index = text.IndexOfAny(SEPARATORS);

				if(index == 0)
					return default;

				if(index < 0)
				{
					(var name, var version) = ParseIdentity(text);
					return string.IsNullOrEmpty(name) ? default : new Argument(name, version);
				}
				else
				{
					(var name, var version) = ParseIdentity(text[..index]);
					return string.IsNullOrEmpty(name) ? default : new Argument(name, version, text[(index + 1)..].ToString());
				}
			}

			private static (string name, string version) ParseIdentity(ReadOnlySpan<char> text)
			{
				if(text.IsEmpty)
					return default;

				var index = text.IndexOf('@');

				if(index == 0)
					return default;
				if(index < 0)
					return (text.ToString(), null);

				return (text[..index].ToString(), text[(index + 1)..].ToString());
			}
			#endregion
		}
		#endregion
	}
}