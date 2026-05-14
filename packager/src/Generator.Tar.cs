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
using System.Formats.Tar;
using System.IO.Compression;
using System.Collections.Generic;

namespace Zongsoft.Tools.Packager;

partial class Generator
{
	public static void Tar(this Package package, string output)
	{
		using var stream = File.Create(output);
		using var gzip = new GZipStream(stream, CompressionLevel.Optimal);
		using var writer = new TarWriter(gzip, TarEntryFormat.Pax, false);

		foreach(var entry in package.Entries)
			WriteTarEntry(writer, entry);

		WriteTarScript(writer, ".install/installing.sh", package.Scripts.Installing);
		WriteTarScript(writer, ".install/installed.sh", package.Scripts.Installed);
		WriteTarScript(writer, ".install/uninstalling.sh", package.Scripts.Uninstalling);
		WriteTarScript(writer, ".install/uninstalled.sh", package.Scripts.Uninstalled);
		WriteTarText(writer, "install.sh", CreateInstallScript(package), 0755);
	}

	static byte[] CreateDataTarball(IReadOnlyCollection<Package.Entry> entries)
	{
		using var memory = new MemoryStream();
		using(var gzip = new GZipStream(memory, CompressionLevel.Optimal, true))
		using(var writer = new TarWriter(gzip, TarEntryFormat.Pax, true))
		{
			foreach(var entry in entries)
				WriteTarEntry(writer, entry);
		}

		return memory.ToArray();
	}

	static byte[] CreateControlTarball(string control, Package.InstallScripts scripts)
	{
		using var memory = new MemoryStream();
		using(var gzip = new GZipStream(memory, CompressionLevel.Optimal, true))
		using(var writer = new TarWriter(gzip, TarEntryFormat.Pax, true))
		{
			WriteTarText(writer, "control", control, 0644);
			WriteTarScript(writer, "preinst", scripts.Installing);
			WriteTarScript(writer, "postinst", scripts.Installed);
			WriteTarScript(writer, "prerm", scripts.Uninstalling);
			WriteTarScript(writer, "postrm", scripts.Uninstalled);
		}

		return memory.ToArray();
	}

	static void WriteTarEntry(TarWriter writer, Package.Entry item)
	{
		var entry = new PaxTarEntry(TarEntryType.RegularFile, item.EntryName)
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
			run_hook installed.sh
			echo "Installed {{packageName}} to $TARGET"
			""";

		static string Quote(string value) => string.IsNullOrEmpty(value) ? "''" : $"'{value.Replace("'", "'\"'\"'")}'";
	}
}
