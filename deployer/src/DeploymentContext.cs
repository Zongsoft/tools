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

using Zongsoft.Text.Templating;

namespace Zongsoft.Tools.Deployer;

/// <summary>提供当前描述文件的部署环境，包括部署器、目标目录、变量及共享计数。</summary>
public class DeploymentContext
{
	#region 构造函数
	/// <summary>创建当前 .deploy 文件的输出上下文。</summary>
	/// <param name="deployer">负责本次部署的实例。</param>
	/// <param name="profile">当前 .deploy 文件的配置。</param>
	/// <param name="outputDirectory">该文件对应的输出目录。</param>
	public DeploymentContext(Deployer deployer, Zongsoft.Configuration.Profiles.Profile profile, string outputDirectory)
	{
		if(string.IsNullOrWhiteSpace(outputDirectory))
			throw new ArgumentNullException(nameof(outputDirectory));

		this.Deployer = deployer ?? throw new ArgumentNullException(nameof(deployer));
		this.Profile = profile ?? throw new ArgumentNullException(nameof(profile));
		this.OutputDirectory = outputDirectory;
		this.Counter = deployer.Session?.Counter ?? new DeploymentCounter(profile.FilePath);
	}
	#endregion

	#region 公共属性
	/// <summary>获取负责本次部署的实例。</summary>
	public Deployer Deployer { get; }
	/// <summary>获取本次部署共享的计数器。</summary>
	public DeploymentCounter Counter { get; }
	/// <summary>获取当前 .deploy 文件对应的输出目录。</summary>
	public string OutputDirectory { get; }
	/// <summary>获取当前 .deploy 文件的配置。</summary>
	public Configuration.Profiles.Profile Profile { get; }
	/// <summary>获取当前部署器的模板评估器。</summary>
	public TemplateEvaluator Evaluator => this.Deployer.Evaluator;
	#endregion
}
