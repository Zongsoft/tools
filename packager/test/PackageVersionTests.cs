using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Formats.Tar;
using System.IO.Compression;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Security.Cryptography;

using Zongsoft.Services;
using Xunit;

namespace Zongsoft.Tools.Packager.Tests;

public sealed class PackageVersionTests
{
	#region 条目测试
	[Fact]
	public void Entry_MemoryContent_OpensIndependentReadableStreams()
	{
		var content = Encoding.UTF8.GetBytes("Zongsoft.Daemon@1.2.0\r\n");
		var entry = new Package.Entry(content, ".version", 1700000000, (UnixFileMode)420, false);
		using var first = entry.OpenRead();
		using var second = entry.OpenRead();

		Assert.NotSame(first, second);
		Assert.Equal((long)content.Length, entry.Size);
		Assert.Equal((int)'Z', first.ReadByte());
		Assert.Equal(0, second.Position);
		Assert.False(first.CanWrite);
		first.Dispose();
		using var copy = new MemoryStream();
		second.CopyTo(copy);
		Assert.Equal(content, copy.ToArray());
		using var third = entry.OpenRead();
		Assert.Equal((int)'Z', third.ReadByte());
	}

	[Fact]
	public void Entry_FileContent_StillReadsSource()
	{
		using var directory = new MigrationTestDirectory();
		var path = directory.Write("application.txt", "hosting payload");
		var entry = new Package.Entry(path, "application.txt", new FileInfo(path).Length, 1700000000, (UnixFileMode)420, false);

		using var stream = entry.OpenRead();
		using var reader = new StreamReader(stream);

		Assert.Equal("hosting payload", reader.ReadToEnd());
		Assert.Equal(path, entry.Source);
	}
	#endregion

	#region 制包测试
	public static IEnumerable<object[]> SelectionCases()
	{
		foreach(var format in new[] { "tar", "deb", "rpm" })
			foreach(var selection in new[] { "recursive", "explicit", "excluded", "root-alias", "both-aliases" })
				yield return [format, selection];
	}

	[Theory]
	[MemberData(nameof(SelectionCases))]
	public void Package_VersionIdentity_ReplacesOldEntriesAcrossSelectionModes(string format, string selection)
	{
		using var directory = new MigrationTestDirectory();
		var original = directory.Write("source/.version", "Zongsoft.Hosting.Web\n[Community]\n1.0.0\n[Enterprise]\n4.0.0\n");
		directory.Write("source/nested/.version", "Zongsoft.Nested@9.0.0\n");
		directory.Write("source/application.txt", "payload retained");
		var source = Path.GetDirectoryName(original);
		var sourceBytes = File.ReadAllBytes(original);
		var output = Path.Combine(directory.Path, "output");
		Directory.CreateDirectory(output);
		Normalizer.Initialize(new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
		{
			["source"] = source,
			["framework"] = "net10.0",
			["daemon"] = "disabled",
		});
		Package package = format switch
		{
			"tar" => new Package.Tar("Zongsoft.Hosting.Web", "Community", new Version(2, 3, 4), Platform.Linux, Architecture.X64),
			"deb" => new Package.Deb("Zongsoft.Hosting.Web", "Community", new Version(2, 3, 4), Platform.Linux, Architecture.X64),
			"rpm" => new Package.Rpm("Zongsoft.Hosting.Web", "Community", new Version(2, 3, 4), Platform.Linux, Architecture.X64),
			_ => throw new ArgumentOutOfRangeException(nameof(format)),
		};
		package.InstallPath = "/opt/zongsoft/web";
		var alias = ".version:" + package.InstallPath + "/.version";
		string[] arguments = selection switch
		{
			"explicit" => [".version", "application.txt"],
			"root-alias" => [alias, "application.txt"],
			"both-aliases" => [".version", alias, "application.txt"],
			_ => [],
		};
		package.Entries.Load(source, arguments, selection == "excluded" ? [".version"] : []);
		var identity = new ApplicationIdentifier(package.Name, package.Edition, package.Version);

		package.Entries.SetVersion(identity);
		package.Scriptor.Script();
		package.Pack(output, true);

		var expected = Encoding.UTF8.GetBytes("Zongsoft.Hosting.Web-Community@2.3.4");
		var target = format == "tar" ? ".version" : "opt/zongsoft/web/.version";
		var entry = Assert.Single(package.Entries, item => item.EntryName == target);
		Assert.False(entry.Rooted);
		Assert.Null(entry.Source);
		Assert.Equal((long)expected.Length, entry.Size);
		Assert.Equal((UnixFileMode)420, entry.Mode);
		var archivePath = Path.Combine(output, package.FileName);
		var archive = ReadArchive(archivePath, format);
		var archived = Assert.Single(archive, item => item.Name == target);
		Assert.Equal(expected, archived.Content);
		Assert.Equal((UnixFileMode)420, archived.Mode);
		Assert.Equal(expected.Length, archived.Size);
		Assert.DoesNotContain(archive, item => item.Name == ".root/opt/zongsoft/web/.version");
		Assert.Equal(sourceBytes, File.ReadAllBytes(original));
		Assert.Contains(archive, item => item.Name.EndsWith("application.txt", StringComparison.Ordinal) && Encoding.UTF8.GetString(item.Content) == "payload retained");
		var parsed = ApplicationIdentifier.Parse(Encoding.UTF8.GetString(archived.Content).Trim());
		Assert.Equal(identity, parsed);
		if(selection == "recursive")
		{
			var nested = Assert.Single(archive, item => item.Name.EndsWith("nested/.version", StringComparison.Ordinal));
			Assert.Equal("Zongsoft.Nested@9.0.0\r\n", Encoding.UTF8.GetString(nested.Content));
		}
		if(format == "rpm") AssertRpmVersionDigest(archivePath, expected);
	}
	#endregion

	#region 归档读取
	private static List<ArchiveFile> ReadArchive(string path, string format)
	{
		var bytes = File.ReadAllBytes(path);
		if(format == "tar") return ReadTar(bytes);
		if(format == "deb")
		{
			Assert.Equal("!<arch>\n", Encoding.ASCII.GetString(bytes, 0, 8));
			for(var offset = 8; offset + 60 <= bytes.Length;)
			{
				var name = Encoding.ASCII.GetString(bytes, offset, 16).Trim().TrimEnd('/');
				var size = int.Parse(Encoding.ASCII.GetString(bytes, offset + 48, 10).Trim(), System.Globalization.CultureInfo.InvariantCulture);
				if(name == "data.tar.gz") return ReadTar(bytes[(offset + 60)..(offset + 60 + size)]);
				offset += 60 + size + (size & 1);
			}
			throw new InvalidDataException("Missing Debian payload.");
		}

		var payload = HeaderEnd(bytes, HeaderEnd(bytes, 96, true), false);
		using var stream = new MemoryStream(bytes, payload, bytes.Length - payload);
		using var gzip = new GZipStream(stream, CompressionMode.Decompress);
		using var copy = new MemoryStream();
		gzip.CopyTo(copy);
		bytes = copy.ToArray();
		var files = new List<ArchiveFile>();
		for(var offset = 0; offset + 110 <= bytes.Length;)
		{
			Assert.Equal("070701", Encoding.ASCII.GetString(bytes, offset, 6));
			var mode = Convert.ToInt32(Encoding.ASCII.GetString(bytes, offset + 14, 8), 16);
			var size = Convert.ToInt32(Encoding.ASCII.GetString(bytes, offset + 54, 8), 16);
			var nameSize = Convert.ToInt32(Encoding.ASCII.GetString(bytes, offset + 94, 8), 16);
			var name = NormalizeName(Encoding.UTF8.GetString(bytes, offset + 110, nameSize - 1));
			offset = (offset + 110 + nameSize + 3) & ~3;
			if(name == "TRAILER!!!") break;
			if((mode & 0xF000) == 0x8000) files.Add(new(name, bytes[offset..(offset + size)], (UnixFileMode)(mode & 0xFFF), size));
			offset = (offset + size + 3) & ~3;
		}
		return files;
	}

	private static List<ArchiveFile> ReadTar(byte[] bytes)
	{
		using var stream = new MemoryStream(bytes);
		using var gzip = new GZipStream(stream, CompressionMode.Decompress);
		using var reader = new TarReader(gzip);
		var files = new List<ArchiveFile>();
		TarEntry entry;
		while((entry = reader.GetNextEntry()) != null)
		{
			if(entry.DataStream == null) continue;
			using var copy = new MemoryStream();
			entry.DataStream.CopyTo(copy);
			files.Add(new(NormalizeName(entry.Name), copy.ToArray(), entry.Mode, entry.Length));
		}
		return files;
	}

	private static void AssertRpmVersionDigest(string path, byte[] expected)
	{
		var bytes = File.ReadAllBytes(path);
		var header = HeaderEnd(bytes, 96, true);
		var names = Strings(bytes, header, 1117);
		var directories = Strings(bytes, header, 1118);
		var indexes = TagOffset(bytes, header, 1116);
		var index = Assert.Single(Enumerable.Range(0, names.Length), index => names[index] == ".version" && directories[ReadInt(bytes, indexes.Offset + index * 4)] == "/opt/zongsoft/web/");
		var digests = Strings(bytes, header, 1035);
		Assert.Equal(Convert.ToHexString(SHA256.HashData(expected)).ToLowerInvariant(), digests[index]);
		Assert.Equal(8, ReadInt(bytes, TagOffset(bytes, header, 5011).Offset));
	}

	private static string[] Strings(byte[] bytes, int header, int tag)
	{
		var item = TagOffset(bytes, header, tag);
		var result = new string[item.Count];
		var offset = item.Offset;
		for(var index = 0; index < result.Length; index++)
		{
			var end = Array.IndexOf(bytes, (byte)0, offset);
			Assert.True(end >= offset);
			result[index] = Encoding.UTF8.GetString(bytes, offset, end - offset);
			offset = end + 1;
		}
		return result;
	}

	private static (int Offset, int Count) TagOffset(byte[] bytes, int header, int tag)
	{
		var count = ReadInt(bytes, header + 8);
		var offset = Assert.Single(Enumerable.Range(0, count).Select(index => header + 16 + index * 16), offset => ReadInt(bytes, offset) == tag);
		return (header + 16 + count * 16 + ReadInt(bytes, offset + 8), ReadInt(bytes, offset + 12));
	}

	private static int HeaderEnd(byte[] bytes, int offset, bool align)
	{
		var size = 16 + ReadInt(bytes, offset + 8) * 16 + ReadInt(bytes, offset + 12);
		return offset + (align ? (size + 7) & ~7 : size);
	}

	private static int ReadInt(byte[] bytes, int offset) => BinaryPrimitives.ReadInt32BigEndian(bytes.AsSpan(offset, 4));

	private static string NormalizeName(string name) => (name.StartsWith("./", StringComparison.Ordinal) ? name[2..] : name).TrimStart('/');
	#endregion

	#region 嵌套结构
	private sealed record ArchiveFile(string Name, byte[] Content, UnixFileMode Mode, long Size);
	#endregion
}
