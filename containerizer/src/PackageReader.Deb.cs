/*
 *   _____                                ______
 *  /_   /  ____  ____  ____  _________  / __/ /_
 *    / /  / __ \/ __ \/ __ \/ ___/ __ \/ /_/ __/
 *   / /__/ /_/ / / / /_/ /\_ \/ /_/ / __/ /_
 *  /____/\____/_/ /_/\__  /____/\____/_/  \__/
 *                   /____/
 *
 * Authors:
 *   钟峰(Popeye Zhong) <zongsoft@gmail.com>
 *
 * The MIT License (MIT)
 *
 * Copyright (C) 2026 Zongsoft Corporation <http://www.zongsoft.com>
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
using System.Text;
using System.Formats.Tar;
using System.Globalization;
using System.IO.Compression;
using System.Buffers.Binary;
using System.Collections.Generic;

using Zongsoft.Tools.Containerizer.Protocol;

namespace Zongsoft.Tools.Containerizer;

partial class PackageReader
{
	#region 私有方法
	private static void ReadDeb(FileStream stream, Descriptor result)
	{
		var magic = new byte[8];
		stream.ReadExactly(magic);

		if(Encoding.ASCII.GetString(magic) != "!<arch>\n")
			throw new ContainerizationException(2, Properties.Resources.PackageReader_8_Message);

		var header = new byte[60];
		while(stream.Position < stream.Length)
		{
			stream.ReadExactly(header);

			if(header[58] != '`' ||
			   header[59] != '\n' ||
			   !long.TryParse(Encoding.ASCII.GetString(header, 48, 10).Trim(), out var length) ||
			   length < 0 ||
			   length > stream.Length - stream.Position)
				throw new ContainerizationException(2, Properties.Resources.PackageReader_9_Message);

			var name = Encoding.ASCII.GetString(header, 0, 16).Trim().TrimEnd('/');
			var next = stream.Position + length + (length & 1);

			if(name is "control.tar.gz" or "data.tar.gz")
				ReadTar(stream, result, false);
			else if(name.StartsWith("control.tar", StringComparison.Ordinal) || name.StartsWith("data.tar", StringComparison.Ordinal))
				throw new ContainerizationException(2, Properties.Resources.PackageReader_10_Message);

			stream.Position = next;
		}

		if(!result.Texts.TryGetValue("control", out var control))
			throw new ContainerizationException(2, Properties.Resources.PackageReader_11_Message);

		var fields = control.Split('\n')
			.Where(line => line.Length > 0 && !char.IsWhiteSpace(line[0]) && line.Contains(':'))
			.Select(line => line.Split(':', 2))
			.ToDictionary(parts => parts[0], parts => parts[1].Trim(), StringComparer.OrdinalIgnoreCase);

		result.Name = fields.GetValueOrDefault("Package");
		result.Version = fields.GetValueOrDefault("Version");
		result.Architecture = fields.GetValueOrDefault("Architecture");
		result.Listen = fields.GetValueOrDefault("Listen");
	}
	#endregion
}
