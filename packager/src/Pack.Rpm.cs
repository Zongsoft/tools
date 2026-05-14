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
using System.Buffers.Binary;
using System.IO.Compression;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Runtime.InteropServices;

namespace Zongsoft.Tools.Packager;

partial class PackCommand
{
	static void GenerateRpm(string output, PackageMetadata metadata, IReadOnlyCollection<PackageEntry> entries, InstallScripts scripts)
	{
		var payload = CreateCpioPayload(entries, out var archiveSize);
		var header = RpmHeader.Create(metadata, entries, scripts, archiveSize);
		var body = Combine(header, payload);
		var signature = RpmSignature.Create(body);

		using var stream = File.Create(output);
		WriteRpmLead(stream, metadata);
		stream.Write(signature);
		stream.Write(body);
	}

	static byte[] CreateCpioPayload(IReadOnlyCollection<PackageEntry> entries, out long archiveSize)
	{
		using var raw = new MemoryStream();
		var directories = GetRpmDirectories(entries);
		var inode = 1;

		foreach(var directory in directories)
			WriteCpioEntry(raw, inode++, "." + directory, 0040755, 0, DateTimeOffset.UtcNow.ToUnixTimeSeconds(), null);

		foreach(var entry in entries)
		{
			using var file = File.OpenRead(entry.Source);
			WriteCpioEntry(raw, inode++, "." + GetRpmPath(entry.EntryName), 0100000 | entry.Mode, entry.Size, entry.ModifiedTime, file);
		}

		WriteCpioEntry(raw, inode, "TRAILER!!!", 0, 0, 0, null);
		Pad(raw, 512);

		archiveSize = raw.Length;

		using var compressed = new MemoryStream();
		raw.Position = 0;
		using(var gzip = new GZipStream(compressed, CompressionLevel.Optimal, true))
			raw.CopyTo(gzip);

		return compressed.ToArray();
	}

	static void WriteCpioEntry(Stream stream, int inode, string name, int mode, long size, long mtime, Stream data)
	{
		var namesize = Encoding.UTF8.GetByteCount(name) + 1;
		var header = string.Create(System.Globalization.CultureInfo.InvariantCulture, $"070701{inode:x8}{mode:x8}{0:x8}{0:x8}{1:x8}{mtime:x8}{size:x8}{0:x8}{0:x8}{0:x8}{0:x8}{namesize:x8}{0:x8}");

		stream.Write(Encoding.ASCII.GetBytes(header));
		stream.Write(Encoding.UTF8.GetBytes(name));
		stream.WriteByte(0);
		Pad(stream, 4);

		if(data != null)
			data.CopyTo(stream);

		Pad(stream, 4);
	}

	static void WriteRpmLead(Stream stream, PackageMetadata metadata)
	{
		var lead = new byte[96];
		lead[0] = 0xed;
		lead[1] = 0xab;
		lead[2] = 0xee;
		lead[3] = 0xdb;
		lead[4] = 3;
		lead[5] = 0;
		WriteInt16(lead.AsSpan(6), 0);
		WriteInt16(lead.AsSpan(8), GetRpmArchitectureNumber(metadata.Architecture));

		var name = Encoding.ASCII.GetBytes($"{metadata.PackageName}-{metadata.Version}");
		name.AsSpan(0, Math.Min(name.Length, 65)).CopyTo(lead.AsSpan(10));

		WriteInt16(lead.AsSpan(76), 1);
		WriteInt16(lead.AsSpan(78), 5);
		stream.Write(lead);
	}

	static byte[] Combine(byte[] first, byte[] second)
	{
		var result = new byte[first.Length + second.Length];
		Buffer.BlockCopy(first, 0, result, 0, first.Length);
		Buffer.BlockCopy(second, 0, result, first.Length, second.Length);
		return result;
	}

	static List<string> GetRpmDirectories(IReadOnlyCollection<PackageEntry> entries)
	{
		var result = new SortedSet<string>(StringComparer.Ordinal) { "/" };

		foreach(var entry in entries)
		{
			var directory = Path.GetDirectoryName(GetRpmPath(entry.EntryName))?.Replace('\\', '/');

			while(!string.IsNullOrEmpty(directory) && directory != "/")
			{
				result.Add(directory);
				directory = Path.GetDirectoryName(directory)?.Replace('\\', '/');
			}
		}

		return [.. result];
	}

	static RpmEntryCollection GetRpmEntries(IReadOnlyCollection<PackageEntry> entries)
	{
		var result = new RpmEntryCollection();
		var directories = GetRpmDirectories(entries);

		foreach(var directory in directories)
		{
			var fullName = directory == "/" ? "/" : directory + "/";
			AddRpmEntry(result, directories, fullName, 0, 0040755, DateTimeOffset.UtcNow.ToUnixTimeSeconds(), string.Empty);
		}

		foreach(var entry in entries)
		{
			using var stream = File.OpenRead(entry.Source);
			var digest = Convert.ToHexString(SHA1.HashData(stream)).ToLowerInvariant();
			AddRpmEntry(result, directories, GetRpmPath(entry.EntryName), entry.Size, 0100000 | entry.Mode, entry.ModifiedTime, digest);
		}

		result.Directories.AddRange(directories.ConvertAll(directory => directory.EndsWith('/') ? directory : directory + "/"));
		return result;
	}

	static void AddRpmEntry(RpmEntryCollection entries, IReadOnlyList<string> directories, string fullName, long size, int mode, long modified, string digest)
	{
		var name = fullName.TrimEnd('/');
		var directory = fullName.EndsWith('/') ?
			Path.GetDirectoryName(name)?.Replace('\\', '/') :
			Path.GetDirectoryName(fullName)?.Replace('\\', '/');

		if(string.IsNullOrEmpty(directory))
			directory = "/";

		var directoryName = directory.EndsWith('/') ? directory : directory + "/";
		var directoryIndex = IndexOf(directories, directory);

		if(directoryIndex < 0)
			directoryIndex = IndexOf(directories, directoryName.TrimEnd('/'));

		entries.Add(new(
			entries.Count + 1,
			size,
			mode,
			modified,
			digest,
			directoryIndex < 0 ? 0 : directoryIndex,
			fullName == "/" ? string.Empty : Path.GetFileName(name)));
	}

	static void Align(Stream stream, int size) => Pad(stream, size);
	static void Pad(Stream stream, int size)
	{
		while(stream.Position % size != 0)
			stream.WriteByte(0);
	}

	static int IndexOf(IReadOnlyList<string> values, string value)
	{
		if(values == null)
			return -1;

		for(int i = 0; i < values.Count; i++)
		{
			if(string.Equals(values[i], value, StringComparison.Ordinal))
				return i;
		}

		return -1;
	}

	static void WriteInt16(Span<byte> destination, int value) => BinaryPrimitives.WriteInt16BigEndian(destination, (short)value);
	static void WriteInt32(Span<byte> destination, int value) => BinaryPrimitives.WriteInt32BigEndian(destination, value);

	static string GetRpmPath(string entryName) => "/" + NormalizeEntryName(entryName);

	static string GetRpmArchitecture(Architecture architecture) => architecture switch
	{
		Architecture.X64 => "x86_64",
		Architecture.X86 => "i386",
		Architecture.Arm64 => "aarch64",
		Architecture.Arm => "armv7hl",
		_ => "noarch",
	};

	static short GetRpmArchitectureNumber(Architecture architecture) => architecture switch
	{
		Architecture.X64 => 1,
		Architecture.X86 => 1,
		Architecture.Arm64 => 12,
		Architecture.Arm => 12,
		_ => 255,
	};

	readonly record struct RpmHeaderIndex(int Tag, int Type, int Offset, int Count);
	readonly record struct RpmEntry(int Inode, long Size, int Mode, long ModifiedTime, string Digest, int DirectoryIndex, string BaseName);

	sealed class RpmSignature
	{
		public static byte[] Create(byte[] body)
		{
			using var md5 = MD5.Create();
			var digest = md5.ComputeHash(body);
			var header = new RpmHeaderBuilder();

			header.AddInt32(257, body.Length);
			header.AddBinary(261, digest);

			return header.Build(true);
		}
	}

	sealed class RpmHeader
	{
		public static byte[] Create(PackageMetadata metadata, IReadOnlyCollection<PackageEntry> entries, InstallScripts scripts, long archiveSize)
		{
			var builder = new RpmHeaderBuilder();
			var buildTime = (int)DateTimeOffset.UtcNow.ToUnixTimeSeconds();
			var rpmEntries = GetRpmEntries(entries);

			builder.AddString(1000, metadata.PackageName);
			builder.AddString(1001, metadata.Version.ToString());
			builder.AddString(1002, string.IsNullOrWhiteSpace(metadata.Edition) ? "1" : metadata.Edition);
			builder.AddInternationalString(1004, metadata.Summary ?? metadata.Title ?? metadata.Name);
			builder.AddInternationalString(1005, metadata.Description ?? metadata.Summary ?? metadata.Name);
			builder.AddInt32(1006, buildTime);
			builder.AddString(1007, Environment.MachineName);
			builder.AddInt32(1009, (int)Math.Min(int.MaxValue, GetPackageSize(entries)));
			builder.AddString(1014, "MIT");
			builder.AddString(1015, "Zongsoft Studio <zongsoft@qq.com>");
			builder.AddString(1016, "Applications/System");
			builder.AddString(1020, "https://github.com/Zongsoft/framework");
			builder.AddString(1021, "linux");
			builder.AddString(1022, GetRpmArchitecture(metadata.Architecture));
			builder.AddScript(1023, scripts.Installing);
			builder.AddScript(1024, scripts.Installed);
			builder.AddScript(1025, scripts.Uninstalling);
			builder.AddScript(1026, scripts.Uninstalled);
			builder.AddInt32Array(1028, rpmEntries.ConvertAll(entry => (int)Math.Min(int.MaxValue, entry.Size)));
			builder.AddInt16Array(1030, rpmEntries.ConvertAll(entry => (short)entry.Mode));
			builder.AddInt16Array(1033, rpmEntries.ConvertAll(_ => (short)0));
			builder.AddInt32Array(1034, rpmEntries.ConvertAll(entry => (int)entry.ModifiedTime));
			builder.AddStringArray(1035, rpmEntries.ConvertAll(entry => entry.Digest));
			builder.AddStringArray(1036, rpmEntries.ConvertAll(_ => string.Empty));
			builder.AddInt32Array(1037, rpmEntries.ConvertAll(_ => 0));
			builder.AddStringArray(1039, rpmEntries.ConvertAll(_ => "root"));
			builder.AddStringArray(1040, rpmEntries.ConvertAll(_ => "root"));
			builder.AddInt32Array(1045, rpmEntries.ConvertAll(_ => -1));
			builder.AddInt32(1046, (int)Math.Min(int.MaxValue, archiveSize));
			builder.AddStringArray(1047, [metadata.PackageName]);
			builder.AddInt32Array(1048, [0]);
			builder.AddStringArray(1049, ["rpmlib(CompressedFileNames)", "rpmlib(PayloadFilesHavePrefix)"]);
			builder.AddStringArray(1050, [""]);
			builder.AddString(1056, metadata.InstallRoot);
			builder.AddString(1124, "cpio");
			builder.AddString(1125, "gzip");
			builder.AddString(1126, "9");
			builder.AddInt32Array(1095, rpmEntries.ConvertAll(_ => 1));
			builder.AddInt32Array(1096, rpmEntries.ConvertAll(entry => entry.Inode));
			builder.AddStringArray(1097, rpmEntries.ConvertAll(_ => string.Empty));
			builder.AddInt32Array(1116, rpmEntries.ConvertAll(entry => entry.DirectoryIndex));
			builder.AddStringArray(1117, rpmEntries.ConvertAll(entry => entry.BaseName));
			builder.AddStringArray(1118, rpmEntries.Directories);
			builder.AddInt32Array(1140, rpmEntries.ConvertAll(_ => 0));
			builder.AddStringArray(1142, [""]);
			builder.AddInt32Array(5011, [1]);

			return builder.Build(false);
		}
	}

	sealed class RpmHeaderBuilder
	{
		private readonly List<RpmHeaderIndex> _indexes = [];
		private readonly MemoryStream _store = new();

		public void AddString(int tag, string value) => Add(tag, 6, 1, () => WriteString(value ?? string.Empty));
		public void AddInternationalString(int tag, string value) => Add(tag, 9, 1, () => WriteString(value ?? string.Empty));
		public void AddScript(int tag, string value)
		{
			if(!string.IsNullOrWhiteSpace(value))
				AddString(tag, "#!/bin/sh\nset -e\n" + value.Trim() + "\n");
		}
		public void AddBinary(int tag, byte[] value) => Add(tag, 7, value.Length, () => _store.Write(value));
		public void AddInt32(int tag, int value) => AddInt32Array(tag, [value]);
		public void AddInt32Array(int tag, IReadOnlyList<int> values) => Add(tag, 4, values.Count, () =>
		{
			Span<byte> buffer = stackalloc byte[4];

			foreach(var value in values)
			{
				WriteInt32(buffer, value);
				_store.Write(buffer);
			}
		});
		public void AddInt16Array(int tag, IReadOnlyList<short> values) => Add(tag, 3, values.Count, () =>
		{
			Span<byte> buffer = stackalloc byte[2];

			foreach(var value in values)
			{
				WriteInt16(buffer, value);
				_store.Write(buffer);
			}
		});
		public void AddStringArray(int tag, IReadOnlyList<string> values) => Add(tag, 8, values.Count, () =>
		{
			foreach(var value in values)
				WriteString(value ?? string.Empty);
		});

		public byte[] Build(bool signature)
		{
			using var stream = new MemoryStream();
			Span<byte> buffer = stackalloc byte[16];

			stream.Write(signature ? [0x8e, 0xad, 0xe8, 0x01] : [0x8e, 0xad, 0xe8, 0x01]);
			stream.WriteByte(0);
			stream.Write([0, 0, 0]);
			WriteInt32(buffer[..4], _indexes.Count);
			stream.Write(buffer[..4]);
			WriteInt32(buffer[..4], (int)_store.Length);
			stream.Write(buffer[..4]);

			foreach(var index in _indexes)
			{
				WriteInt32(buffer[..4], index.Tag);
				WriteInt32(buffer[4..8], index.Type);
				WriteInt32(buffer[8..12], index.Offset);
				WriteInt32(buffer[12..16], index.Count);
				stream.Write(buffer);
			}

			_store.Position = 0;
			_store.CopyTo(stream);
			Pad(stream, 8);

			return stream.ToArray();
		}

		private void Add(int tag, int type, int count, Action writer)
		{
			Align(_store, GetAlignment(type));
			_indexes.Add(new(tag, type, (int)_store.Position, count));
			writer();
		}

		private void WriteString(string value)
		{
			_store.Write(Encoding.UTF8.GetBytes(value));
			_store.WriteByte(0);
		}

		static int GetAlignment(int type) => type switch
		{
			3 => 2,
			4 => 4,
			5 => 8,
			_ => 1,
		};
	}

	sealed class RpmEntryCollection : List<RpmEntry>
	{
		public List<string> Directories { get; } = [];
	}
}
