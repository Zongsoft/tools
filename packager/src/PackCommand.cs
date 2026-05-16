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
 * Copyright (C) 2020-2026 Zongsoft Corporation <http://www.zongsoft.com>
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
using System.Threading;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.Runtime.InteropServices;

using Zongsoft.Services;
using Zongsoft.Terminals;
using Zongsoft.Components;

namespace Zongsoft.Tools.Packager;

[CommandOption(NAME_OPTION, typeof(string), Required = true)]
[CommandOption(VERSION_OPTION, typeof(Version), Required = true)]
[CommandOption(PLATFORM_OPTION, typeof(Platform), Required = true)]
[CommandOption(FRAMEWORK_OPTION, typeof(string), Required = true)]
[CommandOption(SOURCE_OPTION, typeof(string))]
[CommandOption(EDITION_OPTION, typeof(string))]
[CommandOption(COMPILATION_OPTION, typeof(string), DEFAULT_COMPILATION)]
[CommandOption(ARCHITECTURE_OPTION, typeof(Architecture), Architecture.X64)]
[CommandOption(OUTPUT_OPTION, typeof(string))]
[CommandOption(OVERWRITE_OPTION, typeof(bool), false)]
[CommandOption(URL_OPTION, typeof(string), DEFAULT_URL)]
[CommandOption(TITLE_OPTION, typeof(string))]
[CommandOption(LICENSE_OPTION, typeof(string))]
[CommandOption(CATEGORY_OPTION, typeof(string))]
[CommandOption(MAINTAINER_OPTION, typeof(string), DEFAULT_MAINTAINER)]
[CommandOption(SUMMARY_OPTION, typeof(string))]
[CommandOption(DESCRIPTION_OPTION, typeof(string))]
[CommandOption(DEPENDENCIES_OPTION, typeof(string))]
[CommandOption(INSTALL_PATH_OPTION, typeof(string))]
[CommandOption(DAEMON_OPTION, typeof(string))]
[CommandOption(DAEMON_BIND_OPTION, typeof(string))]
[CommandOption(DAEMON_ENVIRONMENTS_OPTION, typeof(string))]
[CommandOption(INSTALLING_OPTION, typeof(string))]
[CommandOption(INSTALLED_OPTION, typeof(string))]
[CommandOption(UNINSTALLING_OPTION, typeof(string))]
[CommandOption(UNINSTALLED_OPTION, typeof(string))]
[CommandOption(PREINSTALLING_OPTION, typeof(string))]
[CommandOption(POSTINSTALLING_OPTION, typeof(string))]
[CommandOption(PREINSTALLED_OPTION, typeof(string))]
[CommandOption(POSTINSTALLED_OPTION, typeof(string))]
[CommandOption(PREUNINSTALLING_OPTION, typeof(string))]
[CommandOption(POSTUNINSTALLING_OPTION, typeof(string))]
[CommandOption(PREUNINSTALLED_OPTION, typeof(string))]
[CommandOption(POSTUNINSTALLED_OPTION, typeof(string))]
public abstract partial class PackCommand<TPackage> : CommandBase<CommandContext> where TPackage : Package
{
	#region 常量定义
	protected const string NAME_OPTION = Variables.NAME;
	protected const string TITLE_OPTION = Variables.TITLE;
	protected const string SOURCE_OPTION = Variables.SOURCE;
	protected const string OUTPUT_OPTION = Variables.OUTPUT;
	protected const string EDITION_OPTION = Variables.EDITION;
	protected const string VERSION_OPTION = Variables.VERSION;
	protected const string PLATFORM_OPTION = Variables.PLATFORM;
	protected const string FRAMEWORK_OPTION = Variables.FRAMEWORK;
	protected const string COMPILATION_OPTION = Variables.COMPILATION;
	protected const string ARCHITECTURE_OPTION = Variables.ARCHITECTURE;
	protected const string SUMMARY_OPTION = Variables.SUMMARY;
	protected const string DESCRIPTION_OPTION = Variables.DESCRIPTION;
	protected const string URL_OPTION = "url";
	protected const string LICENSE_OPTION = "license";
	protected const string CATEGORY_OPTION = "category";
	protected const string OVERWRITE_OPTION = "overwrite";
	protected const string MAINTAINER_OPTION = "maintainer";
	protected const string DEPENDENCIES_OPTION = "dependencies";
	protected const string INSTALL_PATH_OPTION = "install-path";
	protected const string DAEMON_OPTION = Variables.DaemonVariable.DAEMON;
	protected const string DAEMON_BIND_OPTION = Variables.DaemonVariable.DAEMON_BIND;
	protected const string DAEMON_ENVIRONMENTS_OPTION = Variables.DaemonVariable.DAEMON_ENVIRONMENTS;
	protected const string INSTALLING_OPTION = Variables.ScriptVariable.INSTALLING;
	protected const string INSTALLED_OPTION = Variables.ScriptVariable.INSTALLED;
	protected const string UNINSTALLING_OPTION = Variables.ScriptVariable.UNINSTALLING;
	protected const string UNINSTALLED_OPTION = Variables.ScriptVariable.UNINSTALLED;
	protected const string PREINSTALLING_OPTION = Variables.ScriptVariable.PREINSTALLING;
	protected const string POSTINSTALLING_OPTION = Variables.ScriptVariable.POSTINSTALLING;
	protected const string PREINSTALLED_OPTION = Variables.ScriptVariable.PREINSTALLED;
	protected const string POSTINSTALLED_OPTION = Variables.ScriptVariable.POSTINSTALLED;
	protected const string PREUNINSTALLING_OPTION = Variables.ScriptVariable.PREUNINSTALLING;
	protected const string POSTUNINSTALLING_OPTION = Variables.ScriptVariable.POSTUNINSTALLING;
	protected const string PREUNINSTALLED_OPTION = Variables.ScriptVariable.PREUNINSTALLED;
	protected const string POSTUNINSTALLED_OPTION = Variables.ScriptVariable.POSTUNINSTALLED;

	private const string DEFAULT_COMPILATION = "Release";
	private const string DEFAULT_MAINTAINER = "Zongsoft Studio <zongsoft@gmail.com>";
	private const string DEFAULT_URL = "https://github.com/Zongsoft";
	#endregion

	#region 执行方法
	protected override ValueTask<object> OnExecuteAsync(CommandContext context, CancellationToken cancellation)
	{
		//显示启动画面
		Dumper.Splash();

		if(context.Options.GetValue<Version>(VERSION_OPTION).IsZero())
		{
			Terminal.WriteLine(CommandOutletColor.Red, $"The version number is invalid.");
			return ValueTask.FromResult<object>(null);
		}

		//初始化变量集
		Normalizer.Initialize(GetVariables(context).DistinctBy(variable => variable.Key).ToDictionary(StringComparer.OrdinalIgnoreCase));

		if(!Normalizer.TryNormalize(context.Options.GetValue<string>(SOURCE_OPTION), out var source))
			return ValueTask.FromResult<object>(null);

		if(string.IsNullOrEmpty(source))
			source = Environment.CurrentDirectory;
		else if(!Path.IsPathFullyQualified(source))
			source = Path.Combine(Environment.CurrentDirectory, source);

		if(!Directory.Exists(source))
		{
			Terminal.WriteLine(CommandOutletColor.Red, $"The source directory '{source}' does not exist.");
			return ValueTask.FromResult<object>(null);
		}

		if(!Normalizer.TryNormalize(context.Options.GetValue<string>(OUTPUT_OPTION), out var output))
			return ValueTask.FromResult<object>(null);

		Normalizer.Variables[SOURCE_OPTION] = source = Path.GetFullPath(source);
		Normalizer.Variables[OUTPUT_OPTION] = output = Path.GetFullPath(Path.Combine(source, output));

		//确保输出目录存在
		if(!Directory.Exists(output))
			Directory.CreateDirectory(output);

		//创建安装包对象
		var package = this.CreatePackage(context);
		if(package == null)
			return ValueTask.FromResult<object>(null);

		//生成安装脚本
		package.Scriptor.Script();

		//加载安装条目
		package.Entries.Load(source, context.Arguments);

		//添加版本文件
		if(!package.Entries.Contains(".version"))
		{
			var filePath = Path.GetTempFileName();
			var identifier = new ApplicationIdentifier(package.Name, package.Edition, package.Version);

			using var writer = new StreamWriter(filePath);
			writer.WriteLine(identifier.ToString());
			writer.Close();

			package.Entries.Add(source, $"{filePath}:.version");
		}

		//打包，制作安装包
		package.Pack(output, context.Options.Switch(OVERWRITE_OPTION));

		//输出安装包制作成功
		Terminal.WriteLine(CommandOutletColor.DarkGreen, string.Format(Properties.Resources.PackageGeneratedSuccessfully_Message, Path.Combine(output, package.FileName)));
		//返回安装包的文件路径
		return ValueTask.FromResult<object>(Path.Combine(output, package.FileName));
	}
	#endregion

	#region 抽象方法
	protected abstract TPackage CreatePackage(CommandContext context);
	#endregion

	#region 配置方法
	protected static void Configure(Package package, CommandContext context)
	{
		if(context.Options.TryGetValue(INSTALL_PATH_OPTION, out string installPath) && !string.IsNullOrEmpty(installPath))
			package.InstallPath = Normalizer.Normalize(installPath);
	}
	#endregion

	#region 私有方法
	static IEnumerable<KeyValuePair<string, string>> GetVariables(CommandContext context, params KeyValuePair<string, string>[] options)
	{
		foreach(System.Collections.DictionaryEntry variable in Environment.GetEnvironmentVariables())
			yield return new(variable.Key.ToString(), variable.Value?.ToString());

		foreach(var option in context.Descriptor.Options)
			yield return new(option.Name, context.Options.GetValue(option.Name)?.ToString());

		foreach(var option in context.Options)
		{
			if(!context.Descriptor.Options.Contains(option.Key))
				yield return new(option.Key, option.Value?.ToString());
		}

		foreach(var option in options)
			yield return new(option.Key, option.Value);
	}
	#endregion
}
