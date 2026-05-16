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
				WriteTarEntry(writer, entry, ".install/root/" + entry.EntryName);
			else
				WriteTarEntry(writer, entry);
		}

		WriteTarScript(writer, ".install/installing.sh", package.Scripts.Installing);
		WriteTarScript(writer, ".install/installed.sh", package.Scripts.Installed);
		WriteTarScript(writer, ".install/uninstalling.sh", package.Scripts.Uninstalling);
		WriteTarScript(writer, ".install/uninstalled.sh", package.Scripts.Uninstalled);
		WriteTarText(writer, "install.sh", CreateInstallScript(package), 0755);
	}

	static void WriteTarEntry(TarWriter writer, Package.Entry item, string name = null)
	{
		var entry = new PaxTarEntry(TarEntryType.RegularFile, name ?? item.EntryName)
		{
			Mode = (UnixFileMode)item.Mode,
			ModificationTime = DateTimeOffset.FromUnixTimeSeconds(item.ModifiedTime),
			DataStream = File.OpenRead(item.Source),
		};

		writer.WriteEntry(entry);
		entry.DataStream.Dispose();
	}

	static void WriteTarScript(TarWriter writer, string name, string script)
	{
		if(string.IsNullOrWhiteSpace(script))
			return;

		WriteTarText(writer, name, "#!/bin/sh" + Environment.NewLine + "set -e" + Environment.NewLine + script.Trim() + Environment.NewLine, 0755);
	}

	static void WriteTarText(TarWriter writer, string name, string text, int mode)
	{
		var data = Encoding.UTF8.GetBytes(text ?? string.Empty);
		var entry = new PaxTarEntry(TarEntryType.RegularFile, name)
		{
			Mode = (UnixFileMode)mode,
			ModificationTime = DateTimeOffset.UtcNow,
			DataStream = new MemoryStream(data),
		};

		writer.WriteEntry(entry);
		entry.DataStream.Dispose();
	}

	static string CreateInstallScript(Package package)
	{
		var installPath = Quote(package.InstallPath);
		var packageName = Quote(package.PackageName);
		var rootEntries = package.Entries.Where(entry => entry.Rooted).ToArray();
		var rootInstallScript = CreateRootInstallScript(rootEntries);
		var rootUninstallScript = CreateRootUninstallScript(rootEntries);

		return $$"""
			#!/bin/sh
			set -e

			SOURCE_DIR=$(CDPATH= cd -- "$(dirname -- "$0")" && pwd)
			INSTALL_PATH=${INSTALL_PATH:-{{installPath}}}
			DESTDIR=${DESTDIR:-}
			TARGET="${DESTDIR%/}$INSTALL_PATH"
			export SOURCE_DIR INSTALL_PATH DESTDIR TARGET

			run_hook() {
				if [ -f "$SOURCE_DIR/.install/$1" ]; then
					sh "$SOURCE_DIR/.install/$1"
				fi
			}

			if [ "${1:-install}" = "uninstall" ]; then
				run_hook uninstalling.sh
				rm -rf "$TARGET"
				{{rootUninstallScript}}
				run_hook uninstalled.sh
				echo "Uninstalled {{packageName}} from $TARGET"
				exit 0
			fi

			if [ "$(id -u)" -ne 0 ] && [ -z "$DESTDIR" ] && [ "${INSTALL_PATH#/}" != "$INSTALL_PATH" ]; then
				echo "Installing to $INSTALL_PATH requires root privileges. Re-run with sudo or set DESTDIR." >&2
				exit 1
			fi

			run_hook installing.sh
			mkdir -p "$TARGET"
			(
				cd "$SOURCE_DIR"
				find . -mindepth 1 \( -path './install.sh' -o -path './.install' -o -path './.install/*' \) -prune -o -print |
					tar -cf - -T - |
					tar -xpf - -C "$TARGET"
			)
			{{rootInstallScript}}
			run_hook installed.sh
			echo "Installed {{packageName}} to $TARGET"
			""";

		static string Quote(string value) => string.IsNullOrEmpty(value) ? "''" : $"'{value.Replace("'", "'\"'\"'")}'";
	}

	static string CreateRootInstallScript(Package.Entry[] entries)
	{
		if(entries == null || entries.Length == 0)
			return ":";

		var builder = new StringBuilder();

		builder.AppendLine("if [ -d \"$SOURCE_DIR/.install/root\" ]; then");

		foreach(var entry in entries)
		{
			var path = Quote(entry.EntryName);
			builder.AppendLine($"\troot_source=\"$SOURCE_DIR/.install/root/{path}\"");
			builder.AppendLine($"\troot_target=\"${{DESTDIR%/}}/{path}\"");
			builder.AppendLine("\tinstall -d \"$(dirname -- \"$root_target\")\"");
			builder.AppendLine($"\tinstall -m {GetInstallMode(entry.Mode)} \"$root_source\" \"$root_target\"");
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
	static string GetInstallMode(int mode) => mode <= 511 ? Convert.ToString(mode, 8).PadLeft(4, '0') : mode.ToString("0000");
}
