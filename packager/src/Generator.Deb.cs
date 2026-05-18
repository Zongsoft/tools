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
using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace Zongsoft.Tools.Packager;

partial class Generator
{
	public static void Deb(this Package package, string output, bool overwrite)
	{
		using var stream = new FileStream(
			Path.Combine(output, package.FileName),
			overwrite ? FileMode.Create : FileMode.CreateNew,
			FileAccess.Write);

		var control = GetDebControl(package);

		WriteArHeader(stream);
		WriteArEntry(stream, "debian-binary", Encoding.ASCII.GetBytes("2.0\n"));
		WriteArEntry(stream, "control.tar.gz", CreateControlTarball(control, package));
		WriteArEntry(stream, "data.tar.gz", CreateDataTarball(package.Entries));
	}

	static string GetDebControl(Package package)
	{
		var builder = new StringBuilder();
		var summary = NormalizeDebText(package.Summary) ?? NormalizeDebText(package.Title) ?? NormalizeDebText(package.Name) ?? package.PackageName;
		var description = string.IsNullOrWhiteSpace(package.Description) ? summary : package.Description;

		builder.AppendLine($"Package: {package.PackageName}");
		builder.AppendLine($"Version: {package.Version}");
		builder.AppendLine($"Section: {NormalizeDebText(package.Category) ?? "utils"}");
		builder.AppendLine($"Priority: optional");
		builder.AppendLine($"Architecture: {GetDebianArchitecture(package.Architecture)}");
		builder.AppendLine($"Installed-Size: {Math.Max(1, (package.GetPackageSize() + 1023) / 1024)}");
		builder.AppendLine($"Maintainer: {NormalizeDebText(package.Maintainer)}");
		builder.AppendLine($"Homepage: {NormalizeDebText(package.Url)}");
		builder.AppendLine($"License: {NormalizeDebText(package.License)}");

		if(package.Dependencies.Length > 0)
			builder.AppendLine($"Depends: {string.Join(", ", package.Dependencies)}");

		builder.AppendLine($"Description: {summary}");

		if(!string.IsNullOrWhiteSpace(description))
		{
			foreach(var line in description.Replace("\r", string.Empty).Split('\n'))
				builder.AppendLine(string.IsNullOrWhiteSpace(line) ? " ." : $" {NormalizeDebText(line)}");
		}

		return builder.ToString().ReplaceLineEndings("\n");
	}

	static byte[] CreateDataTarball(IReadOnlyCollection<Package.Entry> entries)
	{
		using var memory = new MemoryStream();
		using(var gzip = new GZipStream(memory, CompressionLevel.Optimal, true))
		using(var writer = new TarWriter(gzip, TarEntryFormat.Ustar, true))
		{
			foreach(var directory in GetDebianDirectories(entries))
				WriteDebTarDirectory(writer, directory);

			foreach(var entry in entries)
				WriteDebTarEntry(writer, entry);
		}

		return memory.ToArray();
	}

	static byte[] CreateControlTarball(string control, Package package)
	{
		using var memory = new MemoryStream();
		using(var gzip = new GZipStream(memory, CompressionLevel.Optimal, true))
		using(var writer = new TarWriter(gzip, TarEntryFormat.Ustar, true))
		{
			WriteDebTarText(writer, "control", control, 420);
			WriteDebTarScript(writer, "preinst", package.Scripts.Installing);
			WriteDebTarScript(writer, "postinst", package.Scripts.Installed);
			WriteDebTarScript(writer, "prerm", package.Scripts.Uninstalling);
			WriteDebTarScript(writer, "postrm", package.Scripts.Uninstalled);

			var conffiles = GetDebianConfigurationFiles(package.Entries);
			if(!string.IsNullOrEmpty(conffiles))
				WriteDebTarText(writer, "conffiles", conffiles, 420);
		}

		return memory.ToArray();
	}

	static void WriteDebTarEntry(TarWriter writer, Package.Entry item)
	{
		var entry = new UstarTarEntry(TarEntryType.RegularFile, item.EntryName)
		{
			Mode = GetDebianFileMode(item.Mode),
			ModificationTime = DateTimeOffset.FromUnixTimeSeconds(item.ModifiedTime),
			DataStream = File.OpenRead(item.Source),
		};

		writer.WriteEntry(entry);
		entry.DataStream.Dispose();
	}

	static void WriteDebTarDirectory(TarWriter writer, string name)
	{
		writer.WriteEntry(new UstarTarEntry(TarEntryType.Directory, name)
		{
			Mode = GetDebianFileMode(493),
			ModificationTime = DateTimeOffset.UtcNow,
		});
	}

	static void WriteDebTarScript(TarWriter writer, string name, string script)
	{
		if(string.IsNullOrWhiteSpace(script))
			return;

		WriteDebTarText(writer, name, "#!/bin/sh\nset -e\n" + script.Trim().ReplaceLineEndings("\n") + "\n", 493);
	}

	static void WriteDebTarText(TarWriter writer, string name, string text, int mode)
	{
		var data = Encoding.UTF8.GetBytes((text ?? string.Empty).ReplaceLineEndings("\n"));
		var entry = new UstarTarEntry(TarEntryType.RegularFile, name)
		{
			Mode = GetDebianFileMode(mode),
			ModificationTime = DateTimeOffset.UtcNow,
			DataStream = new MemoryStream(data),
		};

		writer.WriteEntry(entry);
		entry.DataStream.Dispose();
	}

	static string GetDebianConfigurationFiles(IReadOnlyCollection<Package.Entry> entries)
	{
		var files = entries
			.Where(IsDebianConfigurationFile)
			.Select(entry => "/" + Utility.NormalizePath(entry.EntryName))
			.Order(StringComparer.Ordinal)
			.ToArray();

		return files.Length == 0 ? null : string.Join('\n', files) + "\n";
	}

	static IEnumerable<string> GetDebianDirectories(IEnumerable<Package.Entry> entries)
	{
		var directories = new List<string>();
		var unique = new HashSet<string>(StringComparer.Ordinal);

		foreach(var entry in entries)
		{
			var path = Utility.NormalizePath(entry.EntryName);
			var index = 0;

			while((index = path.IndexOf('/', index + 1)) > 0)
			{
				var directory = path[..index];
				if(unique.Add(directory))
					directories.Add(directory);
			}
		}

		return directories;
	}

	static bool IsDebianConfigurationFile(Package.Entry entry) => entry.Rooted && entry.EntryName.StartsWith("etc/", StringComparison.Ordinal);

	static UnixFileMode GetDebianFileMode(int mode) => mode <= 0x1FF ?
		(UnixFileMode)mode :
		(UnixFileMode)Convert.ToInt32(mode.ToString(System.Globalization.CultureInfo.InvariantCulture), 8);

	static void WriteArHeader(Stream stream) => stream.Write(Encoding.ASCII.GetBytes("!<arch>\n"));

	static void WriteArEntry(Stream stream, string name, byte[] data)
	{
		var header = Encoding.ASCII.GetBytes(string.Format(
			System.Globalization.CultureInfo.InvariantCulture,
			"{0,-16}{1,-12}{2,-6}{3,-6}{4,-8}{5,-10}`\n",
			name.EndsWith('/') ? name : name + "/",
			DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
			0,
			0,
			"100644",
			data.Length));

		stream.Write(header);
		stream.Write(data);

		if((data.Length & 1) != 0)
			stream.WriteByte((byte)'\n');
	}

	static string NormalizeDebText(string text)
	{
		return string.IsNullOrWhiteSpace(text) ? null : text.Replace("\r", string.Empty).Replace("\n", " ").Trim();
	}

	static string GetDebianArchitecture(Architecture architecture) => architecture switch
	{
		Architecture.X64 => "amd64",
		Architecture.X86 => "i386",
		Architecture.Arm64 => "arm64",
		Architecture.Arm => "armhf",
		_ => "all",
	};
}
