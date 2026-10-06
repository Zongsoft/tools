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
	private static void ReadRpm(FileStream stream, Descriptor result)
	{
		var lead = new byte[96];
		stream.ReadExactly(lead);

		if(!lead.AsSpan(0, 4).SequenceEqual<byte>([0xed, 0xab, 0xee, 0xdb]))
			throw new ContainerizationException(2, Properties.Resources.PackageReader_12_Message);

		ReadHeader(stream);
		stream.Position = (stream.Position + 7) & ~7L;

		var fields = ReadHeader(stream);
		result.Name = fields.GetValueOrDefault(1000);
		result.Version = fields.GetValueOrDefault(1001);
		result.Architecture = fields.GetValueOrDefault(1022);
		result.Listen = fields.GetValueOrDefault(1000001);

		if(fields.GetValueOrDefault(1125) is not (null or "gzip"))
			throw new ContainerizationException(2, Properties.Resources.PackageReader_13_Message);

		using var gzip = new GZipStream(stream, CompressionMode.Decompress, true);
		var header = new byte[110];

		while(true)
		{
			gzip.ReadExactly(header);

			if(Encoding.ASCII.GetString(header, 0, 6) is not ("070701" or "070702"))
				throw new ContainerizationException(2, Properties.Resources.PackageReader_14_Message);

			var size = Convert.ToInt64(Encoding.ASCII.GetString(header, 54, 8), 16);
			var nameSize = Convert.ToInt32(Encoding.ASCII.GetString(header, 94, 8), 16);

			if(nameSize < 1 || nameSize > 65536)
				throw new ContainerizationException(2, Properties.Resources.PackageReader_15_Message);

			var nameBytes = new byte[nameSize];
			gzip.ReadExactly(nameBytes);
			var name = Encoding.UTF8.GetString(nameBytes, 0, nameSize - 1);
			Skip(gzip, (4 - (110 + nameSize) % 4) % 4);

			if(name == "TRAILER!!!")
				break;

			var mode = Convert.ToInt32(Encoding.ASCII.GetString(header, 14, 8), 16);
			var consumed = Capture(result, name, gzip, size, (mode & 0xf000) == 0x8000, (mode & 0xf000) == 0xa000);
			Skip(gzip, size - consumed);

			Skip(gzip, (4 - size % 4) % 4);
		}
	}

	private static Dictionary<int, string> ReadHeader(Stream stream)
	{
		var header = new byte[16];
		stream.ReadExactly(header);

		if(!header.AsSpan(0, 4).SequenceEqual<byte>([0x8e, 0xad, 0xe8, 1]))
			throw new ContainerizationException(2, Properties.Resources.PackageReader_16_Message);

		var count = BinaryPrimitives.ReadInt32BigEndian(header.AsSpan(8));
		var length = BinaryPrimitives.ReadInt32BigEndian(header.AsSpan(12));

		if(count < 0 || count > 65536 || length < 0 || length > 32 * 1024 * 1024)
			throw new ContainerizationException(2, Properties.Resources.PackageReader_17_Message);

		var indices = new byte[count * 16];
		var data = new byte[length];
		stream.ReadExactly(indices);
		stream.ReadExactly(data);
		var result = new Dictionary<int, string>();

		for(int index = 0; index < count; index++)
		{
			var item = indices.AsSpan(index * 16, 16);
			var tag = BinaryPrimitives.ReadInt32BigEndian(item);
			var type = BinaryPrimitives.ReadInt32BigEndian(item[4..]);
			var offset = BinaryPrimitives.ReadInt32BigEndian(item[8..]);

			if(tag == 1000001 && (type != 6 || BinaryPrimitives.ReadInt32BigEndian(item[12..]) != 1 || result.ContainsKey(tag)))
				throw ApplicationHealth.Invalid("RPM", "Listen");

			if(type != 6)
				continue;
			if(offset < 0 || offset >= data.Length)
				throw new ContainerizationException(2, Properties.Resources.PackageReader_18_Message);

			var end = Array.IndexOf(data, (byte)0, offset);
			if(end < 0)
				throw new ContainerizationException(2, Properties.Resources.PackageReader_19_Message);

			result[tag] = Encoding.UTF8.GetString(data, offset, end - offset);
		}

		return result;
	}

	private static void Skip(GZipStream stream, long length)
	{
		var buffer = new byte[8192];

		while(length > 0)
		{
			var count = stream.Read(buffer, 0, (int)Math.Min(length, buffer.Length));

			if(count == 0)
				throw new EndOfStreamException();

			length -= count;
		}
	}
	#endregion
}
