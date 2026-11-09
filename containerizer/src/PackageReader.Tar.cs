/*
 *   _____                                ______
 *  /_   /  ____  ____  ____  _________  / __/ /_
 *    / /  / __ \/ __ \/ __ \/ ___/ __ \/ /_/ __/
 *   / /__/ /_/ / / / / /_/ /\_ \/ /_/ / __/ /_
 *  /____/\____/_/ /_/\__  /____/\____/_/  \__/
 *                   /____/
 *
 * Authors:
 *   钟峰(Popeye Zhong) <zongsoft@qq.com>
 *
 * Copyright (c) Zongsoft Studio. All rights reserved.
 * The above copyright notice and this permission notice shall be included in all
 * AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
 *
 *
 * Licensed under the MIT License (see LICENSE).
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
			}
			else
				Capture(result, entry.Name, entry.DataStream, entry.Length, entry.EntryType is TarEntryType.RegularFile or TarEntryType.V7RegularFile, entry.EntryType is TarEntryType.SymbolicLink or TarEntryType.HardLink);
		}
	}
	#endregion
}
