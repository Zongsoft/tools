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
using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace Zongsoft.Tools.Packager;

partial class PackCommand
{
	static void GenerateDeb(string output, PackageMetadata metadata, IReadOnlyCollection<PackageEntry> entries, InstallScripts scripts)
	{
		var control = GetDebControl(metadata, entries);
		using var stream = File.Create(output);

		WriteArHeader(stream);
		WriteArEntry(stream, "debian-binary", Encoding.ASCII.GetBytes("2.0\n"));
		WriteArEntry(stream, "control.tar.gz", CreateControlTarball(control, scripts));
		WriteArEntry(stream, "data.tar.gz", CreateDataTarball(entries));
	}

	static string GetDebControl(PackageMetadata metadata, IReadOnlyCollection<PackageEntry> entries)
	{
		var builder = new StringBuilder();
		var summary = string.IsNullOrWhiteSpace(metadata.Summary) ? metadata.Title : metadata.Summary;
		var description = string.IsNullOrWhiteSpace(metadata.Description) ? summary : metadata.Description;

		builder.AppendLine($"Package: {metadata.PackageName}");
		builder.AppendLine($"Version: {metadata.Version}");
		builder.AppendLine($"Section: utils");
		builder.AppendLine($"Priority: optional");
		builder.AppendLine($"Architecture: {GetDebianArchitecture(metadata.Architecture)}");
		builder.AppendLine($"Installed-Size: {Math.Max(1, (GetPackageSize(entries) + 1023) / 1024)}");
		builder.AppendLine($"Maintainer: Zongsoft Studio <zongsoft@qq.com>");
		builder.AppendLine($"Homepage: https://github.com/Zongsoft/framework");
		builder.AppendLine($"Description: {NormalizeDebText(summary ?? metadata.Name)}");

		if(!string.IsNullOrWhiteSpace(description))
		{
			foreach(var line in description.Replace("\r", string.Empty).Split('\n'))
				builder.AppendLine(string.IsNullOrWhiteSpace(line) ? " ." : $" {NormalizeDebText(line)}");
		}

		return builder.ToString();
	}

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
		return string.IsNullOrWhiteSpace(text) ? string.Empty : text.Replace("\r", string.Empty).Replace("\n", " ").Trim();
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
