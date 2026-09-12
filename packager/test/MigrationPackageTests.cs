using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.IO.Compression;
using System.Formats.Tar;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Security.Cryptography;

using Zongsoft.Tools.Packager.Migration;

using Xunit;

namespace Zongsoft.Tools.Packager.Tests;

public sealed class MigrationPackageTests
{
	#region 测试方法
	[Theory]
	[InlineData("tar")]
	[InlineData("deb")]
	[InlineData("rpm")]
	public void Package_MigrationAndDefaultDaemon_PreparesGuardAndMigratesBeforeServiceStarts(string format)
	{
		using var directory = new MigrationTestDirectory();
		File.Copy(typeof(MigrationPackageTests).Assembly.Location, Path.Combine(directory.Path, "Zongsoft.Migration.TestHost.dll"));
		var package = Create(format, directory.Path, "zongsoft.migration.test");
		package.Migration = Plan(directory);

		package.Scriptor.Script();
		package.Pack(directory.Path, true);
		var script = ReadInstalled(directory.Path, format);

		AssertOrdered(script, "ExecStartPre=/bin/sh", "migrate.sh\" apply", "systemctl start");
		Assert.Contains("migrate.sh\" check", script);
		Assert.DoesNotContain("systemctl start 'zongsoft.migration.test.service' >/dev/null 2>&1 || true", script);
		Assert.Contains("PACK_INSTALL_PATH", script);
		if(format == "deb")
		{
			Assert.Contains("case \"${1:-}\" in\n\tconfigure)", script);
			AssertOrdered(script, "configure)", "migrate.sh\" apply", "\n\t\t;;\nesac");
		}
		if(format == "tar")
		{
			Assert.Contains("if [ -z \"$DESTDIR\" ]; then", script);
			AssertOrdered(script, "tar -xpf - -C \"$TARGET\"", "migrate.sh\" apply");
		}
	}

	[Theory]
	[InlineData("tar")]
	[InlineData("deb")]
	[InlineData("rpm")]
	public void Package_DisabledDaemonAndCustomInstalledHook_StillMigratesBeforeHook(string format)
	{
		using var directory = new MigrationTestDirectory();
		var package = Create(format, directory.Path, "disabled", "echo custom-installed-marker");
		package.Migration = Plan(directory);

		package.Scriptor.Script();
		package.Pack(directory.Path, true);
		var script = ReadInstalled(directory.Path, format);

		AssertOrdered(script, "migrate.sh\" apply", "echo custom-installed-marker");
		Assert.DoesNotContain("systemctl", script);
		Assert.StartsWith("#!/bin/sh\nset -e", script);
	}

	[Theory]
	[InlineData("tar")]
	[InlineData("deb")]
	[InlineData("rpm")]
	public void Bundle_NativeRuntime_ContainsPrivatePlanScriptsAndExecutableLauncher(string format)
	{
		using var directory = new MigrationTestDirectory();
		var package = Create(format, directory.Path, "disabled");
		var plan = Plan(directory);
		using var bundle = MigrationBundle.Attach(package, plan, directory.CreateRuntime());
		package.Scriptor.Script();

		package.Pack(directory.Path, true);

		var planEntry = Assert.Single(package.Entries, entry => entry.EntryName.EndsWith(".migration/migration.json", StringComparison.Ordinal));
		Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite, planEntry.Mode);
		var idEntry = Assert.Single(package.Entries, entry => entry.EntryName.EndsWith(".migration/id", StringComparison.Ordinal));
		Assert.Equal(MigrationPlan.Load(planEntry.Source).Fingerprint(), File.ReadAllText(idEntry.Source));
		Assert.DoesNotContain("\r", File.ReadAllText(planEntry.Source));
		var executable = Assert.Single(package.Entries, entry => entry.EntryName.EndsWith("/Zongsoft.Tools.Packager.Migrator", StringComparison.Ordinal));
		Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute | UnixFileMode.GroupRead | UnixFileMode.GroupExecute | UnixFileMode.OtherRead | UnixFileMode.OtherExecute, executable.Mode);
		Assert.Contains(package.Entries, entry => entry.EntryName.EndsWith("/libe_sqlite3.so", StringComparison.Ordinal));
		Assert.Contains(package.Entries, entry => entry.EntryName.EndsWith("/libduckdb.so", StringComparison.Ordinal));
		var payload = ReadPayload(directory.Path, format);
		var archivePlan = Assert.Single(payload, pair => pair.Key.EndsWith(".migration/migration.json", StringComparison.Ordinal));
		Assert.Contains("zongsoft.migration.test", Encoding.UTF8.GetString(archivePlan.Value));
		var archiveSql = Assert.Single(payload, pair => pair.Key.EndsWith(".migration/.artifacts/schema.sql", StringComparison.Ordinal));
		Assert.Equal(Encoding.UTF8.GetBytes(plan.Tasks[0].Scripts[0].Content), archiveSql.Value);
		var launcher = Assert.Single(payload, pair => pair.Key.EndsWith(".migration/migrate.sh", StringComparison.Ordinal));
		Assert.Contains("exec \"$BASE_DIR/Zongsoft.Tools.Packager.Migrator\"", Encoding.UTF8.GetString(launcher.Value));
		Assert.DoesNotContain("dotnet", Encoding.UTF8.GetString(launcher.Value));
		Assert.Contains("$BASE_DIR/migration.json", Encoding.UTF8.GetString(launcher.Value));
		Assert.DoesNotContain(payload.Keys, name => name.EndsWith("migrating.json", StringComparison.Ordinal) || name.StartsWith("migration/", StringComparison.Ordinal) || name.Contains("/migration/", StringComparison.Ordinal));
		var native = Assert.Single(payload, pair => pair.Key.EndsWith("/Zongsoft.Tools.Packager.Migrator", StringComparison.Ordinal));
		Assert.Equal(new byte[] { 0x7f, 0x45, 0x4c, 0x46 }, native.Value[..4]);
		Assert.DoesNotContain(payload.Keys, name => name.EndsWith(".dll", StringComparison.Ordinal) || name.EndsWith(".deps.json", StringComparison.Ordinal) || name.EndsWith(".runtimeconfig.json", StringComparison.Ordinal));
	}

	[Theory]
	[InlineData("tar")]
	[InlineData("deb")]
	[InlineData("rpm")]
	public void Package_AllMigrationFilesMissing_PackagesServiceWithoutRuntimeOrStartupGate(string format)
	{
		using var directory = new MigrationTestDirectory();
		var input = directory.Write("source/application.txt", "hosting payload");
		var source = Path.GetDirectoryName(input);
		File.Copy(typeof(MigrationPackageTests).Assembly.Location, Path.Combine(source, "Zongsoft.Migration.TestHost.dll"));
		var package = Create(format, source, "zongsoft.migration.test");
		var warnings = new List<string>();
		package.Migration = new MigrationLoader(value => value, warnings.Add).Load("missing.ini;absent/*.ini", source, package.PackageName, package.Version.ToString());

		Assert.Null(package.Migration);
		package.Scriptor.Script();
		package.Pack(directory.Path, true);

		var payload = ReadPayload(directory.Path, format);
		var service = Assert.Single(payload, entry => entry.Key.EndsWith(".service", StringComparison.Ordinal));
		Assert.Contains("ExecStart=", Encoding.UTF8.GetString(service.Value));
		Assert.DoesNotContain("migrate.sh", Encoding.UTF8.GetString(service.Value));
		Assert.DoesNotContain(payload.Keys, name => name.Split('/').Any(segment => segment is ".migration" or "migration"));
		var installed = ReadInstalled(directory.Path, format);
		Assert.DoesNotContain("migrate.sh", installed);
		Assert.DoesNotContain(".migration/", installed);
		Assert.Contains("systemctl start", installed);
		Assert.Equal(2, warnings.Count);
		Assert.Contains(Path.Combine(source, "missing.ini"), warnings[0]);
		Assert.Contains(Path.Combine(source, "absent", "*.ini"), warnings[1]);
	}

	[Theory]
	[InlineData("net8.0")]
	[InlineData("net9.0")]
	public void Bundle_HostFramework_DoesNotChangeMigratorRuntime(string framework)
	{
		using var directory = new MigrationTestDirectory();
		Normalizer.Initialize(new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["framework"] = framework, ["source"] = directory.Path, ["daemon"] = "disabled" });
		var package = new Package.Tar("zongsoft.daemon", null, new Version(1, 1, 0), Platform.Linux, Architecture.X64) { InstallPath = "/opt/zongsoft/daemon" };

		using var bundle = MigrationBundle.Attach(package, Plan(directory), directory.CreateRuntime());

		Assert.Contains(package.Entries, entry => entry.EntryName == ".migration/Zongsoft.Tools.Packager.Migrator");
		Assert.DoesNotContain(package.Entries, entry => entry.EntryName.EndsWith(".runtimeconfig.json", StringComparison.Ordinal));
		var launcher = Assert.Single(package.Entries, entry => entry.EntryName == ".migration/migrate.sh");
		Assert.DoesNotContain("dotnet", File.ReadAllText(launcher.Source));
	}

	[Fact]
	public void Package_MigrationWithoutResolvableDaemon_FailsInsteadOfDroppingMigration()
	{
		using var directory = new MigrationTestDirectory();
		Directory.CreateDirectory(Path.Combine(directory.Path, "bin", "Release", "net10.0"));
		var package = Create("tar", directory.Path, "zongsoft.migration.test");
		package.Migration = Plan(directory);

		Assert.Throws<InvalidOperationException>(() => package.Scriptor.Script());
		Assert.Empty(Directory.GetFiles(directory.Path, "*.tar.gz"));
	}

	[Fact]
	public void Bundle_ReservedEntryAlreadyOccupied_RejectsConflict()
	{
		using var directory = new MigrationTestDirectory();
		directory.Write(".migration/migrate.sh", "[Unit]\nDescription=conflicting fixture\n[Service]\nExecStart=/bin/true\n");
		var package = Create("tar", directory.Path, ".migration/migrate.sh");
		package.Scriptor.Script();

		Assert.Throws<InvalidOperationException>(() => MigrationBundle.Attach(package, Plan(directory), directory.CreateRuntime()));
	}

	[Theory]
	[InlineData("mssql", Architecture.X64)]
	[InlineData("mysql", Architecture.Arm64)]
	[InlineData("postgres", Architecture.X64)]
	[InlineData("sqlite", Architecture.Arm64)]
	[InlineData("duckdb", Architecture.X64)]
	[InlineData("tdengine", Architecture.Arm64)]
	[InlineData("amazon.s3", Architecture.X64)]
	public void Bundle_ProviderAndArchitecture_CollectsCompleteSelectedNativeDirectory(string provider, Architecture architecture)
	{
		using var directory = new MigrationTestDirectory();
		Normalizer.Initialize(new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["framework"] = "net10.0", ["source"] = directory.Path, ["daemon"] = "disabled" });
		var package = new Package.Tar("zongsoft.migration.test", null, new Version(1, 1, 0), Platform.Linux, architecture) { InstallPath = "/opt/zongsoft/migration-test" };
		var plan = new MigrationPlan { Package = package.PackageName, Version = "1.1.0", Tasks = [new() { Id = "0001-" + provider, Provider = provider }] };

		using var bundle = MigrationBundle.Attach(package, plan, directory.CreateRuntime());

		var executable = Assert.Single(package.Entries, entry => entry.EntryName == ".migration/Zongsoft.Tools.Packager.Migrator");
		Assert.Equal(architecture == Architecture.X64 ? (byte)62 : (byte)183, File.ReadAllBytes(executable.Source)[18]);
		var sqlite = Assert.Single(package.Entries, entry => entry.EntryName == ".migration/libe_sqlite3.so");
		Assert.Equal("sqlite-" + package.Runtime, File.ReadAllText(sqlite.Source));
		var duckdb = Assert.Single(package.Entries, entry => entry.EntryName == ".migration/libduckdb.so");
		Assert.Equal("duckdb-" + package.Runtime, File.ReadAllText(duckdb.Source));
		var manifest = Assert.Single(package.Entries, entry => entry.EntryName == ".migration/assets/manifest.txt");
		Assert.Equal(package.Runtime, File.ReadAllText(manifest.Source));
		Assert.DoesNotContain(package.Entries, entry => entry.EntryName.EndsWith(".deps.json", StringComparison.Ordinal));
		Assert.Empty(Directory.GetFiles(directory.Path, "*.tar.gz"));
	}

	[Fact]
	public void Bundle_MissingNativeEntry_FailsWithExpectedExecutablePath()
	{
		using var directory = new MigrationTestDirectory();
		var package = Create("tar", directory.Path, "disabled");
		var runtime = directory.CreateRuntime();
		var executable = Path.Combine(runtime, "linux-x64", "Zongsoft.Tools.Packager.Migrator");
		File.Delete(executable);

		var error = Assert.Throws<FileNotFoundException>(() => MigrationBundle.Attach(package, Plan(directory), runtime));

		Assert.Equal(executable, error.FileName);
	}

	[Theory]
	[InlineData(false)]
	[InlineData(true)]
	public void Bundle_InvalidNativeEntry_RejectsWrongFormatOrArchitecture(bool wrongArchitecture)
	{
		using var directory = new MigrationTestDirectory();
		var package = Create("tar", directory.Path, "disabled");
		var runtime = directory.CreateRuntime();
		var executable = Path.Combine(runtime, "linux-x64", "Zongsoft.Tools.Packager.Migrator");
		var bytes = File.ReadAllBytes(executable);
		if(wrongArchitecture) bytes[18] = 183;
		else bytes[0] = (byte)'M';
		File.WriteAllBytes(executable, bytes);

		Assert.Throws<InvalidDataException>(() => MigrationBundle.Attach(package, Plan(directory), runtime));
	}

	[Fact]
	public void Rpm_GzipPayload_DeclaresSupportedRpmlibRequirementsAndAlignedVersions()
	{
		using var directory = new MigrationTestDirectory();
		const string serviceName = "zongsoft.daemon.service";
		var source = directory.Write(serviceName, "[Unit]\nDescription=Zongsoft Daemon\n[Service]\nExecStart=/bin/true\n");
		var package = Create("rpm", directory.Path, serviceName);
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
		var serviceIndex = Array.IndexOf(baseNames, serviceName);
		Assert.True(serviceIndex >= 0);
		var directoryIndex = ReadInt(bytes, directoryIndexes.Offset + serviceIndex * 4);
		Assert.Equal(package.InstallPath + "/" + serviceName, directories[directoryIndex] + baseNames[serviceIndex]);

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
		var service = Assert.Single(payload, entry => entry.Key.EndsWith("/" + serviceName, StringComparison.Ordinal));
		Assert.Equal(File.ReadAllBytes(source), service.Value);

	}

	[Fact]
	public void Rpm_ImmutableRegionsAndDigests_CoverActualHeaderPayloadAndFileBytes()
	{
		using var directory = new MigrationTestDirectory();
		var package = Create("rpm", directory.Path, "disabled");
		var plan = Plan(directory);
		plan.Tasks[0].Scripts.Add(directory.Script(".migration/.artifacts/empty.sql", ""));
		using var bundle = MigrationBundle.Attach(package, plan, directory.CreateRuntime());
		package.Scriptor.Script();

		package.Pack(directory.Path, true);

		var bytes = File.ReadAllBytes(Directory.GetFiles(directory.Path, "*.rpm").Single());
		const int signature = 96;
		var main = RpmHeaderEnd(bytes, signature, true);
		var payloadOffset = RpmHeaderEnd(bytes, main, false);
		Region(signature, 62);
		Region(main, 63);
		Assert.All(bytes[RpmHeaderEnd(bytes, signature, false)..main], value => Assert.Equal((byte)0, value));
		Assert.Equal(0, main % 8);

		var header = bytes[main..payloadOffset];
		var payload = bytes[payloadOffset..];
		var body = bytes[main..];
		Assert.Equal(6, RpmIndex(bytes, signature, 269).Type);
		Assert.Equal(6, RpmIndex(bytes, signature, 273).Type);
		Assert.Equal(Convert.ToHexString(SHA1.HashData(header)).ToLowerInvariant(), Assert.Single(RpmStrings(bytes, signature, 269)));
		Assert.Equal(Convert.ToHexString(SHA256.HashData(header)).ToLowerInvariant(), Assert.Single(RpmStrings(bytes, signature, 273)));
		Assert.Equal(body.Length, Scalar(signature, 1000));
		var md5 = RpmIndex(bytes, signature, 1004);
		Assert.Equal(7, md5.Type);
		Assert.Equal(16, md5.Count);
		Assert.Equal(MD5.HashData(body), bytes[md5.Offset..(md5.Offset + md5.Count)]);
		var signatureTags = Enumerable.Range(0, ReadInt(bytes, signature + 8)).Select(index => ReadInt(bytes, signature + 16 + index * 16)).ToArray();
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
		Assert.Equal(package.Entries.Count, regularFiles);
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
					if(type == 6) Assert.Equal(1, items);
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
		const string serviceName = "zongsoft.migration.test.service";
		const string service = "[Unit]\nDescription=RPM boundary fixture\n[Service]\nExecStart=/bin/true\n";
		foreach(var metadataLength in new[] { 0, 1, 2, 3, 4, 5, 6, 7, 8, 15 })
		{
			using var directory = new MigrationTestDirectory();
			var source = directory.Write(serviceName, service);
			var package = Create("rpm", directory.Path, serviceName);
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
			var entry = Assert.Single(payload, pair => pair.Key.EndsWith("/" + serviceName, StringComparison.Ordinal));
			Assert.Equal(File.ReadAllBytes(source), entry.Value);
		}
		Assert.Contains(4, observedAlignment);
		Assert.True(observedHeaderSizes.Count > 1, "Metadata variants must exercise multiple declared header lengths.");
	}

	[Theory]
	[InlineData("tar")]
	[InlineData("deb")]
	[InlineData("rpm")]
	public void Bundle_PreprocessedSqlServerBatches_PreserveContentOrderAndChecksums(string format)
	{
		using var directory = new MigrationTestDirectory();
		directory.Write("db.ini", "[mssql]\n./sql/*.sql\n");
		directory.Write("mssql.env", "Server=localhost\nDatabase=hosting\nUserName=operator\n");
		directory.Write("sql/020-seed.sql", "INSERT INTO samples VALUES (N'附件');\r\nGO\r\n");
		directory.Write("sql/010-schema.sql", "CREATE TABLE samples (title NVARCHAR(100));\r\nGO\r\nSELECT N'GO';\r\n");
		directory.Write("second.ini", "[mssql]\n./other.sql\n");
		directory.Write("other.sql", "SELECT N'other task';");
		var plan = new MigrationLoader(null).Load("db.ini;second.ini", directory.Path, "zongsoft.daemon", "1.1.0");
		var package = Create(format, directory.Path, "disabled");
		using var bundle = MigrationBundle.Attach(package, plan, directory.CreateRuntime());
		package.Scriptor.Script();

		package.Pack(directory.Path, true);

		var payload = ReadPayload(directory.Path, format);
		var archivePlan = Assert.Single(payload, pair => pair.Key.EndsWith(".migration/migration.json", StringComparison.Ordinal));
		using var json = JsonDocument.Parse(archivePlan.Value);
		var tasks = json.RootElement.GetProperty("Tasks").EnumerateArray().ToArray();
		Assert.Equal(2, tasks.Length);
		var scripts = tasks.SelectMany(task => task.GetProperty("Scripts").EnumerateArray()).ToArray();
		var expected = new[] { "CREATE TABLE samples (title NVARCHAR(100));", "SELECT N'GO';", "INSERT INTO samples VALUES (N'附件');", "SELECT N'other task';" };
		Assert.Equal(expected.Length, scripts.Length);
		Assert.Equal(expected.Length, payload.Count(pair => pair.Key.Contains(".migration/.artifacts/mssql/", StringComparison.Ordinal)));
		for(var index = 0; index < expected.Length; index++)
		{
			var path = $".migration/.artifacts/mssql/{index + 1:D4}.sql";
			Assert.Equal(path, scripts[index].GetProperty("Path").GetString());
			var entry = Assert.Single(payload, pair => pair.Key.EndsWith(path, StringComparison.Ordinal));
			Assert.Equal(Encoding.UTF8.GetBytes(expected[index]), entry.Value);
			Assert.Equal(Convert.ToHexString(SHA256.HashData(entry.Value)), scripts[index].GetProperty("Checksum").GetString());
			Assert.False(scripts[index].TryGetProperty("Source", out _));
			Assert.False(scripts[index].TryGetProperty("Content", out _));
			var target = format == "tar" ? path : package.InstallPath.Trim('/') + "/" + path;
			var model = Assert.Single(package.Entries, entry => entry.EntryName == target);
			Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.GroupRead | UnixFileMode.OtherRead, model.Mode);
		}
		Assert.DoesNotContain(payload.Keys, name => name.StartsWith("migration/", StringComparison.Ordinal) || name.Contains("/migration/", StringComparison.Ordinal));
	}
	#endregion

	#region 辅助方法
	private static Package Create(string format, string source, string daemon, string installed = null)
	{
		var variables = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["framework"] = "net10.0", ["source"] = source, ["daemon"] = daemon };
		if(installed != null) variables["installed"] = installed;
		Normalizer.Initialize(variables);
		Package package = format switch
		{
			"tar" => new Package.Tar("Zongsoft.Migration.TestHost", null, new Version(1, 1, 0), Platform.Linux, Architecture.X64),
			"deb" => new Package.Deb("Zongsoft.Migration.TestHost", null, new Version(1, 1, 0), Platform.Linux, Architecture.X64),
			"rpm" => new Package.Rpm("Zongsoft.Migration.TestHost", null, new Version(1, 1, 0), Platform.Linux, Architecture.X64),
			_ => throw new ArgumentOutOfRangeException(nameof(format)),
		};
		package.InstallPath = "/opt/zongsoft/migration-test";
		return package;
	}

	private static MigrationPlan Plan(MigrationTestDirectory directory) => new()
	{
		Package = "zongsoft.migration.test", Version = "1.1.0",
		Tasks = [new() { Id = "0001-sqlite", Provider = "sqlite", Parameters = new(StringComparer.OrdinalIgnoreCase) { ["Database"] = "/var/lib/zongsoft/hosting.db" }, Scripts = [directory.Script(".migration/.artifacts/schema.sql", "CREATE TABLE IF NOT EXISTS samples (id INTEGER);")] }],
	};

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
		if(format == "tar") return Encoding.UTF8.GetString(ReadTar(File.ReadAllBytes(file))["install.sh"]);
		if(format == "deb") return Encoding.UTF8.GetString(ReadTar(ReadAr(file)["control.tar.gz"])["postinst"]);
		var bytes = File.ReadAllBytes(file);
		var start = RpmHeaderEnd(bytes, 96, true);
		var count = ReadInt(bytes, start + 8);
		var store = start + 16 + count * 16;
		for(var i = 0; i < count; i++)
		{
			var entry = start + 16 + i * 16;
			if(ReadInt(bytes, entry) != 1024) continue;
			var offset = store + ReadInt(bytes, entry + 8);
			return Encoding.UTF8.GetString(bytes, offset, Array.IndexOf(bytes, (byte)0, offset) - offset);
		}
		throw new InvalidDataException("RPM post-install tag missing.");
	}

	private static Dictionary<string, byte[]> ReadPayload(string directory, string format)
	{
		var file = Directory.GetFiles(directory, format == "tar" ? "*.tar.gz" : "*." + format).Single();
		if(format == "tar") return ReadTar(File.ReadAllBytes(file));
		if(format == "deb") return ReadTar(ReadAr(file)["data.tar.gz"]);
		var bytes = File.ReadAllBytes(file);
		var offset = RpmHeaderEnd(bytes, RpmHeaderEnd(bytes, 96, true), false);
		using var compressed = new MemoryStream(bytes, offset, bytes.Length - offset);
		using var gzip = new GZipStream(compressed, CompressionMode.Decompress);
		using var content = new MemoryStream(); gzip.CopyTo(content);
		bytes = content.ToArray(); offset = 0;
		var entries = new Dictionary<string, byte[]>(StringComparer.Ordinal);
		while(offset + 110 <= bytes.Length)
		{
			Assert.Equal("070701", Encoding.ASCII.GetString(bytes, offset, 6));
			var size = Convert.ToInt32(Encoding.ASCII.GetString(bytes, offset + 54, 8), 16);
			var nameSize = Convert.ToInt32(Encoding.ASCII.GetString(bytes, offset + 94, 8), 16);
			var name = Encoding.UTF8.GetString(bytes, offset + 110, nameSize - 1).TrimStart('.', '/');
			offset = (offset + 110 + nameSize + 3) & ~3;
			if(name == "TRAILER!!!") break;
			entries.Add(name, bytes[offset..(offset + size)]);
			offset = (offset + size + 3) & ~3;
		}
		return entries;
	}

	private static Dictionary<string, byte[]> ReadAr(string path)
	{
		var bytes = File.ReadAllBytes(path); var result = new Dictionary<string, byte[]>(); var offset = 8;
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
		using var memory = new MemoryStream(archive); using var gzip = new GZipStream(memory, CompressionMode.Decompress); using var reader = new TarReader(gzip);
		var result = new Dictionary<string, byte[]>(StringComparer.Ordinal); TarEntry entry;
		while((entry = reader.GetNextEntry()) != null)
		{
			if(entry.DataStream == null) continue;
			using var content = new MemoryStream(); entry.DataStream.CopyTo(content);
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
