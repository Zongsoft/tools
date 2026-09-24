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
using System.Formats.Tar;
using System.IO.Compression;
using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace Zongsoft.Tools.Packager;

/// <summary>定位并集成独立升迁工具的既有产物。</summary>
public sealed class Migrator
{
	#region 构造函数
	private Migrator(string archive, string script)
	{
		this.Archive = archive;
		this.Script = script;
	}
	#endregion

	#region 公共属性
	public string Archive { get; }
	public string Script { get; }
	#endregion

	#region 公共方法
	public static Migrator Load(Package package, string name)
	{
		ArgumentNullException.ThrowIfNull(package);
		if(string.IsNullOrWhiteSpace(name))
			throw new ArgumentException(Properties.Resources.MigratorNameInvalid_Message, nameof(name));

		name = Normalizer.Normalize(name, package.Variables, null);
		var searchParents = name.IndexOfAny(['/', '\\']) < 0;
		var path = Path.GetFullPath(Path.Combine(package.Variables.Source, name));
		name = Path.GetFileName(path);

		if(!Regex.IsMatch(name, @"^[A-Za-z0-9][A-Za-z0-9._+-]*$") ||
			name.EndsWith(".tar.gz", StringComparison.OrdinalIgnoreCase) || name.EndsWith(".sh", StringComparison.OrdinalIgnoreCase) || name.EndsWith(".cmd", StringComparison.OrdinalIgnoreCase))
			throw new ArgumentException(Properties.Resources.MigratorNameInvalid_Message, nameof(name));

		if(!HasSuffix(name))
			name += "-migrate";

		if(package.Platform != Platform.Linux || package.Architecture is not (System.Runtime.InteropServices.Architecture.X64 or System.Runtime.InteropServices.Architecture.Arm64))
			throw new InvalidOperationException(Properties.Resources.MigrationPlatformInvalid_Message);

		if(!Regex.IsMatch(package.PackageName, @"^[A-Za-z0-9][A-Za-z0-9._+-]*$"))
			throw new InvalidOperationException(Properties.Resources.MigrationIdentityInvalid_Message);

		if(!Regex.IsMatch(package.InstallPath, @"^/(?:[A-Za-z0-9._+-]+/)*[A-Za-z0-9._+-]+$") || package.InstallPath.Split('/').Any(part => part is "." or ".."))
			throw new InvalidOperationException(Properties.Resources.MigrationInstallPathInvalid_Message);

		var prefix = name + (string.IsNullOrEmpty(package.Edition) ? "" : "-" + package.Edition) + "@" + package.Version + "_" + package.Runtime;
		var migrator = Locate(Path.GetDirectoryName(path), prefix, searchParents);
		Validate(migrator.Archive, package.Runtime);
		return migrator;
	}

	public void Attach(Package package)
	{
		foreach(var entry in package.Entries)
		{
			var prefix = entry.Rooted ? package.InstallPath.TrimStart('/') + "/" : string.IsNullOrEmpty(package.EntryPrefix) ? "" : package.EntryPrefix.Trim('/') + "/";
			if(entry.EntryName == prefix + ".migration" || entry.EntryName.StartsWith(prefix + ".migration/", StringComparison.Ordinal))
				throw new InvalidOperationException(string.Format(Properties.Resources.MigrationPathReserved_Message, entry.EntryName));
		}

		package.Entries.AddGenerated(this.Archive, ".migration/" + Path.GetFileName(this.Archive), UnixFileMode.UserRead | UnixFileMode.UserWrite);
		package.Entries.AddGenerated(this.Script, ".migration/" + Path.GetFileName(this.Script), Utility.Unix.Mode755);
	}
	#endregion

	#region 私有方法
	private static Migrator Locate(string directory, string prefix, bool searchParents)
	{
		var archiveName = prefix + ".tar.gz";
		var scriptName = prefix + ".sh";
		var searched = new List<string>();

		for(var current = new DirectoryInfo(directory); current != null; current = searchParents ? current.Parent : null)
		{
			searched.Add(current.FullName);
			var archive = Path.Combine(current.FullName, archiveName);
			var script = Path.Combine(current.FullName, scriptName);
			var hasArchive = File.Exists(archive);
			var hasScript = File.Exists(script);

			if(hasArchive && hasScript)
				return new(archive, script);

			if(hasArchive || hasScript || !searchParents)
			{
				var missing = hasArchive ? script : archive;
				throw new FileNotFoundException(string.Format(Properties.Resources.MigratorArtifactMissing_Message, missing), missing);
			}
		}

		throw new FileNotFoundException(string.Format(Properties.Resources.MigratorArtifactsNotFound_Message,
			archiveName, scriptName, string.Join(Environment.NewLine, searched)), Path.Combine(directory, archiveName));
	}

	private static void Validate(string archive, string expectedRuntime)
	{
		using var stream = File.OpenRead(archive);
		using var gzip = new GZipStream(stream, CompressionMode.Decompress);
		using var reader = new TarReader(gzip);

		if(reader.GetNextEntry() is not PaxGlobalExtendedAttributesTarEntry metadata ||
			!metadata.GlobalExtendedAttributes.TryGetValue("Runtime", out var runtime) || runtime != expectedRuntime ||
			!metadata.GlobalExtendedAttributes.TryGetValue("Migrator", out var generator) || string.IsNullOrWhiteSpace(generator))
			throw new InvalidDataException(string.Format(Properties.Resources.MigratorMetadataInvalid_Message, archive, expectedRuntime));
	}

	private static bool HasSuffix(string name) =>
		name.EndsWith("-migrate", StringComparison.OrdinalIgnoreCase) || name.EndsWith("-migration", StringComparison.OrdinalIgnoreCase) ||
		name.EndsWith(".migrate", StringComparison.OrdinalIgnoreCase) || name.EndsWith(".migration", StringComparison.OrdinalIgnoreCase);
	#endregion

	#region 脚本方法
	public static string StateDirectory(Package package) => "/var/lib/" + package.PackageName + "/packager";
	internal static string Quote(string value) => "'" + value.Replace("'", "'\"'\"'") + "'";
	private static string MessageScript(Func<string> message)
	{
		var culture = System.Globalization.CultureInfo.CurrentUICulture;
		try
		{
			//同步读取两种资源后恢复当前执行上下文，不修改生成资源类的全局 Culture。
			System.Globalization.CultureInfo.CurrentUICulture = System.Globalization.CultureInfo.GetCultureInfo("zh-Hans");
			var chinese = message();
			System.Globalization.CultureInfo.CurrentUICulture = System.Globalization.CultureInfo.InvariantCulture;
			return $$$$"""
				case "${LC_ALL:-${LC_MESSAGES:-${LANG:-}}}" in
					zh*) printf '%s\n' {{{{Quote(chinese)}}}} ;;
					*) printf '%s\n' {{{{Quote(message())}}}} ;;
				esac >&2
				""";
		}
		finally { System.Globalization.CultureInfo.CurrentUICulture = culture; }
	}

	internal static string ApplyScript(Package package) => package.Migrator == null ? null :
		$"sh \"$PACK_INSTALL_PATH/.migration/{Path.GetFileName(package.Migrator.Script)}\" apply {Quote(StateDirectory(package))}";

	internal static string ContextScript(Package package) => package.Migrator == null ? null : $$"""
		PACK_INSTALL_PATH=${TARGET:-{{Quote(package.InstallPath)}}}
		export PACK_INSTALL_PATH
		if [ "$PACK_INSTALL_PATH" != {{Quote(package.InstallPath)}} ]; then
			{{MessageScript(() => Properties.Resources.MigrationInstallPathFixed)}}
			exit 1
		fi
		""";

	internal static string InvalidateScript(Package package) => package.Migrator == null ? null : $"rm -f {Quote(StateDirectory(package) + "/ready")}";
	#endregion
}
