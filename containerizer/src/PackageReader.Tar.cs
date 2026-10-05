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
	private static void ReadTar(Stream stream, Descriptor result, bool metadata)
	{
		using var gzip = new GZipStream(stream, CompressionMode.Decompress, true);
		using var reader = new TarReader(gzip, true);

		while(reader.GetNextEntry() is { } entry)
		{
			if(entry is PaxGlobalExtendedAttributesTarEntry global && metadata)
			{
				result.Name = global.GlobalExtendedAttributes.GetValueOrDefault("PackageName");
				result.Architecture = global.GlobalExtendedAttributes.GetValueOrDefault("Architecture");
				result.Version = global.GlobalExtendedAttributes.GetValueOrDefault("Version");
				result.InstallPath = global.GlobalExtendedAttributes.GetValueOrDefault("InstallPath");
				result.Listen = global.GlobalExtendedAttributes.GetValueOrDefault("Listen");
			}
			else if(entry.DataStream != null && IsMetadata(entry.Name) && entry.Length <= 1024 * 1024)
			{
				using var text = new StreamReader(entry.DataStream, leaveOpen: true);
				result.Texts.Add(NormalizeName(entry.Name), text.ReadToEnd());
			}
		}
	}
	#endregion
}
