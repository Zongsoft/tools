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
using System.Collections.Generic;

using Zongsoft.Text.Templating;

namespace Zongsoft.Tools.Deployer;

partial class Deployer
{
	#region 变量加载
	/// <summary>组合命令参数、目标应用配置、逐级 .env 视图和系统环境，并解析部署目标目录。</summary>
	/// <param name="options">原始命令选项；变量名称中的点号和连字符映射为下划线。</param>
	/// <param name="currentDirectory">搜索 .env 及解析相对目标路径的起始目录；为空时使用进程当前目录。</param>
	/// <returns>按优先级组织变量来源并启用递归求值的评估器。</returns>
	public static TemplateEvaluator CreateEvaluator(IDictionary<string, string> options, string currentDirectory = null)
	{
		currentDirectory ??= Environment.CurrentDirectory;
		var commands = new global::Zongsoft.Common.Variables();

		foreach(var option in options)
			commands[Utility.NormalizeVariableName(option.Key)] = option.Value;

		var evaluator = Utility.CreateEvaluator(commands);
		evaluator.Providers.Add(global::Zongsoft.Common.Variables.Environments());
		Utility.LoadEnvironmentProfiles(evaluator, currentDirectory);
		var target = Path.GetFullPath(evaluator.GetOption(DESTINATION_OPTION) ?? currentDirectory, currentDirectory);

		var settings = new global::Zongsoft.Common.Variables();
		AppSettingsUtility.Load(settings, target);
		evaluator.Providers.Insert(1, settings);
		evaluator.SetVariable(DESTINATION_OPTION, target.Replace('\\', '/'));
		NugetUtility.Initialize(evaluator);
		return evaluator;
	}
	#endregion
}
