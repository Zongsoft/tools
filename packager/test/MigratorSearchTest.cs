using System;
using System.IO;
using System.Linq;
using System.Formats.Tar;
using System.IO.Compression;
using System.Collections.Generic;

using Xunit;

namespace Zongsoft.Tools.Packager.Tests;

public sealed partial class MigratorPackageTest
{
	#region 父目录查找
	[Theory]
	[InlineData("hosting/web", "bootstrap")]
	[InlineData("hosting", "bootstrap")]
	[InlineData("", "bootstrap")]
	[InlineData("hosting", "$(migrationName)")]
	public void AncestorSearch_UsesClosestCompletePair(string selectedDirectory, string input)
	{
		using var directory = new MigrationTestDirectory();
		var package = Create("tar", directory);
		var source = SetSearchSource(directory);
		var selected = Pair(directory, Path.Combine(selectedDirectory, "bootstrap"), null, "2.7.1", "linux-x64");

		if(selectedDirectory.Length > 0)
			Pair(directory, "bootstrap", null, "2.7.1", "linux-x64");

		Assert.NotEqual(Environment.CurrentDirectory, source);
		var migrator = Migrator.Load(package, input);
		Assert.Equal(selected.Archive, migrator.Archive);
		Assert.Equal(selected.Script, migrator.Script);
	}

	[Fact]
	public void AncestorSearch_AllMissingReportsEveryDirectoryThroughRoot()
	{
		using var directory = new MigrationTestDirectory();
		var package = Create("tar", directory, "Enterprise");
		var source = SetSearchSource(directory);
		var name = "absent-" + Guid.NewGuid().ToString("N");
		Pair(directory, name, null, "2.7.1", "linux-x64");
		Pair(directory, name, "Enterprise", "1.0.0", "linux-x64");
		Pair(directory, name, "Enterprise", "2.7.1", "linux-arm64");
		Pair(directory, "hosting/web/nested/" + name, "Enterprise", "2.7.1", "linux-x64");
		var prefix = name + "-migrate-Enterprise@2.7.1_linux-x64";

		var error = Assert.Throws<FileNotFoundException>(() => Migrator.Load(package, name));
		Assert.Equal(Path.Combine(source, prefix + ".tar.gz"), error.FileName);
		Assert.Contains(prefix + ".tar.gz", error.Message);
		Assert.Contains(prefix + ".sh", error.Message);
		var lines = error.Message.Split(["\r\n", "\n"], StringSplitOptions.None);
		var expected = new List<string>();
		for(var current = new DirectoryInfo(source); current != null; current = current.Parent)
			expected.Add(current.FullName);
		Assert.Equal(expected, lines.Where(line => expected.Contains(line, StringComparer.Ordinal)));
		Assert.Empty(package.Entries);
	}

	[Theory]
	[InlineData("archive")]
	[InlineData("script")]
	public void AncestorSearch_IncompleteNearPairStopsBeforeCompleteAncestor(string missing)
	{
		using var directory = new MigrationTestDirectory();
		var package = Create("tar", directory);
		SetSearchSource(directory);
		Pair(directory, "bootstrap", null, "2.7.1", "linux-x64");
		var near = Pair(directory, "hosting/bootstrap", null, "2.7.1", "linux-x64");
		var missingPath = missing == "archive" ? near.Archive : near.Script;
		File.Delete(missingPath);

		var error = Assert.Throws<FileNotFoundException>(() => Migrator.Load(package, "bootstrap"));
		Assert.Equal(missingPath, error.FileName);
		Assert.Contains(missingPath, error.Message);
		Assert.Empty(package.Entries);
	}

	[Theory]
	[InlineData("missing-runtime")]
	[InlineData("missing-migrator")]
	[InlineData("runtime-mismatch")]
	[InlineData("malformed-gzip")]
	public void AncestorSearch_InvalidNearArchiveStopsBeforeCompleteAncestor(string failure)
	{
		using var directory = new MigrationTestDirectory();
		var package = Create("tar", directory);
		SetSearchSource(directory);
		Pair(directory, "bootstrap", null, "2.7.1", "linux-x64");
		var near = Pair(directory, "hosting/bootstrap", null, "2.7.1", "linux-x64", failure);
		if(failure == "malformed-gzip")
			File.WriteAllText(near.Archive, "invalid gzip archive");

		var error = Assert.Throws<InvalidDataException>(() => Migrator.Load(package, "bootstrap"));
		if(failure != "malformed-gzip")
			Assert.Contains(near.Archive, error.Message);
		Assert.Empty(package.Entries);
	}

	[Theory]
	[InlineData("./bootstrap")]
	[InlineData(".\\bootstrap")]
	[InlineData("absolute")]
	[InlineData("$(migrationDirectory)/bootstrap")]
	[InlineData("$(migrationDirectory)\\bootstrap")]
	[InlineData("$(qualifiedMigration)")]
	public void AncestorSearch_ExplicitDirectoryNeverAscends(string input)
	{
		using var directory = new MigrationTestDirectory();
		var package = Create("tar", directory);
		var source = SetSearchSource(directory);
		Pair(directory, "hosting/bootstrap", null, "2.7.1", "linux-x64");
		if(input == "absolute")
			input = Path.Combine(source, "bootstrap");

		if(!OperatingSystem.IsWindows() && input.Contains('\\'))
		{
			Assert.Throws<ArgumentException>(() => Migrator.Load(package, input));
			Assert.Empty(package.Entries);
			return;
		}

		var error = Assert.Throws<FileNotFoundException>(() => Migrator.Load(package, input));
		Assert.Equal(Path.Combine(source, "bootstrap-migrate@2.7.1_linux-x64.tar.gz"), error.FileName);
		var pair = Pair(directory, "hosting/web/bootstrap", null, "2.7.1", "linux-x64");
		var migrator = Migrator.Load(package, input);
		Assert.Equal(pair.Archive, migrator.Archive);
		Assert.Equal(pair.Script, migrator.Script);
	}
	#endregion

	#region 包内容验证
	[Theory]
	[InlineData("tar")]
	[InlineData("deb")]
	[InlineData("rpm")]
	public void AncestorSearch_PreservesPairPermissionsAndLifecycleAcrossFormats(string format)
	{
		using var directory = new MigrationTestDirectory();
		var package = Create(format, directory, "Enterprise");
		var source = SetSearchSource(directory);
		directory.Write("hosting/web/zongsoft.daemon.service", "[Unit]\nDescription=Hosting\n[Service]\nExecStart=/bin/true\n");
		var pair = Pair(directory, "bootstrap", "Enterprise", "2.7.1", "linux-x64");
		package.Migrator = Migrator.Load(package, "bootstrap");
		package.Migrator.Attach(package);
		package.Scriptor.Script();
		package.Pack(source, true);

		var payload = ReadPayload(source, format);
		var migration = payload.Where(entry => entry.Key.Contains(".migration/", StringComparison.Ordinal)).ToArray();
		Assert.Equal(2, migration.Length);
		Assert.Equal(File.ReadAllBytes(pair.Archive), Assert.Single(migration, entry => entry.Key.EndsWith(".tar.gz", StringComparison.Ordinal)).Value);
		Assert.Equal(File.ReadAllBytes(pair.Script), Assert.Single(migration, entry => entry.Key.EndsWith(".sh", StringComparison.Ordinal)).Value);
		var modes = ReadMigrationModes(source, format);
		Assert.Equal((UnixFileMode)384, Assert.Single(modes, entry => entry.Key.EndsWith(Path.GetFileName(pair.Archive), StringComparison.Ordinal)).Value);
		Assert.Equal((UnixFileMode)493, Assert.Single(modes, entry => entry.Key.EndsWith(Path.GetFileName(pair.Script), StringComparison.Ordinal)).Value);
		var script = ReadInstalled(source, format);
		AssertOrdered(script, "ExecStartPre=/bin/sh", Path.GetFileName(pair.Script) + "\" apply", "systemctl start");
		Assert.Contains(Path.GetFileName(pair.Script) + "\" check", script);
		Assert.Contains(Migrator.StateDirectory(package), script);
		Assert.Contains("set -e", script);
		Assert.DoesNotContain("apply || true", script);
	}
	#endregion

	#region 辅助方法
	private static string SetSearchSource(MigrationTestDirectory directory)
	{
		var source = Directory.CreateDirectory(Path.Combine(directory.Path, "hosting", "web")).FullName;
		var variables = new Dictionary<string, string>(Normalizer.Variables.Raw, StringComparer.OrdinalIgnoreCase)
		{
			["source"] = source,
			["migrationName"] = "bootstrap",
			["migrationDirectory"] = ".",
			["qualifiedMigration"] = "./bootstrap",
		};
		Normalizer.Initialize(variables);
		return source;
	}

	private static Dictionary<string, UnixFileMode> ReadMigrationModes(string directory, string format)
	{
		var file = Directory.GetFiles(directory, format == "tar" ? "*.tar.gz" : "*." + format).Single();
		var result = new Dictionary<string, UnixFileMode>();
		if(format != "rpm")
		{
			using var memory = new MemoryStream(format == "deb" ? ReadAr(file)["data.tar.gz"] : File.ReadAllBytes(file));
			using var gzip = new GZipStream(memory, CompressionMode.Decompress);
			using var reader = new TarReader(gzip);
			TarEntry entry;
			while((entry = reader.GetNextEntry()) != null)
				if(entry.DataStream != null)
					result.Add(entry.Name, entry.Mode);
			return result;
		}

		var bytes = File.ReadAllBytes(file);
		var header = RpmHeaderEnd(bytes, 96, true);
		var names = RpmStrings(bytes, header, 1117);
		var modes = RpmIndex(bytes, header, 1030);
		Assert.Equal(names.Length, modes.Count);
		for(var index = 0; index < names.Length; index++)
			result.Add(names[index], (UnixFileMode)(System.Buffers.Binary.BinaryPrimitives.ReadUInt16BigEndian(bytes.AsSpan(modes.Offset + index * 2, 2)) & 0xFFF));
		return result;
	}
	#endregion
}
