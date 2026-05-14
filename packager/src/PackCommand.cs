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

	#region 抽象属性
	protected abstract string Extension { get; }
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

		if(platform != Platform.Windows && platform != Platform.Linux)
			throw new CommandOptionValueException(PLATFORM_OPTION, platform.ToString());

		var runtime = Utility.GetRuntimeIdentifier(platform, architecture);
		var packageName = Package.GetPackageName(name, edition);
		var installPath = $"{context.Options.GetValue(INSTALL_PATH_OPTION, DEFAULT_INSTALL_PATH)}/{packageName}";
		var variables = GetVariables(context,
		[
			new("Runtime", runtime),
			new("RuntimeIdentifier", runtime),
			new("Architecture", architecture.ToString().ToLowerInvariant()),
			new("InstallPath", installPath),
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

		source = Path.GetFullPath(source);
		output = GetOutputPath(source, output, name, edition, version, runtime, this.Extension);

		variables[SOURCE_OPTION] = source;
		variables[OUTPUT_OPTION] = output;
		context.Parameters.SetValue("Variables", variables);

		var directory = Path.GetDirectoryName(output);
		if(!string.IsNullOrEmpty(directory))
			Directory.CreateDirectory(directory);

		if(File.Exists(output))
		{
			if(context.Options.Switch(OVERWRITE_OPTION))
				File.Delete(output);
			else
				throw new IOException($"The output file '{output}' already exists.");
		}

		Terminal.WriteLine(CommandOutletColor.DarkCyan, $"Installing package generation in progress, please wait...");
		Terminal.WriteLine();

		var package = this.CreatePackage(context, variables);
		if(package == null)
			return ValueTask.FromResult<object>(null);

		var daemonPath = GetDaemonPath(context, source, variables);
		var daemonEntryName = daemonPath == null ? null : GetDaemonEntryName(source, daemonPath);
		package.Scripts = GetScripts(context, source, variables, package, daemonPath, daemonEntryName);
		var entries = GetEntries(source, context.Arguments, variables, package.EntryPrefix);

		if(daemonPath != null)
			AddFile(entries, new HashSet<string>(entries.ConvertAll(entry => entry.EntryName), StringComparer.Ordinal), daemonPath, daemonEntryName, package.EntryPrefix);

		if(entries.Count == 0)
		{
			Terminal.WriteLine(CommandOutletColor.Red, $"The source directory '{source}' does not contain any package entries.");
			return ValueTask.FromResult<object>(null);
		}

		package.Entries = [.. entries];
		package.Pack(output);

		Terminal.WriteLine(CommandOutletColor.DarkGreen, string.Format(Properties.Resources.PackageGeneratedSuccessfully_Message, output));
		return ValueTask.FromResult<object>(output);
	}
	#endregion

	#region 抽象方法
	protected abstract TPackage CreatePackage(CommandContext context, IDictionary<string, string> variables);
	#endregion

	#region 条目方法
	static List<Package.Entry> GetEntries(string source, IReadOnlyCollection<string> arguments, IDictionary<string, string> variables, string prefix)
	{
		var entries = new List<Package.Entry>();
		var names = new HashSet<string>(StringComparer.Ordinal);

		if(arguments == null || arguments.Count == 0)
		{
			foreach(var file in Directory.GetFiles(source, "*", SearchOption.AllDirectories))
				AddEntry(entries, names, source, file, Path.GetRelativePath(source, file), prefix);

			return entries;
		}

		foreach(var argument in arguments)
		{
			if(!Normalizer.Normalize(argument, variables, out var text))
				continue;

			var index = text.LastIndexOf(':');

			if(OperatingSystem.IsWindows() && index == 1)
				index = -1;

			var path = index > 0 ? text[..index].Trim() : text;
			var alias = index > 0 ? text[(index + 1)..].Trim() : null;

			AddEntry(entries, names, source, path, alias, prefix);
		}

		return entries;
	}

	static void AddEntry(List<Package.Entry> entries, ISet<string> names, string source, string path, string alias, string prefix)
	{
		if(alias != null)
			alias = alias
				.Trim('~')
				.Trim(Path.DirectorySeparatorChar)
				.Trim(Path.AltDirectorySeparatorChar);
		else if(IsExternal(source, path))
			alias = string.Empty;

		if(!Path.IsPathFullyQualified(path))
			path = Path.Combine(source, path);

		if(path.Contains('*') || path.Contains('?'))
		{
			var working = Path.GetDirectoryName(path);
			var pattern = Path.GetFileName(path);

			if(string.IsNullOrEmpty(working) || !Directory.Exists(working))
			{
				Terminal.WriteLine(CommandOutletColor.DarkYellow, $"[Warn] The source path '{path}' does not exist.");
				return;
			}

			alias ??= Path.GetRelativePath(source, working);

			if(alias == "." || alias.StartsWith(".."))
				alias = string.Empty;

			foreach(var file in Directory.GetFiles(working, pattern))
				AddFile(entries, names, file, Path.Combine(alias, Path.GetFileName(file)), prefix);

			foreach(var directory in Directory.GetDirectories(working, pattern))
				AddDirectory(entries, names, source, directory, Path.Combine(alias, Path.GetFileName(directory)), prefix);
		}
		else
		{
			alias ??= Path.GetRelativePath(source, path);

			if(alias == "." || alias.StartsWith(".."))
				alias = string.Empty;

			if(File.Exists(path))
				AddFile(entries, names, path, alias, prefix);
			else if(Directory.Exists(path))
				AddDirectory(entries, names, source, path, alias, prefix);
			else
				Terminal.WriteLine(CommandOutletColor.DarkYellow, $"[Warn] The source path '{path}' does not exist.");
		}
	}

	static void AddDirectory(List<Package.Entry> entries, ISet<string> names, string source, string path, string alias, string prefix)
	{
		foreach(var file in Directory.GetFiles(path))
			AddFile(entries, names, file, Path.Combine(alias, Path.GetFileName(file)), prefix);

		foreach(var directory in Directory.GetDirectories(path))
			AddDirectory(entries, names, source, directory, Path.Combine(alias, Path.GetFileName(directory)), prefix);
	}

	static void AddFile(List<Package.Entry> entries, ISet<string> names, string source, string entryName, string prefix)
	{
		if(string.IsNullOrEmpty(entryName))
			entryName = Path.GetFileName(source);
		else
		{
			var filename = Path.GetFileName(entryName);

			if(string.IsNullOrEmpty(filename) || filename == ".")
				entryName = Path.Combine(Path.GetDirectoryName(entryName), Path.GetFileName(source));
		}

		entryName = Utility.NormalizePath(Path.Combine(prefix ?? string.Empty, entryName));

		if(!names.Add(entryName))
		{
			Terminal.WriteLine(CommandOutletColor.DarkYellow, $"[Warn] The source file '{source}' conflicts with an existing package entry '{entryName}'.");
			return;
		}

		var file = new FileInfo(source);
		entries.Add(new Package.Entry(source, entryName, file.Length, GetUnixTime(file.LastWriteTimeUtc), GetFileMode(source)));
	}

	static bool IsExternal(string source, string path)
	{
		return Path.IsPathFullyQualified(path) &&
			!Path.GetFullPath(path).StartsWith(source, GetComparison());

		static StringComparison GetComparison() => OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
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
		return IsExternal(source, daemon) ? Path.GetFileName(daemon) : Utility.NormalizePath(Path.GetRelativePath(source, daemon));
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

	static string GetOutputPath(string source, string output, string name, string edition, Version version, string runtime, string extension)
	{
		if(string.IsNullOrEmpty(output))
			output = Path.Combine(source, GetFileName(name, edition, version, runtime) + extension);
		else
		{
			if(!Path.IsPathFullyQualified(output))
				output = Path.Combine(source, output);

			if(Directory.Exists(output) || EndsWithDirectorySeparator(output) || string.IsNullOrEmpty(Path.GetExtension(output)))
				output = Path.Combine(output, GetFileName(name, edition, version, runtime) + extension);
			else if(!HasExtension(output, extension))
				output += extension;
		}

		return Path.GetFullPath(output);
	}

	static string GetFileName(string name, string edition, Version version, string runtime) => string.IsNullOrEmpty(edition) ?
		$"{name}@{version}_{runtime}" :
		$"{name}-{edition}@{version}_{runtime}";

	static bool HasExtension(string path, string extension)
	{
		return path.EndsWith(extension, OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);
	}

	static bool EndsWithDirectorySeparator(string path)
	{
		return path.EndsWith(Path.DirectorySeparatorChar) || path.EndsWith(Path.AltDirectorySeparatorChar);
	}

	static int GetFileMode(string path)
	{
		if(!OperatingSystem.IsWindows())
		{
			var mode = (int)(File.GetUnixFileMode(path) & (UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute | UnixFileMode.GroupRead | UnixFileMode.GroupWrite | UnixFileMode.GroupExecute | UnixFileMode.OtherRead | UnixFileMode.OtherWrite | UnixFileMode.OtherExecute));

			if(mode > 0)
				return mode;
		}

		return IsExecutable(path) ? 0755 : 0644;
	}

	static bool IsExecutable(string path)
	{
		var extension = Path.GetExtension(path);
		return string.IsNullOrEmpty(extension) ||
			extension.Equals(".sh", StringComparison.OrdinalIgnoreCase) ||
			extension.Equals(".dll", StringComparison.OrdinalIgnoreCase) ||
			extension.Equals(".exe", StringComparison.OrdinalIgnoreCase);
	}

	static long GetUnixTime(DateTime value) => new DateTimeOffset(value).ToUnixTimeSeconds();
	#endregion
}
