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
 * Copyright (C) 2015-2017 Zongsoft Corporation <http://www.zongsoft.com>
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
using System.Linq;
using System.Collections.Generic;

namespace Zongsoft.Tools.Deployer
{
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
			this.Counter = new DeploymentCounter(profile.FilePath);
		}
		#endregion

		#region 公共属性
		public Deployer Deployer { get; }
		public DeploymentCounter Counter { get; }
		public string DestinationDirectory { get; }
		public Configuration.Profiles.Profile Profile { get; }
		public IDictionary<string, string> Variables => this.Deployer.Variables;
		#endregion

		#region 公共方法
		public void Count(DeploymentCounter counter)
		{
			this.Counter.Fail(counter.Failures);
			this.Counter.Success(counter.Successes);
		}
		#endregion
	}

	public static class DeploymentContextUtility
	{
		public static string Normalize(this DeploymentContext context, string text, Action<string> failure) => Normalizer.Normalize(text, context.Variables, failure);

		public static bool IsVerbosity(this DeploymentContext context, Verbosity verbosity) =>
			context.Variables.TryGetValue(Deployer.VERBOSITY_OPTION, out var variable) && Enum.TryParse<Verbosity>(variable, true, out var value) && verbosity == value;
		public static bool IsVerbosity(this Deployer deployer, Verbosity verbosity) =>
			deployer.Variables.TryGetValue(Deployer.VERBOSITY_OPTION, out var variable) && Enum.TryParse<Verbosity>(variable, true, out var value) && verbosity == value;

		public static bool IsVerbosity(this DeploymentContext context, params Verbosity[] verbosities) =>
			context.Variables.TryGetValue(Deployer.VERBOSITY_OPTION, out var variable) && Enum.TryParse<Verbosity>(variable, true, out var value) && verbosities != null && verbosities.Contains(value);
		public static bool IsVerbosity(this Deployer deployer, params Verbosity[] verbosities) =>
			deployer.Variables.TryGetValue(Deployer.VERBOSITY_OPTION, out var variable) && Enum.TryParse<Verbosity>(variable, true, out var value) && verbosities != null && verbosities.Contains(value);

		public static bool IsOverwrite(this DeploymentContext context, Overwrite overwrite) =>
			context.Variables.TryGetValue(Deployer.OVERWRITE_OPTION, out var variable) && Enum.TryParse<Overwrite>(variable, true, out var value) && overwrite == value;
	}
}
