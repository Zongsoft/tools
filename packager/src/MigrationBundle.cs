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
using System.Collections.Generic;
using System.Buffers.Binary;
using System.Text.RegularExpressions;

using Zongsoft.Tools.Packager.Migration;

namespace Zongsoft.Tools.Packager;

/// <summary>Collects the complete native migrator publication for the target runtime.</summary>
public sealed class MigrationBundle : IDisposable
{
	#region 常量定义
	private const string RUNNER = "Zongsoft.Tools.Packager.Migrator";
	#endregion

	#region 成员字段
	private readonly string _temporary = Path.Combine(Path.GetTempPath(), "zongsoft-migration-" + Guid.NewGuid().ToString("N"));
	#endregion

	#region 构造函数
	private MigrationBundle() => Directory.CreateDirectory(_temporary);
	#endregion

	#region 公共方法
	public static MigrationBundle Attach(Package package, MigrationPlan plan, string runtimeDirectory = null)
	{
		if(package.Platform != Platform.Linux || package.Architecture is not (System.Runtime.InteropServices.Architecture.X64 or System.Runtime.InteropServices.Architecture.Arm64))
			throw new InvalidOperationException(Properties.Resources.MigrationPlatformInvalid);

		if(!Regex.IsMatch(package.Framework ?? "", @"^net(8|9|10)\.0$"))
			throw new InvalidOperationException(Properties.Resources.MigrationFrameworkInvalid);

		if(!Regex.IsMatch(package.PackageName, @"^[A-Za-z0-9][A-Za-z0-9._+-]*$"))
			throw new InvalidOperationException(Properties.Resources.MigrationIdentityInvalid);

		if(!Regex.IsMatch(package.InstallPath, @"^/(?:[A-Za-z0-9._+-]+/)*[A-Za-z0-9._+-]+$") || package.InstallPath.Split('/').Any(part => part is "." or ".."))
			throw new InvalidOperationException(Properties.Resources.MigrationInstallPathInvalid);

		foreach(var entry in package.Entries)
		{
			var prefix = entry.Rooted ? package.InstallPath.TrimStart('/') + "/" : string.IsNullOrEmpty(package.EntryPrefix) ? "" : package.EntryPrefix.Trim('/') + "/";
			if(entry.EntryName == prefix + ".migration" || entry.EntryName.StartsWith(prefix + ".migration/", StringComparison.Ordinal))
				throw new InvalidOperationException(string.Format(Properties.Resources.MigrationPathReserved, entry.EntryName));
		}

		package.Migration = plan;
		var bundle = new MigrationBundle();

		try
		{
			bundle.AddRuntime(package, runtimeDirectory ?? Path.Combine(AppContext.BaseDirectory, ".migrator"));
			foreach(var script in plan.Tasks.SelectMany(task => task.Scripts))
				bundle.AddText(package, script.Path, script.Content, Utility.Unix.Mode644, false);

			bundle.AddText(package, ".migration/migration.json", plan.Serialize(), UnixFileMode.UserRead | UnixFileMode.UserWrite);
			bundle.AddText(package, ".migration/id", plan.Fingerprint(), Utility.Unix.Mode644);
			bundle.AddText(package, ".migration/migrate.sh", $$"""
				#!/bin/sh
				set -eu
				BASE_DIR=$(CDPATH= cd -- "$(dirname -- "$0")" && pwd)
				if [ "${1:-apply}" = check ]; then
					if cmp -s {{Quote(StateDirectory(package) + "/ready")}} "$BASE_DIR/id"; then exit 0; fi
					{{MessageScript("MigrationNotCompleted")}}
					exit 1
				fi
				exec "$BASE_DIR/{{RUNNER}}" "${1:-apply}" "$BASE_DIR/migration.json" {{Quote(StateDirectory(package))}}
				""", Utility.Unix.Mode755);

			return bundle;
		}
		catch { bundle.Dispose(); throw; }
	}
	#endregion

	#region 私有方法
	private void AddRuntime(Package package, string directory)
	{
		directory = Path.Combine(directory, package.Runtime);
		var executable = Path.Combine(directory, RUNNER);
		if(!File.Exists(executable))
			throw new FileNotFoundException(Properties.Resources.MigrationRuntimeMissing, executable);

		// Reject wrong-platform artifacts before writing an unusable package.
		using(var stream = File.OpenRead(executable))
		{
			Span<byte> header = stackalloc byte[20];
			if(stream.Read(header) != header.Length || header[0] != 0x7F || header[1] != 'E' || header[2] != 'L' || header[3] != 'F' ||
				header[4] != 2 || header[5] != 1 || BinaryPrimitives.ReadUInt16LittleEndian(header[18..]) != (package.Runtime == "linux-x64" ? 62 : 183))
				throw new InvalidDataException(string.Format(Properties.Resources.MigrationRuntimeInvalid, RUNNER, package.Runtime));
		}

		foreach(var path in Directory.EnumerateFiles(directory, "*", SearchOption.AllDirectories).OrderBy(path => path, StringComparer.Ordinal))
		{
			if(Path.GetExtension(path) is ".pdb" or ".dbg" || path.EndsWith(".dwo", StringComparison.Ordinal))
				continue;

			var relative = Path.GetRelativePath(directory, path).Replace('\\', '/');
			var mode = relative == RUNNER ? Utility.Unix.Mode755 : Utility.Unix.Mode644;

			package.Entries.AddGenerated(path, ".migration/" + relative, mode);
		}
	}

	private void AddText(Package package, string name, string text, UnixFileMode mode, bool normalize = true)
	{
		var path = Path.Combine(_temporary, Guid.NewGuid().ToString("N"));
		File.WriteAllText(path, normalize ? text.ReplaceLineEndings("\n") : text);
		package.Entries.AddGenerated(path, name, mode);
	}
	#endregion

	#region 脚本方法
	public static string StateDirectory(Package package) => "/var/lib/" + package.PackageName + "/packager";
	internal static string Quote(string value) => "'" + value.Replace("'", "'\"'\"'") + "'";

	private static string MessageScript(string name) => $$$$"""
		case "${LC_ALL:-${LC_MESSAGES:-${LANG:-}}}" in
			zh*) printf '%s\n' {{{{Quote(Properties.Resources.ResourceManager.GetString(name, System.Globalization.CultureInfo.GetCultureInfo("zh-Hans")))}}}} ;;
			*) printf '%s\n' {{{{Quote(Properties.Resources.ResourceManager.GetString(name, System.Globalization.CultureInfo.InvariantCulture))}}}} ;;
		esac >&2
		""";

	internal static string ApplyScript(Package package) => package.Migration == null ? null :
		$"sh \"$PACK_INSTALL_PATH/.migration/migrate.sh\" apply";

	internal static string ContextScript(Package package) => package.Migration == null ? null : $$"""
		PACK_INSTALL_PATH=${TARGET:-{{Quote(package.InstallPath)}}}
		export PACK_INSTALL_PATH
		if [ "$PACK_INSTALL_PATH" != {{Quote(package.InstallPath)}} ]; then
			{{MessageScript("MigrationInstallPathFixed")}}
			exit 1
		fi
		""";

	internal static string InvalidateScript(Package package) => package.Migration == null ? null : $"rm -f {Quote(StateDirectory(package) + "/ready")}";
	#endregion

	#region 释放资源
	public void Dispose()
	{
		if(Directory.Exists(_temporary))
			Directory.Delete(_temporary, true);
	}
	#endregion
}
