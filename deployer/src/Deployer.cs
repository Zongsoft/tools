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

using Zongsoft.Terminals;
using Zongsoft.Configuration.Profiles;

namespace Zongsoft.Tools.Deployer
{
	public class Deployer
	{
		#region 常量定义
		internal const string EXPANSION_OPTION = "expansion";
		internal const string OVERWRITE_OPTION = "overwrite";
		internal const string VERBOSITY_OPTION = "verbosity";
		internal const string DESTINATION_OPTION = "destination";
		internal const string IGNOREDEPLOYMENTFILE_OPTION = "ignoreDeploymentFile";

		internal const string DEFAULT_DEPLOYMENT_FILENAME = ".deploy";
		#endregion

		#region 成员字段
		private readonly ITerminal _terminal;
		private readonly IDictionary<string, string> _variables;
		#endregion

		#region 构造函数
		public Deployer(ITerminal terminal, IEnumerable<KeyValuePair<string, string>> options)
		{
			_terminal = terminal ?? throw new ArgumentNullException(nameof(terminal));
			_variables = Collections.DictionaryExtension.ToDictionary<string, string>(Environment.GetEnvironmentVariables(), StringComparer.OrdinalIgnoreCase);

			//设置选项的默认值
			_variables.TryAdd(OVERWRITE_OPTION, Overwrite.Newest.ToString());
			_variables.TryAdd(VERBOSITY_OPTION, Verbosity.Normal.ToString());

			//将部署目录中的 appsettings.json 文件内容解析后加载到变量集
			AppSettingsUtility.Load(_variables);
			//初始化 Nuget 相关的变量
			NugetUtility.Initialize(_variables);

			if(options != null)
			{
				foreach(var option in options)
					_variables[option.Key] = this.Normalize(option.Value, variable => throw new TerminalCommandExecutor.ExitException(-1, string.Format(Properties.Resources.VariableUndefinedInOption_Message, variable, option.Value)));
			}
		}
		#endregion

		#region 公共属性
		public ITerminal Terminal => _terminal;
		public IDictionary<string, string> Variables => _variables;
		#endregion

		#region 公共方法
		public Task<DeploymentCounter> DeployAsync(string deploymentFilePath, CancellationToken cancellation = default) => DeployAsync(deploymentFilePath, null, cancellation);
		public async Task<DeploymentCounter> DeployAsync(string deploymentFilePath, string destinationDirectory, CancellationToken cancellation = default)
		{
			if(string.IsNullOrWhiteSpace(deploymentFilePath))
				throw new ArgumentNullException(nameof(deploymentFilePath));

			if(!Path.IsPathRooted(deploymentFilePath))
				deploymentFilePath = Path.Combine(Environment.CurrentDirectory, deploymentFilePath);

			//对部署文件路径进行参数规整
			deploymentFilePath = this.Normalize(deploymentFilePath, variable => _terminal.UndefinedVariable(variable, deploymentFilePath));

			if(!File.Exists(deploymentFilePath))
			{
				//如果指定部署文件路径是个目录，并且该目录下有一个名为.deploy的文件，则将该部署文件路径指向它
				if(Directory.Exists(deploymentFilePath) && File.Exists(Path.Combine(deploymentFilePath, ".deploy")))
					deploymentFilePath = Path.Combine(deploymentFilePath, ".deploy");
				else
				{
					//打印部署文件不存在的消息
					_terminal.FileNotExists(deploymentFilePath);
					//返回部署计数器
					return new DeploymentCounter(deploymentFilePath, 1, 0);
				}
			}

			if(string.IsNullOrWhiteSpace(destinationDirectory))
			{
				if(_variables.TryGetValue(DESTINATION_OPTION, out destinationDirectory))
				{
					if(!Path.IsPathRooted(destinationDirectory))
						destinationDirectory = Path.Combine(Environment.CurrentDirectory, destinationDirectory);
				}
				else
				{
					destinationDirectory = Environment.CurrentDirectory;
				}
			}

			//对目标目录路径进行参数规整
			destinationDirectory = this.Normalize(destinationDirectory, variable => _terminal.UndefinedVariable(variable, destinationDirectory));

			if(!Directory.Exists(destinationDirectory))
				Directory.CreateDirectory(destinationDirectory);

			//创建部署上下文对象
			var context = this.CreateContext(deploymentFilePath, destinationDirectory);

			foreach(var item in context.Profile)
			{
				await this.DeployItemAsync(context, item, cancellation);
			}

			return context.Counter;
		}
		#endregion

		#region 虚拟方法
		protected virtual DeploymentContext CreateContext(string deploymentFilePath, string destinationDirectory)
		{
			if(string.IsNullOrWhiteSpace(deploymentFilePath))
				throw new ArgumentNullException(nameof(deploymentFilePath));

			return new DeploymentContext(this, Profile.Load(deploymentFilePath), destinationDirectory);
		}
		#endregion

		#region 私有方法
		private async Task DeployItemAsync(DeploymentContext context, ProfileItem item, CancellationToken cancellation)
		{
			switch(item.ItemType)
			{
				case ProfileItemType.Section:
					var section = (ProfileSection)item;

					//确保部署的目标目录已经存在，如不存在则创建它
					Utility.EnsureDirectory(context.DestinationDirectory,
						this.Normalize(
							section.FullName.Replace(' ', '/'),
							variable => _terminal.UndefinedVariable(variable, $"[{section.FullName}]", section.Profile.FilePath, section.LineNumber)
						)
					);

					foreach(var child in section)
						await this.DeployItemAsync(context, child, cancellation);

					break;
				case ProfileItemType.Entry:
					await this.DeployEntryAsync(context, (ProfileEntry)item, cancellation);
					break;
			}
		}

		private async Task DeployEntryAsync(DeploymentContext context, ProfileEntry entry, CancellationToken cancellation)
		{
			//获取部署项
			var deployment = DeploymentEntry.Get(context, entry);

			//如果当前部署项不满足条件则忽略它
			if(deployment.Ignored(_variables))
				return;

			//获取部署项的解析器
			var resolver = DeploymentResolverManager.GetResolver(deployment.Name);

			if(resolver != null)
				await resolver.ResolveAsync(context, deployment, cancellation);
			else
				_terminal.UndefinedResolver(deployment.Name, entry.Name, entry.Profile.FilePath, entry.LineNumber);
		}

		private string Normalize(string text, Action<string> failure) => Normalizer.Normalize(text, _variables, failure);
		#endregion

		#region 嵌套子类
		private class PathToken
		{
			public PathToken(string path, string suffix = null)
			{
				this.Path = path;
				this.Suffix = string.IsNullOrEmpty(suffix) ? null : suffix.Trim(Utility.PATH_SEPARATORS);
			}

			public string Path;
			public string Suffix;

			public bool Exists() => !string.IsNullOrEmpty(this.Path) && File.Exists(this.Path);
			public void Deprecate() => this.Path = null;
			public void Combine(string path) => this.Path = System.IO.Path.Combine(this.Path, path);
			public void AppendSuffix(string value)
			{
				if(string.IsNullOrEmpty(value) || Utility.IsDirectory(value))
					return;

				if(string.IsNullOrEmpty(this.Suffix))
					this.Suffix = value;
				else
					this.Suffix = $"{this.Suffix}/{value}";
			}

			public static PathToken Create(string fullPath, string prefix)
			{
				if(prefix != null && prefix.Length > 0 && fullPath.StartsWith(prefix))
					return new PathToken(fullPath, fullPath.Substring(prefix.Length));

				return new PathToken(fullPath);
			}

			public override string ToString() => string.IsNullOrEmpty(this.Suffix) ? this.Path : $"{this.Path}?{this.Suffix}";
		}
		#endregion
	}
}