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

using Zongsoft.Terminals;
using Zongsoft.Components;

namespace Zongsoft.Tools.Packager;

[CommandOption(NAME_OPTION, typeof(string))]
[CommandOption(VERSION_OPTION, typeof(string))]
[CommandOption(PLATFORM_OPTION, typeof(string), Required = true)]
[CommandOption(FRAMEWORK_OPTION, typeof(string), Required = true)]
[CommandOption(SOURCE_OPTION, typeof(string))]
[CommandOption(EDITION_OPTION, typeof(string))]
[CommandOption(COMPILATION_OPTION, typeof(string), DEFAULT_COMPILATION)]
[CommandOption(ARCHITECTURE_OPTION, typeof(string), "X64")]
[CommandOption(OUTPUT_OPTION, typeof(string))]
[CommandOption(EXCLUDE_OPTION, typeof(string))]
[CommandOption(MIGRATOR_OPTION, typeof(string))]
[CommandOption(OVERWRITE_OPTION, typeof(string), "False")]
[CommandOption(URL_OPTION, typeof(string), DEFAULT_URL)]
[CommandOption(TITLE_OPTION, typeof(string))]
[CommandOption(LICENSE_OPTION, typeof(string))]
[CommandOption(CATEGORY_OPTION, typeof(string))]
[CommandOption(MAINTAINER_OPTION, typeof(string), DEFAULT_MAINTAINER)]
[CommandOption(SUMMARY_OPTION, typeof(string))]
[CommandOption(DESCRIPTION_OPTION, typeof(string))]
[CommandOption(DEPENDENCIES_OPTION, typeof(string))]
[CommandOption(INSTALL_PATH_OPTION, typeof(string))]
[CommandOption(LISTEN_OPTION, typeof(string))]
[CommandOption(DAEMON_OPTION, typeof(string))]
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
	protected const string URL_OPTION = Variables.URL;
	protected const string LICENSE_OPTION = Variables.LICENSE;
	protected const string CATEGORY_OPTION = Variables.CATEGORY;
	protected const string MAINTAINER_OPTION = Variables.MAINTAINER;
	protected const string DEPENDENCIES_OPTION = Variables.DEPENDENCIES;
	protected const string EXCLUDE_OPTION = Variables.EXCLUDE;
	protected const string MIGRATOR_OPTION = "migrator";
	protected const string OVERWRITE_OPTION = "overwrite";
	protected const string INSTALL_PATH_OPTION = "install-path";
	protected const string LISTEN_OPTION = Variables.LISTEN;
	protected const string DAEMON_OPTION = Variables.DaemonVariable.DAEMON;
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

		//仅使用已知变量解析源目录，身份信息随后由源版本文件补全。
		var variables = GetVariables(context);

		foreach(var option in new[] { NAME_OPTION, EDITION_OPTION, VERSION_OPTION })
		{
			var value = context.Options.GetValue(option)?.ToString();
			if(!string.IsNullOrWhiteSpace(value))
				variables[option] = value;
			else
				variables.Remove(option);
		}

		var normalized = Normalizer.Normalize(variables.GetValueOrDefault(SOURCE_OPTION), variables);
		if(!normalized.Succeed)
			throw new InvalidOperationException(string.Format(Properties.Resources.SourceVariableUndefined_Message, normalized.Value));

		var source = normalized.Value;

		if(string.IsNullOrEmpty(source))
			source = Environment.CurrentDirectory;
		else if(!Path.IsPathFullyQualified(source))
			source = Path.Combine(Environment.CurrentDirectory, source);

		if(!Directory.Exists(source))
		{
			Dumper.DirectoryNotExist(CommandOutletColor.Red, source);
			return ValueTask.FromResult<object>(null);
		}

		var name = ResolveIdentity(NAME_OPTION);
		var edition = ResolveIdentity(EDITION_OPTION);
		var versionText = ResolveIdentity(VERSION_OPTION);

		if(!string.IsNullOrWhiteSpace(versionText) && !Version.TryParse(versionText, out _))
			throw new InvalidOperationException(string.Format(Properties.Resources.SourceVersionInvalid_Message, source));

		var versionFile = VersionFile.Load(source, name, edition,
			string.IsNullOrWhiteSpace(versionText) ? null : Version.Parse(versionText));

		string ResolveIdentity(string key)
		{
			var result = Normalizer.Normalize(variables.GetValueOrDefault(key), variables);
			if(!result.Succeed)
				throw new InvalidOperationException(string.Format(Properties.Resources.VariableResolutionFailed_Message, result.Value));
			return result.Value;
		}

		//身份确定后初始化全部变量，输出、载荷与脚本均使用最终值。
		variables[NAME_OPTION] = versionFile.Identifier.Name;
		variables[EDITION_OPTION] = versionFile.Identifier.Edition;
		variables[VERSION_OPTION] = versionFile.Identifier.Version.ToString();
		variables[SOURCE_OPTION] = Path.GetFullPath(source);

		Normalizer.Initialize(variables);

		var overwrite = GetOverwrite(context);
		var output = Normalizer.Variables.Output ?? source;

		Normalizer.Variables[SOURCE_OPTION] = source = Path.GetFullPath(source);
		Normalizer.Variables[OUTPUT_OPTION] = output = Path.GetFullPath(Path.Combine(source, output));

		//创建安装包对象
		var package = this.CreatePackage(context);
		if(package == null)
			return ValueTask.FromResult<object>(null);

		//确保输出目录存在
		if(!Directory.Exists(output))
			Directory.CreateDirectory(output);

		var migrator = context.Options.GetValue<string>(MIGRATOR_OPTION);
		if(!string.IsNullOrWhiteSpace(migrator))
			package.Migrator = Migrator.Load(package, migrator);

		//生成安装脚本
		package.Scriptor.Script();

		//加载安装条目
		package.Entries.Load(source,
			context.Arguments,
			[Normalizer.Variables.Exclude]);

		package.Migrator?.Attach(package);

		//直接添加内存版本条目，替换载荷中的旧版本文件。
		package.Entries.SetVersion(versionFile.Identifier);

		//安装包全部生成成功后才更新源版本文件。
		package.Pack(output, overwrite);
		versionFile.Save(Path.Combine(output, package.FileName));

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
		var installPath = Normalizer.Variables[INSTALL_PATH_OPTION];

		if(!string.IsNullOrEmpty(installPath))
			package.InstallPath = Normalizer.Normalize(installPath);
	}
	#endregion

	#region 私有方法
	internal static Dictionary<string, string> GetVariables(CommandContext context) => Variables.From(context);

	private static bool GetOverwrite(CommandContext context)
	{
		foreach(var option in context.Options)
		{
			if(!string.Equals(option.Key, OVERWRITE_OPTION, StringComparison.OrdinalIgnoreCase))
				continue;

			if(option.Value == null)
				return true;

			var value = Normalizer.Normalize(option.Value.ToString());
			if(bool.TryParse(value, out var result))
				return result;

			if(string.Equals(value, "1", StringComparison.OrdinalIgnoreCase) || string.Equals(value, "yes", StringComparison.OrdinalIgnoreCase) ||
				string.Equals(value, "on", StringComparison.OrdinalIgnoreCase) || string.Equals(value, "enable", StringComparison.OrdinalIgnoreCase) ||
				string.Equals(value, "enabled", StringComparison.OrdinalIgnoreCase))
				return true;

			if(string.Equals(value, "0", StringComparison.OrdinalIgnoreCase) || string.Equals(value, "no", StringComparison.OrdinalIgnoreCase) ||
				string.Equals(value, "off", StringComparison.OrdinalIgnoreCase) || string.Equals(value, "disable", StringComparison.OrdinalIgnoreCase) ||
				string.Equals(value, "disabled", StringComparison.OrdinalIgnoreCase))
				return false;

			throw new ArgumentException(null, OVERWRITE_OPTION);
		}

		return false;
	}
	#endregion
}
