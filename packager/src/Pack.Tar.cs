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

partial class PackCommand
{
	static void GenerateTar(string output, IReadOnlyCollection<PackageEntry> entries, InstallScripts scripts)
	{
		using var stream = File.Create(output);
		using var gzip = new GZipStream(stream, CompressionLevel.Optimal);
		using var writer = new TarWriter(gzip, TarEntryFormat.Pax, false);

		foreach(var entry in entries)
			WriteTarEntry(writer, entry);

		WriteTarScript(writer, ".install/installing.sh", scripts.Installing);
		WriteTarScript(writer, ".install/installed.sh", scripts.Installed);
		WriteTarScript(writer, ".install/uninstalling.sh", scripts.Uninstalling);
		WriteTarScript(writer, ".install/uninstalled.sh", scripts.Uninstalled);
	}

	static byte[] CreateDataTarball(IReadOnlyCollection<PackageEntry> entries)
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

	static byte[] CreateControlTarball(string control, InstallScripts scripts)
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

	static void WriteTarEntry(TarWriter writer, PackageEntry item)
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
}
