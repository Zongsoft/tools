using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Formats.Tar;
using System.IO.Compression;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Runtime.InteropServices;

using Xunit;

namespace Zongsoft.Tools.Packager.Tests;

public sealed class PackageRpmTest
{
	#region 测试方法
	[Fact]
	public void Rpm_GzipPayload_DeclaresSupportedRpmlibRequirementsAndAlignedVersions()
	{
		using var directory = new MigrationTestDirectory();
		const string SERVICE_NAME = "zongsoft.daemon.service";
		var source = directory.Write(SERVICE_NAME, "[Unit]\nDescription=Zongsoft Daemon\n[Service]\nExecStart=/bin/true\n");
		var package = Create("rpm", directory.Path, SERVICE_NAME);
		package.Dependencies = ["dotnet-runtime-10.0 >= 10.0"];
		package.Scriptor.Script();

		package.Pack(directory.Path, true);

		var bytes = File.ReadAllBytes(Directory.GetFiles(directory.Path, "*.rpm").Single());
		var main = RpmHeaderEnd(bytes, 96, true);
		var names = RpmStrings(bytes, main, 1049);
		var versions = RpmStrings(bytes, main, 1050);
		var flags = RpmIndex(bytes, main, 1048);
		Assert.Equal(4, flags.Type);
		Assert.Equal(names.Length, flags.Count);
		Assert.Equal(new[] { "rpmlib(CompressedFileNames)", "rpmlib(FileDigests)", "rpmlib(PayloadFilesHavePrefix)", "dotnet-runtime-10.0" }, names);
		Assert.Equal(new[] { "3.0.4-1", "4.6.0-1", "4.0-1", "10.0" }, versions);
		Assert.Equal(new[] { 0x0100000A, 0x0100000A, 0x0100000A, 0x0000000C }, Enumerable.Range(0, flags.Count).Select(index => ReadInt(bytes, flags.Offset + index * 4)));
		Assert.DoesNotContain("rpmlib(PayloadIsGzip)", names);
		Assert.Equal("cpio", Assert.Single(RpmStrings(bytes, main, 1124)));
		Assert.Equal("gzip", Assert.Single(RpmStrings(bytes, main, 1125)));
		var baseNames = RpmStrings(bytes, main, 1117);
		var directories = RpmStrings(bytes, main, 1118);
		var directoryIndexes = RpmIndex(bytes, main, 1116);
		Assert.Equal(4, directoryIndexes.Type);
		Assert.Equal(baseNames.Length, directoryIndexes.Count);
		var serviceIndex = Array.IndexOf(baseNames, SERVICE_NAME);
		Assert.True(serviceIndex >= 0);
		var directoryIndex = ReadInt(bytes, directoryIndexes.Offset + serviceIndex * 4);
		Assert.Equal(package.InstallPath + "/" + SERVICE_NAME, directories[directoryIndex] + baseNames[serviceIndex]);

		var payloadOffset = RpmHeaderEnd(bytes, main, false);
		Assert.Equal(new byte[] { 0x1f, 0x8b, 0x08 }, bytes[payloadOffset..(payloadOffset + 3)]);
		using var compressed = new MemoryStream(bytes, payloadOffset, bytes.Length - payloadOffset);
		using var gzip = new GZipStream(compressed, CompressionMode.Decompress);
		using var uncompressed = new MemoryStream();
		gzip.CopyTo(uncompressed);
		var cpio = uncompressed.ToArray();
		Assert.Equal("070701", Encoding.ASCII.GetString(cpio, 0, 6));
		var nameLength = Convert.ToInt32(Encoding.ASCII.GetString(cpio, 94, 8), 16);
		Assert.StartsWith("./", Encoding.UTF8.GetString(cpio, 110, nameLength - 1));
		var payload = ReadPayload(directory.Path, "rpm");
		var service = Assert.Single(payload, entry => entry.Key.EndsWith("/" + SERVICE_NAME, StringComparison.Ordinal));
		Assert.Equal(File.ReadAllBytes(source), service.Value);

	}

	[Fact]
	public void Rpm_ImmutableRegionsAndDigests_CoverActualHeaderPayloadAndFileBytes()
	{
		using var directory = new MigrationTestDirectory();
		var package = Create("rpm", directory.Path, "disabled");
		directory.Write("payload/schema.sql", "CREATE TABLE samples (id INTEGER);\n");
		directory.Write("payload/empty.sql", "");
		package.Entries.Load(directory.Path, ["payload/**"]);
		package.Scriptor.Script();

		package.Pack(directory.Path, true);

		var bytes = File.ReadAllBytes(Directory.GetFiles(directory.Path, "*.rpm").Single());
		const int SIGNATURE = 96;
		var main = RpmHeaderEnd(bytes, SIGNATURE, true);
		var payloadOffset = RpmHeaderEnd(bytes, main, false);
		Region(SIGNATURE, 62);
		Region(main, 63);
		Assert.All(bytes[RpmHeaderEnd(bytes, SIGNATURE, false)..main], value => Assert.Equal((byte)0, value));
		Assert.Equal(0, main % 8);

		var header = bytes[main..payloadOffset];
		var payload = bytes[payloadOffset..];
		var body = bytes[main..];
		Assert.Equal(6, RpmIndex(bytes, SIGNATURE, 269).Type);
		Assert.Equal(6, RpmIndex(bytes, SIGNATURE, 273).Type);
		Assert.Equal(Convert.ToHexString(SHA1.HashData(header)).ToLowerInvariant(), Assert.Single(RpmStrings(bytes, SIGNATURE, 269)));
		Assert.Equal(Convert.ToHexString(SHA256.HashData(header)).ToLowerInvariant(), Assert.Single(RpmStrings(bytes, SIGNATURE, 273)));
		Assert.Equal(body.Length, Scalar(SIGNATURE, 1000));
		var md5 = RpmIndex(bytes, SIGNATURE, 1004);
		Assert.Equal(7, md5.Type);
		Assert.Equal(16, md5.Count);
		Assert.Equal(MD5.HashData(body), bytes[md5.Offset..(md5.Offset + md5.Count)]);
		var signatureTags = Enumerable.Range(0, ReadInt(bytes, SIGNATURE + 8)).Select(index => ReadInt(bytes, SIGNATURE + 16 + index * 16)).ToArray();
		Assert.DoesNotContain(257, signatureTags);
		Assert.DoesNotContain(261, signatureTags);
		Assert.DoesNotContain(272, signatureTags);

		Assert.Equal(8, Scalar(main, 5093));
		Assert.Equal(8, RpmIndex(bytes, main, 5092).Type);
		Assert.Equal(Convert.ToHexString(SHA256.HashData(payload)).ToLowerInvariant(), Assert.Single(RpmStrings(bytes, main, 5092)));
		Assert.Equal(8, Scalar(main, 5011));
		var digests = RpmStrings(bytes, main, 1035);
		var baseNames = RpmStrings(bytes, main, 1117);
		var directories = RpmStrings(bytes, main, 1118);
		var indexes = RpmIndex(bytes, main, 1116);
		var modes = RpmIndex(bytes, main, 1030);
		Assert.Equal(baseNames.Length, digests.Length);
		Assert.Equal(baseNames.Length, indexes.Count);
		Assert.Equal(baseNames.Length, modes.Count);
		Assert.Equal(3, modes.Type);
		var archive = ReadPayload(directory.Path, "rpm");
		var regularFiles = 0;
		for(var index = 0; index < baseNames.Length; index++)
		{
			var mode = BinaryPrimitives.ReadUInt16BigEndian(bytes.AsSpan(modes.Offset + index * 2, 2));
			if((mode & 0xF000) == 0x4000)
			{
				Assert.Empty(digests[index]);
				continue;
			}
			Assert.Equal(0x8000, mode & 0xF000);
			var name = directories[ReadInt(bytes, indexes.Offset + index * 4)] + baseNames[index];
			Assert.Equal(Convert.ToHexString(SHA256.HashData(archive[name.TrimStart('/')])).ToLowerInvariant(), digests[index]);
			regularFiles++;
		}
		Assert.Equal(package.Entries.Count(entry => !entry.IsDirectory), regularFiles);
		var empty = Array.IndexOf(baseNames, "empty.sql");
		Assert.True(empty >= 0);
		Assert.Equal("e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855", digests[empty]);

		int Scalar(int offset, int tag)
		{
			var entry = RpmIndex(bytes, offset, tag);
			Assert.Equal(4, entry.Type);
			Assert.Equal(1, entry.Count);
			return ReadInt(bytes, entry.Offset);
		}

		void Region(int offset, int expectedTag)
		{
			var count = ReadInt(bytes, offset + 8);
			var size = ReadInt(bytes, offset + 12);
			var store = offset + 16 + count * 16;
			var tags = Enumerable.Range(0, count).Select(index => ReadInt(bytes, offset + 16 + index * 16)).ToArray();
			Assert.Equal(expectedTag, tags[0]);
			Assert.Equal(tags.Order(), tags);
			Assert.Equal(count, tags.Distinct().Count());
			var cursor = 0;
			for(var index = 1; index < count; index++)
			{
				var entry = offset + 16 + index * 16;
				var type = ReadInt(bytes, entry + 4);
				var position = ReadInt(bytes, entry + 8);
				var items = ReadInt(bytes, entry + 12);
				var alignment = type switch { 3 => 2, 4 => 4, 5 => 8, _ => 1 };
				var expected = (cursor + alignment - 1) & ~(alignment - 1);
				Assert.Equal(expected, position);
				Assert.All(bytes[(store + cursor)..(store + expected)], value => Assert.Equal((byte)0, value));
				Assert.InRange(position, 0, size - 16);
				Assert.True(items >= 0);
				if(type is 6 or 8 or 9)
				{
					if(type == 6)
						Assert.Equal(1, items);
					cursor = position;
					for(var item = 0; item < items; item++)
					{
						var end = Array.IndexOf(bytes, (byte)0, store + cursor, size - 16 - cursor);
						Assert.True(end >= store + cursor);
						cursor = end + 1 - store;
					}
				}
				else
				{
					var width = type switch { 1 or 2 or 7 => 1, 3 => 2, 4 => 4, 5 => 8, _ => throw new InvalidDataException("Unexpected RPM field type.") };
					cursor = position + items * width;
				}
				Assert.InRange(cursor, position, size - 16);
			}
			Assert.Equal(size - 16, cursor);
			var region = RpmIndex(bytes, offset, expectedTag);
			Assert.Equal(7, region.Type);
			Assert.Equal(16, region.Count);
			Assert.Equal(store + size - 16, region.Offset);
			Assert.Equal(expectedTag, ReadInt(bytes, region.Offset));
			Assert.Equal(7, ReadInt(bytes, region.Offset + 4));
			Assert.Equal(-count * 16, ReadInt(bytes, region.Offset + 8));
			Assert.Equal(16, ReadInt(bytes, region.Offset + 12));
		}
	}

	[Fact]
	public void Rpm_VaryingMetadataLengths_GzipStartsImmediatelyAfterDeclaredMainHeader()
	{
		var observedAlignment = new HashSet<int>();
		var observedHeaderSizes = new HashSet<int>();
		const string SERVICE_NAME = "zongsoft.migration.test.service";
		const string SERVICE = "[Unit]\nDescription=RPM boundary fixture\n[Service]\nExecStart=/bin/true\n";

		foreach(var metadataLength in new[] { 0, 1, 2, 3, 4, 5, 6, 7, 8, 15 })
		{
			using var directory = new MigrationTestDirectory();
			var source = directory.Write(SERVICE_NAME, SERVICE);
			var package = Create("rpm", directory.Path, SERVICE_NAME);
			package.Summary = "Host " + new string('s', metadataLength);
			package.Description = "Zongsoft host " + new string('d', metadataLength * 2);
			package.Scriptor.Script();

			package.Pack(directory.Path, true);

			var bytes = File.ReadAllBytes(Directory.GetFiles(directory.Path, "*.rpm").Single());
			var mainHeader = RpmHeaderEnd(bytes, 96, true);
			var declaredEnd = RpmHeaderEnd(bytes, mainHeader, false);
			observedAlignment.Add(declaredEnd % 8);
			observedHeaderSizes.Add(declaredEnd - mainHeader);
			Assert.Equal(new byte[] { 0x1f, 0x8b, 0x08 }, bytes[declaredEnd..(declaredEnd + 3)]);
			var payload = ReadPayload(directory.Path, "rpm");
			var entry = Assert.Single(payload, pair => pair.Key.EndsWith("/" + SERVICE_NAME, StringComparison.Ordinal));
			Assert.Equal(File.ReadAllBytes(source), entry.Value);
		}
		Assert.Contains(4, observedAlignment);
		Assert.True(observedHeaderSizes.Count > 1, "Metadata variants must exercise multiple declared header lengths.");
	}

	#endregion

	#region 辅助方法
	private static Package Create(string format, string source, string daemon, string installed = null)
	{
		var variables = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["framework"] = "net10.0", ["source"] = source, ["daemon"] = daemon };
		if(installed != null)
			variables["installed"] = installed;
		var resolved = new Variables(variables);
		Package package = format switch
		{
			"tar" => new Package.Tar("Zongsoft.Migration.TestHost", null, new Version(1, 1, 0), Platform.Linux, Architecture.X64, resolved),
			"deb" => new Package.Deb("Zongsoft.Migration.TestHost", null, new Version(1, 1, 0), Platform.Linux, Architecture.X64, resolved),
			"rpm" => new Package.Rpm("Zongsoft.Migration.TestHost", null, new Version(1, 1, 0), Platform.Linux, Architecture.X64, resolved),
			_ => throw new ArgumentOutOfRangeException(nameof(format)),
		};
		package.InstallPath = "/opt/zongsoft/migration-test";
		return package;
	}

	private static void AssertOrdered(string script, params string[] fragments)
	{
		var previous = -1;
		foreach(var fragment in fragments)
		{
			var index = script.IndexOf(fragment, StringComparison.Ordinal);
			Assert.True(index > previous, $"Expected '{fragment}' after position {previous}; got {index}.\n{script}");
			previous = index;
		}
	}

	private static string ReadInstalled(string directory, string format)
	{
		var file = Directory.GetFiles(directory, format == "tar" ? "*.tar.gz" : "*." + format).Single();
		if(format == "tar")
			return Encoding.UTF8.GetString(ReadTar(File.ReadAllBytes(file))["install.sh"]);
		if(format == "deb")
			return Encoding.UTF8.GetString(ReadTar(ReadAr(file)["control.tar.gz"])["postinst"]);
		var bytes = File.ReadAllBytes(file);
		var start = RpmHeaderEnd(bytes, 96, true);
		var count = ReadInt(bytes, start + 8);
		var store = start + 16 + count * 16;

		for(var i = 0; i < count; i++)
		{
			var entry = start + 16 + i * 16;
			if(ReadInt(bytes, entry) != 1024)
				continue;
			var offset = store + ReadInt(bytes, entry + 8);
			return Encoding.UTF8.GetString(bytes, offset, Array.IndexOf(bytes, (byte)0, offset) - offset);
		}
		throw new InvalidDataException("RPM post-install tag missing.");
	}

	private static Dictionary<string, byte[]> ReadPayload(string directory, string format)
	{
		var file = Directory.GetFiles(directory, format == "tar" ? "*.tar.gz" : "*." + format).Single();
		if(format == "tar")
			return ReadTar(File.ReadAllBytes(file));
		if(format == "deb")
			return ReadTar(ReadAr(file)["data.tar.gz"]);
		var bytes = File.ReadAllBytes(file);
		var offset = RpmHeaderEnd(bytes, RpmHeaderEnd(bytes, 96, true), false);
		using var compressed = new MemoryStream(bytes, offset, bytes.Length - offset);
		using var gzip = new GZipStream(compressed, CompressionMode.Decompress);
		using var content = new MemoryStream();
		gzip.CopyTo(content);
		bytes = content.ToArray();
		offset = 0;
		var entries = new Dictionary<string, byte[]>(StringComparer.Ordinal);

		while(offset + 110 <= bytes.Length)
		{
			Assert.Equal("070701", Encoding.ASCII.GetString(bytes, offset, 6));
			var size = Convert.ToInt32(Encoding.ASCII.GetString(bytes, offset + 54, 8), 16);
			var nameSize = Convert.ToInt32(Encoding.ASCII.GetString(bytes, offset + 94, 8), 16);
			var name = Encoding.UTF8.GetString(bytes, offset + 110, nameSize - 1).TrimStart('.', '/');
			offset = (offset + 110 + nameSize + 3) & ~3;

			if(name == "TRAILER!!!")
				break;
			entries.Add(name, bytes[offset..(offset + size)]);
			offset = (offset + size + 3) & ~3;
		}
		return entries;
	}

	private static Dictionary<string, byte[]> ReadAr(string path)
	{
		var bytes = File.ReadAllBytes(path);
		var result = new Dictionary<string, byte[]>();
		var offset = 8;
		Assert.Equal("!<arch>\n", Encoding.ASCII.GetString(bytes, 0, 8));
		while(offset + 60 <= bytes.Length)
		{
			var name = Encoding.ASCII.GetString(bytes, offset, 16).Trim().TrimEnd('/');
			var size = int.Parse(Encoding.ASCII.GetString(bytes, offset + 48, 10).Trim(), System.Globalization.CultureInfo.InvariantCulture);
			result.Add(name, bytes[(offset + 60)..(offset + 60 + size)]);
			offset += 60 + size + (size & 1);
		}
		return result;
	}

	private static Dictionary<string, byte[]> ReadTar(byte[] archive)
	{
		using var memory = new MemoryStream(archive);
		using var gzip = new GZipStream(memory, CompressionMode.Decompress);
		using var reader = new TarReader(gzip);
		var result = new Dictionary<string, byte[]>(StringComparer.Ordinal);
		TarEntry entry;
		while((entry = reader.GetNextEntry()) != null)
		{
			if(entry.DataStream == null)
				continue;
			using var content = new MemoryStream();
			entry.DataStream.CopyTo(content);
			result.Add(entry.Name, content.ToArray());
		}
		return result;
	}

	private static (int Type, int Count, int Offset) RpmIndex(byte[] bytes, int header, int tag)
	{
		var count = ReadInt(bytes, header + 8);
		var entry = Assert.Single(Enumerable.Range(0, count).Select(index => header + 16 + index * 16), offset => ReadInt(bytes, offset) == tag);
		return (ReadInt(bytes, entry + 4), ReadInt(bytes, entry + 12), header + 16 + count * 16 + ReadInt(bytes, entry + 8));
	}

	private static string[] RpmStrings(byte[] bytes, int header, int tag)
	{
		var entry = RpmIndex(bytes, header, tag);
		Assert.Contains(entry.Type, new[] { 6, 8 });
		var result = new string[entry.Count];
		var offset = entry.Offset;
		for(var index = 0; index < result.Length; index++)
		{
			var end = Array.IndexOf(bytes, (byte)0, offset);
			Assert.True(end >= offset);
			result[index] = Encoding.UTF8.GetString(bytes, offset, end - offset);
			offset = end + 1;
		}
		return result;
	}

	private static int RpmHeaderEnd(byte[] bytes, int offset, bool align)
	{
		Assert.Equal(new byte[] { 0x8e, 0xad, 0xe8, 0x01 }, bytes[offset..(offset + 4)]);
		var size = 16 + ReadInt(bytes, offset + 8) * 16 + ReadInt(bytes, offset + 12);
		return offset + (align ? (size + 7) & ~7 : size);
	}

	private static int ReadInt(byte[] bytes, int offset) => BinaryPrimitives.ReadInt32BigEndian(bytes.AsSpan(offset, 4));
	#endregion
}
