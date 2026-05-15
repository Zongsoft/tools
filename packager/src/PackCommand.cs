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
using System.Threading;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.Runtime.InteropServices;

using Zongsoft.Terminals;
using Zongsoft.Components;

namespace Zongsoft.Tools.Packager;

[CommandOption(NAME_OPTION, typeof(string), Required = true)]
[CommandOption(VERSION_OPTION, typeof(Version), Required = true)]
[CommandOption(PLATFORM_OPTION, typeof(Platform), Required = true)]
[CommandOption(FRAMEWORK_OPTION, typeof(string), Required = true)]
[CommandOption(SOURCE_OPTION, typeof(string))]
[CommandOption(EDITION_OPTION, typeof(string))]
[CommandOption(ARCHITECTURE_OPTION, typeof(Architecture), Architecture.X64)]
[CommandOption(OUTPUT_OPTION, typeof(string))]
[CommandOption(DAEMON_OPTION, typeof(string))]
[CommandOption(OVERWRITE_OPTION, typeof(bool), false)]
[CommandOption(URL_OPTION, typeof(string))]
[CommandOption(TITLE_OPTION, typeof(string))]
[CommandOption(LICENSE_OPTION, typeof(string))]
[CommandOption(CATEGORY_OPTION, typeof(string))]
[CommandOption(MAINTAINER_OPTION, typeof(string))]
[CommandOption(SUMMARY_OPTION, typeof(string))]
[CommandOption(DESCRIPTION_OPTION, typeof(string))]
[CommandOption(DEPENDENCIES_OPTION, typeof(string))]
[CommandOption(INSTALL_PATH_OPTION, typeof(string))]
[CommandOption(SCRIPT_INSTALLING_OPTION, typeof(string))]
[CommandOption(SCRIPT_INSTALLED_OPTION, typeof(string))]
[CommandOption(SCRIPT_UNINSTALLING_OPTION, typeof(string))]
[CommandOption(SCRIPT_UNINSTALLED_OPTION, typeof(string))]
public abstract partial class PackCommand<TPackage> : CommandBase<CommandContext> where TPackage : Package
{
	#region 常量定义
	protected const string NAME_OPTION = "name";
	protected const string TITLE_OPTION = "title";
	protected const string SOURCE_OPTION = "source";
	protected const string OUTPUT_OPTION = "output";
	protected const string EDITION_OPTION = "edition";
	protected const string VERSION_OPTION = "version";
	protected const string PLATFORM_OPTION = "platform";
	protected const string FRAMEWORK_OPTION = "framework";
	protected const string OVERWRITE_OPTION = "overwrite";
	protected const string ARCHITECTURE_OPTION = "architecture";
	protected const string DAEMON_OPTION = "daemon";
	protected const string URL_OPTION = "url";
	protected const string LICENSE_OPTION = "license";
	protected const string CATEGORY_OPTION = "category";
	protected const string MAINTAINER_OPTION = "maintainer";
	protected const string SUMMARY_OPTION = "summary";
	protected const string DESCRIPTION_OPTION = "description";
	protected const string DEPENDENCIES_OPTION = "dependencies";
	protected const string INSTALL_PATH_OPTION = "install-path";
	protected const string SCRIPT_INSTALLING_OPTION = "script-installing";
	protected const string SCRIPT_INSTALLED_OPTION = "script-installed";
	protected const string SCRIPT_UNINSTALLING_OPTION = "script-uninstalling";
	protected const string SCRIPT_UNINSTALLED_OPTION = "script-uninstalled";

	protected const string DEFAULT_MAINTAINER = "Zongsoft Studio <zongsoft@gmail.com>";
	protected const string DEFAULT_URL = "https://github.com/Zongsoft";
	#endregion

	#region 执行方法
	protected override ValueTask<object> OnExecuteAsync(CommandContext context, CancellationToken cancellation)
	{
		var version = context.Options.GetValue<Version>(VERSION_OPTION);
		var platform = context.Options.GetValue<Platform>(PLATFORM_OPTION);
		var architecture = context.Options.GetValue<Architecture>(ARCHITECTURE_OPTION);

		if(version.IsZero())
		{
			Terminal.WriteLine(CommandOutletColor.Red, $"The version number is invalid.");
			return ValueTask.FromResult<object>(null);
		}

		var runtime = Utility.GetRuntimeIdentifier(platform, architecture);
		Normalizer.Initialize(GetVariables(context,
		[
			new("Runtime", runtime),
			new("RuntimeIdentifier", runtime),
			new("Architecture", architecture.ToString().ToLowerInvariant()),
		]));

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

		Terminal.WriteLine(CommandOutletColor.DarkCyan, $"Installing package generation in progress, please wait...");
		Terminal.WriteLine();

		var package = this.CreatePackage(context);
		if(package == null)
			return ValueTask.FromResult<object>(null);

		package.Scriptor.Script(
			new(source,
				Normalizer.Normalize(context.Options.GetValue<string>(DAEMON_OPTION, null)),
				Normalizer.NormalizeFile(context.Options.GetValue<string>(SCRIPT_INSTALLING_OPTION, null)),
				Normalizer.NormalizeFile(context.Options.GetValue<string>(SCRIPT_INSTALLED_OPTION, null)),
				Normalizer.NormalizeFile(context.Options.GetValue<string>(SCRIPT_UNINSTALLING_OPTION, null)),
				Normalizer.NormalizeFile(context.Options.GetValue<string>(SCRIPT_UNINSTALLED_OPTION, null))
			));

		package.Entries.Load(source, context.Arguments);
		if(package.Entries.Count == 0)
		{
			Terminal.WriteLine(CommandOutletColor.Red, $"The source directory '{source}' does not contain any package entries.");
			return ValueTask.FromResult<object>(null);
		}

		package.Pack(output, context.Options.Switch(OVERWRITE_OPTION));

		Terminal.WriteLine(CommandOutletColor.DarkGreen, string.Format(Properties.Resources.PackageGeneratedSuccessfully_Message, output));
		return ValueTask.FromResult<object>(output);
	}
	#endregion

	#region 抽象方法
	protected abstract TPackage CreatePackage(CommandContext context);
	#endregion

	#region 配置方法
	protected static void Configure(Package package, CommandContext context)
	{
		package.Framework = context.Options.GetValue<string>(FRAMEWORK_OPTION);
		package.InstallPath = Normalizer.Normalize(context.Options.GetValue<string>(INSTALL_PATH_OPTION));
		package.Title = Normalizer.Normalize(context.Options.GetValue<string>(TITLE_OPTION));
		package.Summary = Normalizer.NormalizeFile(context.Options.GetValue<string>(SUMMARY_OPTION));
		package.Description = Normalizer.NormalizeFile(context.Options.GetValue<string>(DESCRIPTION_OPTION));
		package.Url = Normalizer.Normalize(context.Options.GetValue<string>(URL_OPTION), DEFAULT_URL);
		package.Category = Normalizer.Normalize(context.Options.GetValue<string>(CATEGORY_OPTION));
		package.License = Normalizer.Normalize(context.Options.GetValue<string>(LICENSE_OPTION));
		package.Maintainer = Normalizer.Normalize(context.Options.GetValue<string>(MAINTAINER_OPTION), DEFAULT_MAINTAINER);
		package.Dependencies = Normalizer.NormalizeList(context.Options.GetValue<string>(DEPENDENCIES_OPTION));
	}
	#endregion

	#region 私有方法
	static Dictionary<string, string> GetVariables(CommandContext context, params KeyValuePair<string, string>[] options)
	{
		var variables = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

		foreach(System.Collections.DictionaryEntry variable in Environment.GetEnvironmentVariables())
			variables[variable.Key.ToString()] = variable.Value?.ToString();

		foreach(var option in context.Options)
		{
			if(Normalizer.TryNormalize(option.Value?.ToString(), out var value))
				variables[option.Key] = value;
		}

		foreach(var option in options)
		{
			if(Normalizer.TryNormalize(option.Value, out var value))
				variables[option.Key] = value;
		}

		return variables;
	}
	#endregion
}
