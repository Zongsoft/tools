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
using Zongsoft.Text.Templating;

namespace Zongsoft.Tools.Packager;

[CommandOption(NAME_OPTION, typeof(string))]
[CommandOption(VERSION_OPTION, typeof(string))]
[CommandOption(SOURCE_OPTION, typeof(string))]
[CommandOption(EDITION_OPTION, typeof(string))]
[CommandOption(FRAMEWORK_OPTION, typeof(string))]
[CommandOption(COMPILATION_OPTION, typeof(string), DEFAULT_COMPILATION)]
[CommandOption(PLATFORM_OPTION, typeof(string), Required = true)]
[CommandOption(ARCHITECTURE_OPTION, typeof(string), "X64")]
[CommandOption(OUTPUT_OPTION, typeof(string))]
[CommandOption(EXCLUDE_OPTION, typeof(string))]
[CommandOption(MIGRATION_OPTION, typeof(string))]
[CommandOption(WEB_OPTION, typeof(string))]
[CommandOption(OVERWRITE_OPTION, typeof(string), "False")]
[CommandOption(HOMEPAGE_OPTION, typeof(string), DEFAULT_HOMEPAGE)]
[CommandOption(TITLE_OPTION, typeof(string))]
[CommandOption(LICENSE_OPTION, typeof(string))]
[CommandOption(CATEGORY_OPTION, typeof(string))]
[CommandOption(MAINTAINER_OPTION, typeof(string), DEFAULT_MAINTAINER)]
[CommandOption(MANUFACTURER_OPTION, typeof(string), PackageOptions.DEFAULT_MANUFACTURER)]
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
	protected const string NAME_OPTION = PackageOptions.NAME;
	protected const string TITLE_OPTION = PackageOptions.TITLE;
	protected const string SOURCE_OPTION = PackageOptions.SOURCE;
	protected const string OUTPUT_OPTION = PackageOptions.OUTPUT;
	protected const string EDITION_OPTION = PackageOptions.EDITION;
	protected const string VERSION_OPTION = PackageOptions.VERSION;
	protected const string PLATFORM_OPTION = PackageOptions.PLATFORM;
	protected const string FRAMEWORK_OPTION = PackageOptions.FRAMEWORK;
	protected const string COMPILATION_OPTION = PackageOptions.COMPILATION;
	protected const string ARCHITECTURE_OPTION = PackageOptions.ARCHITECTURE;
	protected const string SUMMARY_OPTION = PackageOptions.SUMMARY;
	protected const string DESCRIPTION_OPTION = PackageOptions.DESCRIPTION;
	protected const string HOMEPAGE_OPTION = PackageOptions.HOMEPAGE;
	protected const string LICENSE_OPTION = PackageOptions.LICENSE;
	protected const string CATEGORY_OPTION = PackageOptions.CATEGORY;
	protected const string MAINTAINER_OPTION = PackageOptions.MAINTAINER;
	protected const string MANUFACTURER_OPTION = PackageOptions.MANUFACTURER;
	protected const string DEPENDENCIES_OPTION = PackageOptions.DEPENDENCIES;
	protected const string EXCLUDE_OPTION = PackageOptions.EXCLUDE;
	protected const string MIGRATION_OPTION = "migration";
	protected const string WEB_OPTION = "web";
	protected const string OVERWRITE_OPTION = "overwrite";
	protected const string INSTALL_PATH_OPTION = "install-path";
	protected const string LISTEN_OPTION = PackageOptions.LISTEN;
	protected const string DAEMON_OPTION = PackageOptions.DaemonOptions.DAEMON;
	protected const string DAEMON_ENVIRONMENTS_OPTION = PackageOptions.DaemonOptions.DAEMON_ENVIRONMENTS;
	protected const string INSTALLING_OPTION = PackageOptions.ScriptOptions.INSTALLING;
	protected const string INSTALLED_OPTION = PackageOptions.ScriptOptions.INSTALLED;
	protected const string UNINSTALLING_OPTION = PackageOptions.ScriptOptions.UNINSTALLING;
	protected const string UNINSTALLED_OPTION = PackageOptions.ScriptOptions.UNINSTALLED;
	protected const string PREINSTALLING_OPTION = PackageOptions.ScriptOptions.PREINSTALLING;
	protected const string POSTINSTALLING_OPTION = PackageOptions.ScriptOptions.POSTINSTALLING;
	protected const string PREINSTALLED_OPTION = PackageOptions.ScriptOptions.PREINSTALLED;
	protected const string POSTINSTALLED_OPTION = PackageOptions.ScriptOptions.POSTINSTALLED;
	protected const string PREUNINSTALLING_OPTION = PackageOptions.ScriptOptions.PREUNINSTALLING;
	protected const string POSTUNINSTALLING_OPTION = PackageOptions.ScriptOptions.POSTUNINSTALLING;
	protected const string PREUNINSTALLED_OPTION = PackageOptions.ScriptOptions.PREUNINSTALLED;
	protected const string POSTUNINSTALLED_OPTION = PackageOptions.ScriptOptions.POSTUNINSTALLED;

	private const string DEFAULT_COMPILATION = "Release";
	private const string DEFAULT_MAINTAINER = "Zongsoft";
	private const string DEFAULT_HOMEPAGE = "https://github.com/Zongsoft";
	#endregion

	#region 执行方法
	protected override ValueTask<object> OnExecuteAsync(CommandContext context, CancellationToken cancellation)
	{
		//显示启动画面
		Dumper.Splash();

		//仅使用已知变量解析源目录，身份信息随后由源版本文件补全。
		var evaluator = CreateEvaluator(context);

		var source = evaluator.GetOption(SOURCE_OPTION);

		if(string.IsNullOrEmpty(source))
			source = Environment.CurrentDirectory;
		else if(!Path.IsPathFullyQualified(source))
			source = Path.Combine(Environment.CurrentDirectory, source);

		if(!Directory.Exists(source))
		{
			Dumper.DirectoryNotExist(CommandOutletColor.Red, source);
			return ValueTask.FromResult<object>(null);
		}

		//源目录确定后加载其祖先链 .env；后续变量不能改变本次查找起点。
		source = Path.GetFullPath(source);
		evaluator = CreateEvaluator(context, source);
		evaluator.SetVariable(SOURCE_OPTION, source.Replace('\\', '/'));

		var name = ResolveIdentity(NAME_OPTION);
		var edition = ResolveIdentity(EDITION_OPTION);
		var versionText = ResolveIdentity(VERSION_OPTION);

		if(!string.IsNullOrWhiteSpace(versionText) && !Version.TryParse(versionText, out _))
			throw new InvalidOperationException(string.Format(Properties.Resources.SourceVersionInvalid_Message, source));

		var versionFile = VersionFile.Load(source, name, edition,
			string.IsNullOrWhiteSpace(versionText) ? null : Version.Parse(versionText));
		//身份确定后初始化全部变量，输出、载荷与脚本均使用最终值。
		evaluator.SetVariable(NAME_OPTION, versionFile.Identifier.Name);
		evaluator.SetVariable(EDITION_OPTION, versionFile.Identifier.Edition);
		evaluator.SetVariable(VERSION_OPTION, versionFile.Identifier.Version.ToString());
		evaluator.SetVariable(SOURCE_OPTION, Path.GetFullPath(source).Replace('\\', '/'));

		var options = new PackageOptions(evaluator);
		var cmdlet = new CommandLine.Cmdlet(context.Command.Name);
		cmdlet.Options.Add(new(CommandLine.CmdletOptionKind.Fully, OVERWRITE_OPTION, options[OVERWRITE_OPTION]));
		var overwrite = new CommandContext(context.Executor, cmdlet, context.Command, null).Options.Switch(OVERWRITE_OPTION);
		var output = options.Output ?? source;

		options[SOURCE_OPTION] = source = Path.GetFullPath(source).Replace('\\', '/');
		options[OUTPUT_OPTION] = output = Path.GetFullPath(Path.Combine(source, output)).Replace('\\', '/');

		//创建安装包对象
		var package = this.CreatePackage(context, options);
		if(package == null)
			return ValueTask.FromResult<object>(null);

		//确保输出目录存在
		var migration = context.Options.GetValue<string>(MIGRATION_OPTION, null);
		if(!string.IsNullOrWhiteSpace(migration))
			package.Migration = Migration.Load(package, migration);

		//服务与 Web 配置器共用本次宿主解析结果。
		package.Host = ApplicationHost.Resolve(package);

		//加载安装条目
		package.Entries.Load(source,
			context.Arguments,
			[options.Exclude]);

		package.Migration?.Attach(package);

		var web = Web.Configurator.Parse(context.Options.GetValue<string>(WEB_OPTION, null), source, options);
		if(web.Enabled)
		{
			var configurator = Web.Configurator.Get(web.Hoster);
			var definition = Web.Definition.Load(web.FilePath, options.Evaluator);
			var configuration = new Web.Configurator.Context(package.PackageName, package.InstallPath, options.Evaluator,
				package.Host.Kind == ApplicationHost.HostKind.Generated ? package.Host.Listen : null, package.Architecture);
			var result = configurator.Configure(definition, configuration);
			Web.Installation.Attach(package, result);

			foreach(var diagnostic in result.Diagnostics)
				Terminal.WriteLine(CommandOutletColor.Yellow, diagnostic.ToString());
		}

		//交付、应用服务、升迁和 Web 步骤按各格式的生命周期组合。
		package.Scriptor.Script();
		Web.Installation.Validate(package);

		//直接添加内存版本条目，替换载荷中的旧版本文件。
		package.Entries.SetVersion(versionFile.Identifier);

		//安装包全部生成成功后才更新源版本文件。
		try { package.Pack(output, overwrite); }
		catch(ArtifactAlreadyExistsException exception)
		{
			throw new IOException(string.Format(Properties.Resources.PackageFileAlreadyExists_Message, exception.FilePath), exception);
		}

		versionFile.Save(Path.Combine(output, package.FileName));

		//输出安装包制作成功
		Terminal.WriteLine(CommandOutletColor.DarkGreen, string.Format(Properties.Resources.PackageGeneratedSuccessfully_Message, Path.Combine(output, package.FileName)));
		//返回安装包的文件路径
		return ValueTask.FromResult<object>(Path.Combine(output, package.FileName));

		string ResolveIdentity(string key) => evaluator.GetOption(key);
	}
	#endregion

	#region 抽象方法
	/// <summary>使用最终选项创建相应格式的安装包。</summary>
	/// <param name="context">本次命令的上下文。</param>
	/// <param name="options">包含最终身份、路径及制作参数的类型化选项。</param>
	/// <returns>创建的安装包；返回空值时不继续制作。</returns>
	protected abstract TPackage CreatePackage(CommandContext context, PackageOptions options);
	#endregion

	#region 配置方法
	protected static void Configure(Package package, CommandContext context)
	{
		var installPath = package.Options[INSTALL_PATH_OPTION];

		if(!string.IsNullOrEmpty(installPath))
			package.InstallPath = installPath.Trim();
	}
	#endregion

	#region 私有方法
	internal static TemplateEvaluator CreateEvaluator(CommandContext context, string directory = null)
	{
		var evaluator = Utility.CreateEvaluator(context, directory);

		//身份只由显式选项与源版本文件决定，环境和 .env 可作为选项引用的变量。
		foreach(var option in new[] { NAME_OPTION, EDITION_OPTION, VERSION_OPTION })
		{
			var value = context.Options.GetValue<string>(option, null);
			if(!string.IsNullOrWhiteSpace(value))
				evaluator.SetVariable(option, value);
			else
				evaluator.SetVariable(option, null);
		}

		return evaluator;
	}
	#endregion
}
