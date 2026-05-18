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
using System.Text;
using System.Linq;
using System.Formats.Tar;
using System.IO.Compression;

namespace Zongsoft.Tools.Packager;

partial class Generator
{
	const string TAR_ROOT_DIRECTORY = ".root";
	const string TAR_ROOT_PREFIX = TAR_ROOT_DIRECTORY + "/";

	public static void Tar(this Package package, string output, bool overwrite)
	{
		using var stream = new FileStream(
			Path.Combine(output, package.FileName),
			overwrite ? FileMode.Create : FileMode.CreateNew,
			FileAccess.Write);

		using var gzip = new GZipStream(stream, CompressionLevel.Optimal);
		using var writer = new TarWriter(gzip, TarEntryFormat.Pax, false);

		foreach(var entry in package.Entries)
		{
			if(entry.Rooted)
				WriteTarEntry(writer, entry, TAR_ROOT_PREFIX + entry.EntryName);
			else
				WriteTarEntry(writer, entry);
		}

		WriteTarText(writer, "install.sh", CreateInstallScript(package), Utility.Unix.Mode755);
		WriteTarText(writer, "uninstall.sh", CreateUninstallScript(package), Utility.Unix.Mode755);

		//生成与包同名的安装器脚本
		WriteInstallerScript(
			Path.Combine(output, GetInstallerFileName(package)),
			CreateInstallerScript(package));

		static string GetInstallerFileName(Package package) => package.FileName.EndsWith(Package.Tar.EXTENSION, StringComparison.OrdinalIgnoreCase) ?
			package.FileName[..^Package.Tar.EXTENSION.Length] + ".sh" :
			Path.ChangeExtension(package.FileName, ".sh");
	}

	static void WriteTarEntry(TarWriter writer, Package.Entry item, string name = null)
	{
		var entry = new PaxTarEntry(TarEntryType.RegularFile, name ?? item.EntryName)
		{
			Mode = item.Mode,
			ModificationTime = DateTimeOffset.FromUnixTimeSeconds(item.ModifiedTime),
			DataStream = File.OpenRead(item.Source),
		};

		writer.WriteEntry(entry);
		entry.DataStream.Dispose();
	}

	static void WriteTarText(TarWriter writer, string name, string text, UnixFileMode mode)
	{
		var data = Encoding.UTF8.GetBytes((text ?? string.Empty).ReplaceLineEndings("\n"));
		var entry = new PaxTarEntry(TarEntryType.RegularFile, name)
		{
			Mode = mode,
			ModificationTime = DateTimeOffset.UtcNow,
			DataStream = new MemoryStream(data),
		};

		writer.WriteEntry(entry);
		entry.DataStream.Dispose();
	}

	static void WriteInstallerScript(string path, string text)
	{
		var data = Encoding.UTF8.GetBytes((text ?? string.Empty).ReplaceLineEndings("\n"));

		using(var stream = new FileStream(path, FileMode.Create, FileAccess.Write))
			stream.Write(data);

		if(!OperatingSystem.IsWindows())
			File.SetUnixFileMode(path, Utility.Unix.Mode755);
	}

	static string CreateInstallerScript(Package package)
	{
		var archiveName = ShellQuote(package.FileName);

		return $$"""
			#!/bin/sh
			set -e

			SOURCE_DIR=$(CDPATH= cd -- "$(dirname -- "$0")" && pwd)
			ARCHIVE="$SOURCE_DIR"/{{archiveName}}

			if [ ! -f "$ARCHIVE" ]; then
				echo "Package archive not found: $ARCHIVE" >&2
				exit 1
			fi

			WORK_DIR=$(mktemp -d)
			cleanup() {
				rm -rf "$WORK_DIR"
			}
			trap cleanup EXIT HUP INT TERM

			tar -xzf "$ARCHIVE" -C "$WORK_DIR"
			if [ ! -x "$WORK_DIR/install.sh" ]; then
				echo "Installer not found in package archive." >&2
				exit 1
			fi

			"$WORK_DIR/install.sh" "$@"
			""";
	}

	static string CreateInstallScript(Package package)
	{
		var installPath = Quote(package.InstallPath);
		var packageName = Quote(package.PackageName);
		var rootEntries = package.Entries.Where(entry => entry.Rooted).ToArray();
		var rootInstallScript = CreateRootInstallScript(rootEntries);
		var installingScript = NormalizeScript(package.Scripts.Installing);
		var installedScript = NormalizeScript(package.Scripts.Installed);

		return $$"""
			#!/bin/sh
			set -e

			SOURCE_DIR=$(CDPATH= cd -- "$(dirname -- "$0")" && pwd)
			INSTALL_PATH=${INSTALL_PATH:-{{installPath}}}
			DESTDIR=${DESTDIR:-}
			TARGET="${DESTDIR%/}$INSTALL_PATH"
			export SOURCE_DIR INSTALL_PATH DESTDIR TARGET

			if [ "$(id -u)" -ne 0 ] && [ -z "$DESTDIR" ] && [ "${INSTALL_PATH#/}" != "$INSTALL_PATH" ]; then
				echo "Installing to $INSTALL_PATH requires root privileges. Re-run with sudo or set DESTDIR." >&2
				exit 1
			fi

			{{installingScript}}
			mkdir -p "$TARGET"
			(
				cd "$SOURCE_DIR"
				find . -mindepth 1 \( -path './install.sh' -o -path './uninstall.sh' -o -path './{{TAR_ROOT_DIRECTORY}}' -o -path './{{TAR_ROOT_DIRECTORY}}/*' \) -prune -o -print |
					tar -cf - -T - |
					tar -xpf - -C "$TARGET"
			)
			install -m 0755 "$SOURCE_DIR/uninstall.sh" "$TARGET/uninstall.sh"
			{{rootInstallScript}}
			{{installedScript}}
			echo "Installed {{packageName}} to $TARGET"
			""";

		static string Quote(string value) => string.IsNullOrEmpty(value) ? "''" : $"'{value.Replace("'", "'\"'\"'")}'";
	}

	static string CreateUninstallScript(Package package)
	{
		var installPath = Quote(package.InstallPath);
		var packageName = Quote(package.PackageName);
		var rootEntries = package.Entries.Where(entry => entry.Rooted).ToArray();
		var rootUninstallScript = CreateRootUninstallScript(rootEntries);
		var uninstallingScript = NormalizeScript(package.Scripts.Uninstalling);
		var uninstalledScript = NormalizeScript(package.Scripts.Uninstalled);

		return $$"""
			#!/bin/sh
			set -e

			SOURCE_DIR=$(CDPATH= cd -- "$(dirname -- "$0")" && pwd)
			INSTALL_PATH=${INSTALL_PATH:-{{installPath}}}
			DESTDIR=${DESTDIR:-}
			TARGET="${DESTDIR%/}$INSTALL_PATH"
			export SOURCE_DIR INSTALL_PATH DESTDIR TARGET

			if [ "$(id -u)" -ne 0 ] && [ -z "$DESTDIR" ] && [ "${INSTALL_PATH#/}" != "$INSTALL_PATH" ]; then
				echo "Uninstalling from $INSTALL_PATH requires root privileges. Re-run with sudo or set DESTDIR." >&2
				exit 1
			fi

			{{uninstallingScript}}
			cd /
			rm -rf "$TARGET"
			{{rootUninstallScript}}
			{{uninstalledScript}}
			echo "Uninstalled {{packageName}} from $TARGET"
			""";

		static string Quote(string value) => string.IsNullOrEmpty(value) ? "''" : $"'{value.Replace("'", "'\"'\"'")}'";
	}

	static string NormalizeScript(string script) => string.IsNullOrWhiteSpace(script) ? ":" : script.Trim().ReplaceLineEndings("\n");

	static string CreateRootInstallScript(Package.Entry[] entries)
	{
		if(entries == null || entries.Length == 0)
			return ":";

		var builder = new StringBuilder();

		builder.AppendLine($"if [ -d \"$SOURCE_DIR/{TAR_ROOT_DIRECTORY}\" ]; then");

		foreach(var entry in entries)
		{
			var path = Quote(entry.EntryName);
			var mode = Convert.ToString((int)entry.Mode, 8).PadLeft(4, '0');
			builder.AppendLine($"\troot_source=\"$SOURCE_DIR/{TAR_ROOT_PREFIX}{path}\"");
			builder.AppendLine($"\troot_target=\"${{DESTDIR%/}}/{path}\"");
			builder.AppendLine("\tinstall -d \"$(dirname -- \"$root_target\")\"");
			builder.AppendLine($"\tinstall -m {mode} \"$root_source\" \"$root_target\"");
		}

		builder.Append("fi");
		return builder.ToString();
	}

	static string CreateRootUninstallScript(Package.Entry[] entries)
	{
		if(entries == null || entries.Length == 0)
			return ":";

		var builder = new StringBuilder();

		foreach(var entry in entries)
			builder.AppendLine($"rm -f \"${{DESTDIR%/}}/{Quote(entry.EntryName)}\"");

		return builder.ToString().TrimEnd();
	}

	static string Quote(string value) => string.IsNullOrEmpty(value) ? string.Empty : value.Replace("\"", "\\\"");
	static string ShellQuote(string value) => string.IsNullOrEmpty(value) ? "''" : $"'{value.Replace("'", "'\"'\"'")}'";
}
