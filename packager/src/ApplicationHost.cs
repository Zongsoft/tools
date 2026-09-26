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
using System.Text.RegularExpressions;

namespace Zongsoft.Tools.Packager;

internal sealed record ApplicationHost(ApplicationHost.HostKind Kind, string Entry = null, string ServiceName = null, string ServiceSource = null, string Listen = null)
{
	#region 公共方法
	internal static ApplicationHost Resolve(Package package)
	{
		var variables = package.Variables;
		if(variables.Daemon.Disabled)
			return new(HostKind.None);

		var source = variables.Source;
		var identifier = string.IsNullOrEmpty(variables.Daemon.Identifier) ? package.Name.ToLowerInvariant() : variables.Daemon.Identifier;
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
			if(package.Migrator != null)
				throw new InvalidOperationException(Properties.Resources.MigrationHostRequired_Message);

			Dumper.HostLocateFailed();
			return new(HostKind.None);
		}

		var listen = variables.Listen;
		if(ushort.TryParse(listen, out var port))
			listen = $"http://127.0.0.1:{port}";

		return new(HostKind.Generated, entry, identifier, Listen: listen);
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

		if(string.IsNullOrWhiteSpace(package.Variables.Compilation) || string.IsNullOrWhiteSpace(package.Variables.Framework))
			return null;

		var directory = Path.Combine(source, "bin", package.Variables.Compilation, package.Variables.Framework);
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
