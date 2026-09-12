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

using Zongsoft.Tools.Packager.Migration;
using Xunit;

namespace Zongsoft.Tools.Packager.Tests;

public sealed class PackageArtifactTests
{
	#region 目录与归档
	[Theory]
	[InlineData("tar")]
	[InlineData("deb")]
	[InlineData("rpm")]
	public void Package_DirectoriesAndAliases_PreserveTypesContentAndPermissions(string format)
	{
		using var directory = new MigrationTestDirectory();
		var file = directory.Write("source/data/payload.bin", "");
		var content = Enumerable.Range(0, 8193).Select(index => (byte)(index * 17)).ToArray();
		File.WriteAllBytes(file, content);
		var source = Path.Combine(directory.Path, "source");
		var empty = Directory.CreateDirectory(Path.Combine(source, "data", "empty"));
		var configuration = directory.Write("source/config/settings.conf", "hosting configuration");
		Directory.CreateDirectory(Path.Combine(source, "config", "empty"));
		var mode = OperatingSystem.IsWindows() ? (UnixFileMode)493 : (UnixFileMode)488;
		if(!OperatingSystem.IsWindows()) File.SetUnixFileMode(empty.FullName, mode);
		var package = CreatePackage(format, source);
		package.Entries.Load(source, ["data", "config:/etc/zongsoft-artifact"]);
		var output = Directory.CreateDirectory(Path.Combine(directory.Path, "output")).FullName;

		package.Pack(output, true);

		var archivePath = Path.Combine(output, package.FileName);
		var entries = ReadArchive(archivePath, format);
		var prefix = format == "tar" ? "" : "opt/zongsoft/web/";
		var rootPrefix = format == "tar" ? ".root/" : "";
		var data = Assert.Single(entries, item => item.Name == prefix + "data/payload.bin");
		Assert.False(data.IsDirectory);
		Assert.Equal(content, data.Content);
		Assert.Equal(content.LongLength, data.Size);
		var emptyEntry = Assert.Single(entries, item => item.Name == prefix + "data/empty");
		Assert.True(emptyEntry.IsDirectory);
		Assert.Empty(emptyEntry.Content);
		Assert.Equal(mode, emptyEntry.Mode);
		Assert.Equal(0, emptyEntry.Size);
		Assert.True(Assert.Single(entries, item => item.Name == prefix + "data").IsDirectory);
		Assert.True(Assert.Single(entries, item => item.Name == rootPrefix + "etc/zongsoft-artifact/empty").IsDirectory);
		Assert.Equal(File.ReadAllBytes(configuration), Assert.Single(entries, item => item.Name == rootPrefix + "etc/zongsoft-artifact/settings.conf").Content);
		Assert.Equal(entries.Count, entries.Select(item => item.Name).Distinct(StringComparer.Ordinal).Count());
		if(format == "rpm") AssertRpmDigest(archivePath, "/opt/zongsoft/web/data/", "payload.bin", content);
		if(format == "deb")
		{
			var conffiles = ReadControl(archivePath, "conffiles");
			Assert.Equal("/etc/zongsoft-artifact/settings.conf\n", conffiles);
		}
	}

	[Theory]
	[InlineData("tar")]
	[InlineData("deb")]
	[InlineData("rpm")]
	public void Package_GeneratedParents_HaveDefaultPermissions(string format)
	{
		using var directory = new MigrationTestDirectory();
		directory.Write("source/payload.txt", "generated ancestors");
		var source = Path.Combine(directory.Path, "source");
		var package = CreatePackage(format, source);
		package.Entries.Load(source, ["payload.txt:nested/deep/payload.txt"]);
		var output = Directory.CreateDirectory(Path.Combine(directory.Path, "output")).FullName;

		package.Pack(output, true);

		var entries = ReadArchive(Path.Combine(output, package.FileName), format);
		var prefix = format == "tar" ? "" : "opt/zongsoft/web/";
		foreach(var name in new[] { "nested", "nested/deep" })
		{
			var entry = Assert.Single(entries, item => item.Name == prefix + name);
			Assert.True(entry.IsDirectory);
			Assert.Equal((UnixFileMode)493, entry.Mode);
		}
		Assert.Equal("generated ancestors", Encoding.UTF8.GetString(Assert.Single(entries, item => item.Name == prefix + "nested/deep/payload.txt").Content));
	}

	[Theory]
	[InlineData(false)]
	[InlineData(true)]
	public void Entries_FileDirectoryCollision_RejectsBothInputOrders(bool directoryFirst)
	{
		using var directory = new MigrationTestDirectory();
		directory.Write("payload.txt", "payload");
		Directory.CreateDirectory(Path.Combine(directory.Path, "empty"));
		var package = CreatePackage("tar", directory.Path);
		string[] arguments = directoryFirst ? ["empty:target", "payload.txt:target"] : ["payload.txt:target", "empty:target"];

		Assert.Throws<InvalidOperationException>(() => package.Entries.Load(directory.Path, arguments));
		Assert.Single(package.Entries, entry => entry.EntryName == "target");
	}

	[Fact]
	public void Entries_RecursiveSelection_PreservesSourceDirectoryMode()
	{
		using var directory = new MigrationTestDirectory();
		var empty = Directory.CreateDirectory(Path.Combine(directory.Path, "restricted"));
		var mode = OperatingSystem.IsWindows() ? (UnixFileMode)493 : (UnixFileMode)448;
		if(!OperatingSystem.IsWindows()) File.SetUnixFileMode(empty.FullName, mode);
		var package = CreatePackage("tar", directory.Path);

		package.Entries.Load(directory.Path, []);

		var entry = Assert.Single(package.Entries, item => item.EntryName == "restricted");
		Assert.True(entry.IsDirectory);
		Assert.Equal(mode, entry.Mode);
		Assert.Equal(0, entry.Size);
		Assert.Throws<InvalidOperationException>(() => entry.OpenRead());
	}

	[Fact]
	public void Tar_RootDirectoryAlias_UsesNonrecursiveRemoval()
	{
		using var directory = new MigrationTestDirectory();
		directory.Write("source/config/settings.conf", "settings");
		Directory.CreateDirectory(Path.Combine(directory.Path, "source", "config", "empty"));
		var source = Path.Combine(directory.Path, "source");
		var package = CreatePackage("tar", source);
		package.Entries.Load(source, ["config:/etc/zongsoft-artifact"]);
		var output = Directory.CreateDirectory(Path.Combine(directory.Path, "output")).FullName;

		package.Pack(output, true);

		var entries = ReadArchive(Path.Combine(output, package.FileName), "tar");
		var install = Encoding.UTF8.GetString(Assert.Single(entries, entry => entry.Name == "install.sh").Content);
		var uninstall = Encoding.UTF8.GetString(Assert.Single(entries, entry => entry.Name == "uninstall.sh").Content);
		Assert.Contains("install -d", install);
		Assert.Contains("[ -d \"$(dirname -- \"$root_target\")\" ] || install -d", install);
		Assert.Contains("rmdir", uninstall);
		Assert.Contains("etc/zongsoft-artifact/empty", uninstall);
		Assert.DoesNotContain(uninstall.Split('\n'), line => line.Contains("rm -rf", StringComparison.Ordinal) && line.Contains("zongsoft-artifact", StringComparison.Ordinal));
	}
	#endregion

	#region 文件匹配
	[Fact]
	public void Entries_RecursiveGlob_PreservesArgumentOrderAndRelativePaths()
	{
		using var directory = new MigrationTestDirectory();
		directory.Write("tail.txt", "tail");
		directory.Write("items/b/deep/02.txt", "second nested");
		directory.Write("items/a/01.txt", "first");
		directory.Write("items/b/01.txt", "second");
		directory.Write("items/b/ignored.log", "excluded by extension");
		var package = CreatePackage("tar", directory.Path);

		package.Entries.Load(directory.Path, ["tail.txt", "items/*/**/0?.txt"]);

		Assert.Equal(new[] { "tail.txt", "items/a/01.txt", "items/b/01.txt", "items/b/deep/02.txt" }, package.Entries.Where(entry => !entry.IsDirectory).Select(entry => entry.EntryName));
		Assert.DoesNotContain(package.Entries, entry => entry.EntryName.EndsWith("ignored.log", StringComparison.Ordinal));
	}

	[Fact]
	public void Migration_RecursiveGlob_PreservesSqlOrderAndIndependentTasks()
	{
		using var directory = new MigrationTestDirectory();
		directory.Write("sqlite.env", "Database=/tmp/zongsoft-artifact-hosting.db\n");
		directory.Write("first.ini", "[sqlite]\nsql/z.sql\nsql/**/0?.sql\n");
		directory.Write("parts/b/db.ini", "[sqlite]\n../../sql/z.sql\n");
		directory.Write("parts/a/db.ini", "[sqlite]\n../../sql/A/01.sql\n");
		directory.Write("sql/z.sql", "SELECT 'explicit';");
		directory.Write("sql/B/deep/02.sql", "SELECT 'nested';");
		directory.Write("sql/A/01.sql", "SELECT 'first';");

		var plan = new MigrationLoader(null).Load("first.ini;parts/*/db.ini;first.ini", directory.Path, "zongsoft.daemon", "1.0.0");

		Assert.Equal(4, plan.Tasks.Count);
		Assert.Equal(new[] { "SELECT 'explicit';", "SELECT 'first';", "SELECT 'nested';" }, plan.Tasks[0].Scripts.Select(script => script.Content));
		Assert.Equal("SELECT 'first';", Assert.Single(plan.Tasks[1].Scripts).Content);
		Assert.Equal("SELECT 'explicit';", Assert.Single(plan.Tasks[2].Scripts).Content);
		Assert.Equal(plan.Tasks[0].Scripts.Select(script => script.Content), plan.Tasks[3].Scripts.Select(script => script.Content));
		var scripts = plan.Tasks.SelectMany(task => task.Scripts).ToArray();
		Assert.Equal(Enumerable.Range(1, 8).Select(index => $".migration/.artifacts/sqlite/{index:D4}.sql"), scripts.Select(script => script.Path));
		Assert.Equal(4, plan.Tasks.Select(task => task.Id).Distinct(StringComparer.Ordinal).Count());
		Assert.All(scripts, script => Assert.Equal(Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(script.Content))), script.Checksum));
	}
	#endregion

	#region 流式载荷
	[Theory]
	[InlineData("deb")]
	[InlineData("rpm")]
	public void Package_LargePayload_BoundsManagedAllocationsAndCleansTemporaryFiles(string format)
	{
		using var directory = new MigrationTestDirectory();
		var baseline = GetTemporaryBuffers();
		var file = directory.Write("source/payload.bin", "warmup");
		var source = Path.Combine(directory.Path, "source");
		var output = Directory.CreateDirectory(Path.Combine(directory.Path, "output")).FullName;
		var warmup = CreatePackage(format, source);
		warmup.Entries.Load(source, ["payload.bin"]);
		warmup.Pack(output, true);
		Assert.Equal(baseline, GetTemporaryBuffers());
		const int size = 64 * 1024 * 1024;
		var block = new byte[1024 * 1024];
		new Random(1751).NextBytes(block);
		using(var stream = File.Create(file))
		{
			for(var offset = 0; offset < size; offset += block.Length)
				stream.Write(block);
		}
		var package = CreatePackage(format, source);
		package.Entries.Load(source, ["payload.bin"]);

		var before = GC.GetAllocatedBytesForCurrentThread();
		package.Pack(output, true);
		var allocated = GC.GetAllocatedBytesForCurrentThread() - before;

		Assert.True(allocated < 32L * 1024 * 1024, $"Allocated {allocated:N0} managed bytes while packaging {size:N0} payload bytes.");
		Assert.Equal(baseline, GetTemporaryBuffers());
		var archivePath = Path.Combine(output, package.FileName);
		var payload = Assert.Single(ReadArchive(archivePath, format), entry => entry.Name == "opt/zongsoft/web/payload.bin");
		Assert.Equal(size, payload.Size);
		Assert.Equal(size, payload.Content.Length);
		using var original = File.OpenRead(file);
		Assert.Equal(SHA256.HashData(original), SHA256.HashData(payload.Content));
		if(format == "rpm") AssertRpmDigest(archivePath, "/opt/zongsoft/web/", "payload.bin", payload.Content);
	}

	[Theory]
	[InlineData("deb")]
	[InlineData("rpm")]
	public void Package_SelectedSourceDisappears_CleansTemporaryFilesOnFailure(string format)
	{
		using var directory = new MigrationTestDirectory();
		var file = directory.Write("source/payload.bin", "selected before deletion");
		var source = Path.Combine(directory.Path, "source");
		var output = Directory.CreateDirectory(Path.Combine(directory.Path, "output")).FullName;
		var package = CreatePackage(format, source);
		package.Entries.Load(source, ["payload.bin"]);
		var baseline = GetTemporaryBuffers();
		File.Delete(file);

		Assert.Throws<FileNotFoundException>(() => package.Pack(output, true));

		Assert.Equal(baseline, GetTemporaryBuffers());
	}
	#endregion

	#region Debian 关系
	[Fact]
	public void Debian_Relationships_WriteDistinctControlFields()
	{
		using var directory = new MigrationTestDirectory();
		var package = (Package.Deb)CreatePackage("deb", directory.Path);
		package.Provides = ["zongsoft-hosting (= 1.0)"];
		package.Replaces = ["zongsoft-hosting-old (<< 2.0)"];
		package.Breaks = ["zongsoft-hosting-core (<= 1.0)"];
		package.Conflicts = ["zongsoft-hosting-legacy"];
		package.Recommends = ["mysql-client | mariadb-client", "curl (>= 7.0)"];
		package.Suggests = ["redis-tools | memcached"];

		package.Pack(directory.Path, true);

		var control = ReadControl(Path.Combine(directory.Path, package.FileName), "control");
		Assert.Contains("\nProvides: zongsoft-hosting (= 1.0)\n", control);
		Assert.Contains("\nReplaces: zongsoft-hosting-old (<< 2.0)\n", control);
		Assert.Contains("\nBreaks: zongsoft-hosting-core (<= 1.0)\n", control);
		Assert.Contains("\nConflicts: zongsoft-hosting-legacy\n", control);
		Assert.Contains("\nRecommends: mysql-client | mariadb-client, curl (>= 7.0)\n", control);
		Assert.Contains("\nSuggests: redis-tools | memcached\n", control);
		Assert.Contains("\nVersion: 1.0.0\n", control);
	}

	[Theory]
	[InlineData("Provides", "valid\nInjected: yes")]
	[InlineData("Provides", "name (>= 1.0)")]
	[InlineData("Replaces", "valid\rInjected: yes")]
	[InlineData("Breaks", "name (>=)")]
	[InlineData("Conflicts", "name;other")]
	[InlineData("Conflicts", "name | other")]
	[InlineData("Recommends", "name || other")]
	[InlineData("Suggests", "NAME")]
	public void Debian_InvalidRelationships_RejectsMalformedControlValues(string field, string value)
	{
		using var directory = new MigrationTestDirectory();
		var package = (Package.Deb)CreatePackage("deb", directory.Path);
		switch(field)
		{
			case "Provides": package.Provides = [value]; break;
			case "Replaces": package.Replaces = [value]; break;
			case "Breaks": package.Breaks = [value]; break;
			case "Conflicts": package.Conflicts = [value]; break;
			case "Recommends": package.Recommends = [value]; break;
			case "Suggests": package.Suggests = [value]; break;
		}

		Assert.Throws<InvalidDataException>(() => package.Pack(directory.Path, true));
	}
	#endregion

	#region 辅助方法
	private static string[] GetTemporaryBuffers() => Directory.GetFiles(Path.GetTempPath(), "zongsoft-packager-*").Order(StringComparer.Ordinal).ToArray();

	private static Package CreatePackage(string format, string source)
	{
		Normalizer.Initialize(new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
		{
			["source"] = source,
			["framework"] = "net10.0",
			["daemon"] = "disabled",
		});
		Package package = format switch
		{
			"tar" => new Package.Tar("zongsoft.web", null, new Version(1, 0, 0), Platform.Linux, Architecture.X64),
			"deb" => new Package.Deb("zongsoft.web", null, new Version(1, 0, 0), Platform.Linux, Architecture.X64),
			_ => new Package.Rpm("zongsoft.web", null, new Version(1, 0, 0), Platform.Linux, Architecture.X64),
		};
		package.InstallPath = "/opt/zongsoft/web";
		package.Scripts = new(":", ":", ":", ":");
		return package;
	}

	private static List<ArchiveEntry> ReadArchive(string path, string format)
	{
		var bytes = File.ReadAllBytes(path);
		if(format == "tar") return ReadTar(bytes);
		if(format == "deb") return ReadTar(ReadAr(bytes, "data.tar.gz"));
		var payload = HeaderEnd(bytes, HeaderEnd(bytes, 96, true), false);
		using var stream = new MemoryStream(bytes, payload, bytes.Length - payload);
		using var gzip = new GZipStream(stream, CompressionMode.Decompress);
		using var copy = new MemoryStream();
		gzip.CopyTo(copy);
		bytes = copy.ToArray();
		var entries = new List<ArchiveEntry>();
		for(var offset = 0; offset + 110 <= bytes.Length;)
		{
			Assert.Equal("070701", Encoding.ASCII.GetString(bytes, offset, 6));
			var mode = Convert.ToInt32(Encoding.ASCII.GetString(bytes, offset + 14, 8), 16);
			var size = Convert.ToInt32(Encoding.ASCII.GetString(bytes, offset + 54, 8), 16);
			var nameSize = Convert.ToInt32(Encoding.ASCII.GetString(bytes, offset + 94, 8), 16);
			var name = NormalizeName(Encoding.UTF8.GetString(bytes, offset + 110, nameSize - 1));
			offset = (offset + 110 + nameSize + 3) & ~3;
			if(name == "TRAILER!!!") break;
			entries.Add(new(name, bytes[offset..(offset + size)], (UnixFileMode)(mode & 0xFFF), size, (mode & 0xF000) == 0x4000));
			offset = (offset + size + 3) & ~3;
		}
		return entries;
	}

	private static byte[] ReadAr(byte[] bytes, string target)
	{
		Assert.Equal("!<arch>\n", Encoding.ASCII.GetString(bytes, 0, 8));
		for(var offset = 8; offset + 60 <= bytes.Length;)
		{
			var name = Encoding.ASCII.GetString(bytes, offset, 16).Trim().TrimEnd('/');
			var size = int.Parse(Encoding.ASCII.GetString(bytes, offset + 48, 10).Trim(), System.Globalization.CultureInfo.InvariantCulture);
			if(name == target) return bytes[(offset + 60)..(offset + 60 + size)];
			offset += 60 + size + (size & 1);
		}
		throw new InvalidDataException("Missing Debian member: " + target);
	}

	private static string ReadControl(string path, string name) => Encoding.UTF8.GetString(Assert.Single(ReadTar(ReadAr(File.ReadAllBytes(path), "control.tar.gz")), entry => entry.Name == name).Content);

	private static List<ArchiveEntry> ReadTar(byte[] bytes)
	{
		using var stream = new MemoryStream(bytes);
		using var gzip = new GZipStream(stream, CompressionMode.Decompress);
		using var reader = new TarReader(gzip);
		var entries = new List<ArchiveEntry>();
		TarEntry entry;
		while((entry = reader.GetNextEntry()) != null)
		{
			if(entry.EntryType == TarEntryType.GlobalExtendedAttributes) continue;
			using var copy = new MemoryStream();
			entry.DataStream?.CopyTo(copy);
			entries.Add(new(NormalizeName(entry.Name), copy.ToArray(), entry.Mode, entry.Length, entry.EntryType == TarEntryType.Directory));
		}
		return entries;
	}

	private static void AssertRpmDigest(string path, string directory, string name, byte[] content)
	{
		var bytes = File.ReadAllBytes(path);
		var header = HeaderEnd(bytes, 96, true);
		var names = Strings(bytes, header, 1117);
		var directories = Strings(bytes, header, 1118);
		var indexes = TagOffset(bytes, header, 1116);
		var index = Assert.Single(Enumerable.Range(0, names.Length), index => names[index] == name && directories[ReadInt(bytes, indexes.Offset + index * 4)] == directory);
		Assert.Equal(Convert.ToHexString(SHA256.HashData(content)).ToLowerInvariant(), Strings(bytes, header, 1035)[index]);
		Assert.Equal(8, ReadInt(bytes, TagOffset(bytes, header, 5011).Offset));
		var signature = TagOffset(bytes, 96, 1004);
		Assert.Equal(16, signature.Count);
		Assert.Equal(MD5.HashData(bytes.AsSpan(header)), bytes.AsSpan(signature.Offset, signature.Count).ToArray());
		var payload = HeaderEnd(bytes, header, false);
		Assert.Equal(Convert.ToHexString(SHA256.HashData(bytes.AsSpan(payload))).ToLowerInvariant(), Assert.Single(Strings(bytes, header, 5092)));
		Assert.Equal(8, ReadInt(bytes, TagOffset(bytes, header, 5093).Offset));
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

	private static string NormalizeName(string name) => (name.StartsWith("./", StringComparison.Ordinal) ? name[2..] : name).Trim('/');
	#endregion

	#region 嵌套类型
	private sealed record ArchiveEntry(string Name, byte[] Content, UnixFileMode Mode, long Size, bool IsDirectory);
	#endregion
}