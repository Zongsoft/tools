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
using System.Collections.Generic;

namespace Zongsoft.Tools.Deployer;

/// <summary>提供当前描述文件的部署环境，包括部署器、目标目录、变量及共享计数。</summary>
public class DeploymentContext
{
	#region 构造函数
	public DeploymentContext(Deployer deployer, Zongsoft.Configuration.Profiles.Profile profile, string destinationDirectory)
	{
		if(string.IsNullOrWhiteSpace(destinationDirectory))
			throw new ArgumentNullException(nameof(destinationDirectory));

		this.Deployer = deployer ?? throw new ArgumentNullException(nameof(deployer));
		this.Profile = profile ?? throw new ArgumentNullException(nameof(profile));
		this.DestinationDirectory = destinationDirectory;
		this.Counter = deployer.Session?.Counter ?? new DeploymentCounter(profile.FilePath);
	}
	#endregion

	#region 公共属性
	public Deployer Deployer { get; }
	public DeploymentCounter Counter { get; }
	public string DestinationDirectory { get; }
	public Configuration.Profiles.Profile Profile { get; }
	public IDictionary<string, string> Variables => this.Deployer.Variables;
	#endregion
}

/// <summary>
/// 提供部署环境选项的查询辅助方法。
/// </summary>
public static class DeploymentContextUtility
{
	public static bool IsVerbosity(this Deployer deployer, Verbosity verbosity) =>
		deployer.Variables.TryGetValue(Deployer.VERBOSITY_OPTION, out var variable) && Zongsoft.Common.Convert.TryConvertValue<Verbosity>(variable, out var value) && verbosity == value;
}
