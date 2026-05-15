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

	protected const string DEFAULT_INSTALL_PATH = "/usr/local";
	protected const string DEFAULT_MAINTAINER = "Zongsoft Studio <zongsoft@gmail.com>";
	protected const string DEFAULT_URL = "https://github.com/Zongsoft";
	#endregion

	#region 执行方法
	protected override ValueTask<object> OnExecuteAsync(CommandContext context, CancellationToken cancellation)
	{
		var name = context.Options.GetValue<string>(NAME_OPTION);
		var edition = context.Options.GetValue<string>(EDITION_OPTION);
		var version = context.Options.GetValue<Version>(VERSION_OPTION);
		var platform = context.Options.GetValue<Platform>(PLATFORM_OPTION);
		var architecture = context.Options.GetValue<Architecture>(ARCHITECTURE_OPTION);

		if(version.IsZero())
		{
			Terminal.WriteLine(CommandOutletColor.Red, $"The version number is invalid.");
			return ValueTask.FromResult<object>(null);
		}

		var runtime = Utility.GetRuntimeIdentifier(platform, architecture);
		var variables = GetVariables(context,
		[
			new("Runtime", runtime),
			new("RuntimeIdentifier", runtime),
			new("Architecture", architecture.ToString().ToLowerInvariant()),
		]);

		if(!Normalizer.Normalize(context.Options.GetValue<string>(SOURCE_OPTION), variables, out var source))
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

		if(!Normalizer.Normalize(context.Options.GetValue<string>(OUTPUT_OPTION), variables, out var output))
			return ValueTask.FromResult<object>(null);

		variables[SOURCE_OPTION] = source = Path.GetFullPath(source);
		variables[OUTPUT_OPTION] = output = Path.GetFullPath(Path.Combine(source, output));

		//确保输出目录存在
		if(!Directory.Exists(output))
			Directory.CreateDirectory(output);

		Terminal.WriteLine(CommandOutletColor.DarkCyan, $"Installing package generation in progress, please wait...");
		Terminal.WriteLine();

		var package = this.CreatePackage(context, variables);
		if(package == null)
			return ValueTask.FromResult<object>(null);

		package.Entries.Load(source, context.Arguments, variables);
		if(package.Entries.Count == 0)
		{
			Terminal.WriteLine(CommandOutletColor.Red, $"The source directory '{source}' does not contain any package entries.");
			return ValueTask.FromResult<object>(null);
		}

		var daemonPath = GetDaemonPath(context, source, variables);
		var daemonEntryName = daemonPath == null ? null : GetDaemonEntryName(source, daemonPath);
		package.Scripts = GetScripts(context, source, variables, package, daemonPath, daemonEntryName);

		//if(daemonPath != null)
		//	AddFile(
		//		package.Entries,
		//		new HashSet<string>(entries.ConvertAll(entry => entry.EntryName), StringComparer.Ordinal),
		//		daemonPath,
		//		daemonEntryName,
		//		package.EntryPrefix);

		package.Pack(output, context.Options.Switch(OVERWRITE_OPTION));

		Terminal.WriteLine(CommandOutletColor.DarkGreen, string.Format(Properties.Resources.PackageGeneratedSuccessfully_Message, output));
		return ValueTask.FromResult<object>(output);
	}
	#endregion

	#region 抽象方法
	protected abstract TPackage CreatePackage(CommandContext context, IDictionary<string, string> variables);
	#endregion

	#region 配置方法
	protected static void Configure(Package package, CommandContext context, IDictionary<string, string> variables)
	{
		package.Framework = context.Options.GetValue<string>(FRAMEWORK_OPTION);
		package.InstallPath = Normalizer.NormalizeValue(context.Options.GetValue<string>(INSTALL_PATH_OPTION), variables);
		package.Title = Normalizer.NormalizeValue(context.Options.GetValue<string>(TITLE_OPTION), variables);
		package.Summary = Normalizer.NormalizeText(context.Options.GetValue<string>(SUMMARY_OPTION), variables);
		package.Description = Normalizer.NormalizeText(context.Options.GetValue<string>(DESCRIPTION_OPTION), variables);
		package.Maintainer = Normalizer.NormalizeValue(context.Options.GetValue<string>(MAINTAINER_OPTION), variables, DEFAULT_MAINTAINER);
		package.License = Normalizer.NormalizeValue(context.Options.GetValue<string>(LICENSE_OPTION), variables);
		package.Url = Normalizer.NormalizeValue(context.Options.GetValue<string>(URL_OPTION), variables, DEFAULT_URL);
		package.Category = Normalizer.NormalizeValue(context.Options.GetValue<string>(CATEGORY_OPTION), variables);
		package.Dependencies = Normalizer.NormalizeList(context.Options.GetValue<string>(DEPENDENCIES_OPTION), variables);
	}
	#endregion

	#region 脚本方法
	static string GetDaemonPath(CommandContext context, string source, IDictionary<string, string> variables)
	{
		if(!context.Options.TryGetValue<string>(DAEMON_OPTION, out var daemon) || string.IsNullOrWhiteSpace(daemon))
			return null;

		if(!Normalizer.Normalize(daemon, variables, out daemon))
			return null;

		if(!Path.IsPathFullyQualified(daemon))
			daemon = Path.Combine(source, daemon);

		if(!File.Exists(daemon))
			throw new FileNotFoundException($"The daemon service file '{daemon}' does not exist.", daemon);

		return Path.GetFullPath(daemon);
	}

	static string GetDaemonEntryName(string source, string daemon)
	{
		return Utility.IsExternal(source, daemon) ? Path.GetFileName(daemon) : Utility.NormalizePath(Path.GetRelativePath(source, daemon));
	}

	static Package.InstallScripts GetScripts(CommandContext context, string source, IDictionary<string, string> variables, Package metadata, string daemon, string daemonEntryName)
	{
		var scripts = new Package.InstallScripts(
			ReadScript(context, SCRIPT_INSTALLING_OPTION, source, variables),
			ReadScript(context, SCRIPT_INSTALLED_OPTION, source, variables),
			ReadScript(context, SCRIPT_UNINSTALLING_OPTION, source, variables),
			ReadScript(context, SCRIPT_UNINSTALLED_OPTION, source, variables));

		if(daemon == null)
			return scripts;

		if(metadata.Platform != Platform.Linux)
			Terminal.WriteLine(CommandOutletColor.DarkYellow, $"[Warn] The '{DAEMON_OPTION}' option is intended for Linux systemd services.");

		var service = Path.GetFileName(daemon);
		var servicePath = $"{metadata.InstallPath}/{daemonEntryName}";
		var serviceLink = $"/etc/systemd/system/{service}";

		var installing = $$"""
			if command -v systemctl >/dev/null 2>&1; then
				systemctl stop '{{service}}' >/dev/null 2>&1 || true
			fi
			""";

		var installed = $$"""
			install -d /etc/systemd/system
			ln -sfn '{{servicePath}}' '{{serviceLink}}'
			if command -v systemctl >/dev/null 2>&1; then
				systemctl daemon-reload >/dev/null 2>&1 || true
				systemctl enable '{{service}}' >/dev/null 2>&1 || true
			fi
			""";

		var uninstalling = $$"""
			if command -v systemctl >/dev/null 2>&1; then
				systemctl stop '{{service}}' >/dev/null 2>&1 || true
			fi
			""";

		var uninstalled = $$"""
			rm -f '{{serviceLink}}'
			if command -v systemctl >/dev/null 2>&1; then
				systemctl daemon-reload >/dev/null 2>&1 || true
			fi
			rm -rf '{{metadata.InstallPath}}'
			""";

		return new(
			CombineScript(installing, scripts.Installing),
			CombineScript(installed, scripts.Installed),
			CombineScript(uninstalling, scripts.Uninstalling),
			CombineScript(uninstalled, scripts.Uninstalled));
	}

	static string ReadScript(CommandContext context, string option, string source, IDictionary<string, string> variables)
	{
		if(!context.Options.TryGetValue<string>(option, out var path) || string.IsNullOrWhiteSpace(path))
			return null;

		if(!Normalizer.Normalize(path, variables, out path))
			return null;

		if(!Path.IsPathFullyQualified(path))
			path = Path.Combine(source, path);

		if(!File.Exists(path))
			throw new FileNotFoundException($"The script file '{path}' does not exist.", path);

		return File.ReadAllText(path);
	}

	static string CombineScript(string first, string second)
	{
		if(string.IsNullOrWhiteSpace(first))
			return string.IsNullOrWhiteSpace(second) ? null : second.Trim();
		if(string.IsNullOrWhiteSpace(second))
			return first.Trim();

		return first.Trim() + Environment.NewLine + Environment.NewLine + second.Trim();
	}
	#endregion

	#region 辅助方法
	static Dictionary<string, string> GetVariables(CommandContext context, params KeyValuePair<string, string>[] options)
	{
		var variables = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

		foreach(System.Collections.DictionaryEntry variable in Environment.GetEnvironmentVariables())
			variables[variable.Key.ToString()] = variable.Value?.ToString();

		foreach(var option in context.Options)
		{
			if(Normalizer.Normalize(option.Value?.ToString(), variables, out var value))
				variables[option.Key] = value;
		}

		foreach(var option in options)
		{
			if(Normalizer.Normalize(option.Value, variables, out var value))
				variables[option.Key] = value;
		}

		return variables;
	}
	#endregion
}
