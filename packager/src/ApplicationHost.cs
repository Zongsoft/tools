/*
 *   _____                                ______
 *  /_   /  ____  ____  ____  _________  / __/ /_
 *    / /  / __ \/ __ \/ __ \/ ___/ __ \/ /_/ __/
 *   / /__/ /_/ / / / / /_/ /\_ \/ /_/ / __/ /_
 *  /____/\____/_/ /_/\__  /____/\____/_/  \__/
 *                   /____/
 *
 * Authors:
 *   钟峰(Popeye Zhong) <zongsoft@qq.com>
 *
 * Copyright (c) Zongsoft Studio. All rights reserved.
 * The above copyright notice and this permission notice shall be included in all
 * AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
 *
 *
 * Licensed under the MIT License (see LICENSE).
 */

using System;
using System.IO;
using System.Text.RegularExpressions;

namespace Zongsoft.Tools.Packager;

internal sealed record ApplicationHost(ApplicationHost.HostKind Kind, string Entry = null, string ServiceName = null, string ServiceSource = null)
{
	#region 公共方法
	internal static ApplicationHost Resolve(Package package)
	{
		var options = package.Options;
		if(options.Daemon.Disabled)
			return new(HostKind.None);

		var source = options.Source;
		var identifier = string.IsNullOrEmpty(options.Daemon.Identifier) ? package.Name.ToLowerInvariant() : options.Daemon.Identifier;
		var file = new FileInfo(Path.GetFullPath(Path.Combine(source, identifier)));

		if(file.Exists)
		{
			ValidateServiceName(file.Name);
			return new(HostKind.Existing, ServiceName: file.Name, ServiceSource: file.FullName);
		}

		if(identifier.IndexOfAny(['/', '\\']) >= 0)
			throw new InvalidDataException(string.Format(Properties.Resources.PackageEntryTypeConflict_Message, identifier));

		if(!identifier.EndsWith(".service", StringComparison.Ordinal))
			identifier += ".service";

		ValidateServiceName(identifier);
		var entry = GetEntry(source, package);

		if(entry == null)
		{
			if(package.Migration != null)
				throw new InvalidOperationException(Properties.Resources.MigrationHostRequired_Message);

			Dumper.HostLocateFailed();
			return new(HostKind.None);
		}

		return new(HostKind.Generated, entry, identifier);
	}
	#endregion

	#region 私有方法
	private static void ValidateServiceName(string name)
	{
		if(!Regex.IsMatch(name, @"^[A-Za-z0-9][A-Za-z0-9._@-]*\.service$"))
			throw new InvalidDataException(string.Format(Properties.Resources.PackageEntryTypeConflict_Message, name));
	}

	private static string GetEntry(string source, Package package)
	{
		if(string.IsNullOrWhiteSpace(source) || !Directory.Exists(source))
			return null;

		var path = Path.Combine(source, package.Name + ".dll");
		if(File.Exists(path))
			return Path.GetFileName(path);

		var files = Directory.GetFiles(source, "*.exe", SearchOption.TopDirectoryOnly);
		if(files.Length == 1)
			return Path.GetFileNameWithoutExtension(files[0]) + ".dll";

		if(string.IsNullOrWhiteSpace(package.Options.Compilation) || string.IsNullOrWhiteSpace(package.Options.Framework))
			return null;

		var directory = Path.Combine(source, "bin", package.Options.Compilation, package.Options.Framework);
		path = Path.Combine(directory, package.Name + ".dll");
		if(File.Exists(path))
			return Path.GetFileName(path);

		files = Directory.Exists(directory) ? Directory.GetFiles(directory, "*.exe", SearchOption.TopDirectoryOnly) : [];
		return files.Length == 1 ? Path.GetFileNameWithoutExtension(files[0]) + ".dll" : null;
	}
	#endregion

	#region 嵌套类型
	internal enum HostKind
	{
		None,
		Existing,
		Generated,
	}
	#endregion
}
