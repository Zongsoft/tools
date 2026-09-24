using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Reflection;
using System.Formats.Tar;
using System.Threading.Tasks;
using System.IO.Compression;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Runtime.InteropServices;

using Xunit;

using Zongsoft.Terminals;
using Zongsoft.Components;

namespace Zongsoft.Tools.Packager.Tests;

public sealed partial class MigratorPackageTest
{
	#region 产物消费
	[Theory]
	[InlineData("tar")]
	[InlineData("deb")]
	[InlineData("rpm")]
	public async Task Command_MigratorUsesResolvedSourceEditionVersionAndRuntimeAsync(string format)
	{
		using var directory = new MigrationTestDirectory();
		directory.Write(".version", "Zongsoft.Hosting.Web\n\n[Community]\n1.0.0\n\n[Enterprise]\n2.7.1\n");
		directory.Write("application.txt", "hosting application");
		var pair = Pair(directory, "releases/zongsoft.bootstrap", "Enterprise", "2.7.1", "linux-arm64");
		Pair(directory, "releases/zongsoft.bootstrap", "Community", "1.0.0", "linux-x64");
		CommandBase<CommandContext> command = format switch { "tar" => new TarCommand(), "deb" => new DebCommand(), _ => new RpmCommand() };
		var arguments = new[] { format, "--source:" + directory.Path, "--output:out", "--platform:Linux", "--architecture:Arm64", "--framework:net10.0", "--edition:enterprise", "--daemon:disabled", "--install-path:/opt/zongsoft/web", "--migrator:releases/zongsoft.bootstrap", "application.txt" };
		var context = new CommandContext(new CommandExecutor(), CommandLine.Parse(CommandLine.Get(arguments))[0], command, null);
		var terminalField = typeof(Terminal).GetField("_default", BindingFlags.NonPublic | BindingFlags.Static);
		var terminal = (ITerminal)terminalField.GetValue(null);
		try
		{
			Terminal.Default = DispatchProxy.Create<ITerminal, RecordingTerminal>();
			var path = Assert.IsType<string>(await ((ICommand)command).ExecuteAsync(context, TestContext.Current.CancellationToken));
			Assert.Contains("-Enterprise@2.7.1-arm64", Path.GetFileName(path));
			var payload = ReadPayload(Path.GetDirectoryName(path), format);
			Assert.Equal(File.ReadAllBytes(pair.Archive), Assert.Single(payload, entry => entry.Key.EndsWith(Path.GetFileName(pair.Archive), StringComparison.Ordinal)).Value);
			Assert.Equal(File.ReadAllBytes(pair.Script), Assert.Single(payload, entry => entry.Key.EndsWith(Path.GetFileName(pair.Script), StringComparison.Ordinal)).Value);
			Assert.Equal(2, payload.Count(entry => entry.Key.Contains(".migration/", StringComparison.Ordinal)));
		}
		finally
		{
			Terminal.Default = terminal;
		}
	}

	[Fact]
	public async Task Command_VariableTypedOptions_ExpandBeforeConversionAndOverwriteAsync()
	{
		using var directory = new MigrationTestDirectory();
		directory.Write(".version", "zongsoft.daemon@1.0.0");
		directory.Write("application.txt", "isolated package payload");
		var existing = directory.Write("out/zongsoft.daemon@2.3.4-arm64.tar.gz", "previous artifact");
		var names = new[] { "zongsoft_pack_source", "zongsoft_pack_name", "zongsoft_pack_version", "zongsoft_pack_release", "zongsoft_pack_platform", "zongsoft_pack_architecture", "zongsoft_pack_overwrite" };
		var previousEnvironment = names.ToDictionary(name => name, Environment.GetEnvironmentVariable);
		var terminalField = typeof(Terminal).GetField("_default", BindingFlags.NonPublic | BindingFlags.Static);
		var previousTerminal = (ITerminal)terminalField.GetValue(null);

		try
		{
			Environment.SetEnvironmentVariable("zongsoft_pack_source", directory.Path);
			Environment.SetEnvironmentVariable("zongsoft_pack_name", "zongsoft.daemon");
			Environment.SetEnvironmentVariable("zongsoft_pack_version", "$(zongsoft_pack_release)");
			Environment.SetEnvironmentVariable("zongsoft_pack_release", "2.3.4");
			Environment.SetEnvironmentVariable("zongsoft_pack_platform", "Linux");
			Environment.SetEnvironmentVariable("zongsoft_pack_architecture", "Arm64");
			Environment.SetEnvironmentVariable("zongsoft_pack_overwrite", "true");
			Terminal.Default = DispatchProxy.Create<ITerminal, RecordingTerminal>();

			var arguments = new[] { "tar", "--source:$(zongsoft_pack_source)", "--name:$(zongsoft_pack_name)", "--version:%zongsoft_pack_version%", "--platform:$(zongsoft_pack_platform)", "--architecture:$(zongsoft_pack_architecture)", "--overwrite:$(zongsoft_pack_overwrite)", "--framework:net10.0", "--daemon:disabled", "--output:out", "application.txt" };
			var command = new TarCommand();
			var context = new CommandContext(new CommandExecutor(), CommandLine.Parse(CommandLine.Get(arguments))[0], command, null);
			var path = Assert.IsType<string>(await ((ICommand)command).ExecuteAsync(context, TestContext.Current.CancellationToken));

			Assert.Equal(existing, path);
			var bytes = File.ReadAllBytes(path);
			Assert.True(bytes.Length > 2);
			Assert.Equal((byte)0x1f, bytes[0]);
			Assert.Equal((byte)0x8b, bytes[1]);
			Assert.Contains("2.3.4", File.ReadAllText(Path.Combine(directory.Path, ".version")));
			Assert.EndsWith("-arm64.tar.gz", path, StringComparison.Ordinal);
			Assert.Equal("isolated package payload", Encoding.UTF8.GetString(Assert.Single(ReadPayload(Path.GetDirectoryName(path), "tar"), entry => entry.Key.EndsWith("application.txt", StringComparison.Ordinal)).Value));

			var published = File.ReadAllBytes(path);
			Environment.SetEnvironmentVariable("zongsoft_pack_overwrite", "off");
			var deniedCommand = new TarCommand();
			var deniedContext = new CommandContext(new CommandExecutor(), CommandLine.Parse(CommandLine.Get(arguments))[0], deniedCommand, null);
			await Assert.ThrowsAsync<IOException>(async () => await ((ICommand)deniedCommand).ExecuteAsync(deniedContext, TestContext.Current.CancellationToken));
			Assert.Equal(published, File.ReadAllBytes(path));

			Environment.SetEnvironmentVariable("zongsoft_pack_overwrite", "yes");
			File.WriteAllText(path, "previous artifact");
			var allowedCommand = new TarCommand();
			var allowedContext = new CommandContext(new CommandExecutor(), CommandLine.Parse(CommandLine.Get(arguments))[0], allowedCommand, null);
			Assert.Equal(path, Assert.IsType<string>(await ((ICommand)allowedCommand).ExecuteAsync(allowedContext, TestContext.Current.CancellationToken)));
			Assert.Equal((byte)0x1f, File.ReadAllBytes(path)[0]);

			File.WriteAllText(path, "previous artifact");
			var switchArguments = arguments.Select(argument => argument == "--overwrite:$(zongsoft_pack_overwrite)" ? "--overwrite" : argument).ToArray();
			var switchCommand = new TarCommand();
			var switchContext = new CommandContext(new CommandExecutor(), CommandLine.Parse(CommandLine.Get(switchArguments))[0], switchCommand, null);
			Assert.Equal(path, Assert.IsType<string>(await ((ICommand)switchCommand).ExecuteAsync(switchContext, TestContext.Current.CancellationToken)));
			Assert.Equal((byte)0x1f, File.ReadAllBytes(path)[0]);

			Environment.SetEnvironmentVariable("zongsoft_pack_architecture", "invalid-architecture");
			var invalidArguments = arguments.Select(argument => argument == "--output:out" ? "--output:invalid-out" : argument).ToArray();
			var invalidCommand = new TarCommand();
			var invalidContext = new CommandContext(new CommandExecutor(), CommandLine.Parse(CommandLine.Get(invalidArguments))[0], invalidCommand, null);
			await Assert.ThrowsAsync<InvalidOperationException>(async () => await ((ICommand)invalidCommand).ExecuteAsync(invalidContext, TestContext.Current.CancellationToken));
			Assert.False(Directory.Exists(Path.Combine(directory.Path, "invalid-out")));
		}
		finally
		{
			Terminal.Default = previousTerminal;
			foreach(var name in names)
				Environment.SetEnvironmentVariable(name, previousEnvironment[name]);
		}
	}

	[Fact]
	public async Task Command_FormattedEqualsOptionsPreserveSpacesAndCommandOverwriteWinsAsync()
	{
		using var directory = new MigrationTestDirectory();
		directory.Write("source with spaces/.version", "zongsoft.daemon@1.0.0");
		directory.Write("source with spaces/application.txt", "package payload");
		var previousDirectory = Environment.CurrentDirectory;
		var previousOverwrite = Environment.GetEnvironmentVariable("overwrite");
		var terminalField = typeof(Terminal).GetField("_default", BindingFlags.NonPublic | BindingFlags.Static);
		var previousTerminal = (ITerminal)terminalField.GetValue(null);
		var arguments = new[] { "tar", "--source=source with spaces", "--output=out with spaces", "--platform=Linux", "--architecture=X64", "--framework=net10.0", "--daemon=disabled", "application.txt" };

		try
		{
			Environment.CurrentDirectory = directory.Path;
			Terminal.Default = DispatchProxy.Create<ITerminal, RecordingTerminal>();
			var invalidArguments = arguments.Select(argument => argument == "--architecture=X64" ? "--architecture=invalid" : argument).ToArray();
			var invalid = new TarCommand();
			var invalidLine = CommandLine.Parse(Utility.FormatCommand(invalidArguments[0], invalidArguments.AsSpan(1)))[0];
			var invalidContext = new CommandContext(new CommandExecutor(), invalidLine, invalid, null);
			await Assert.ThrowsAsync<InvalidOperationException>(async () => await ((ICommand)invalid).ExecuteAsync(invalidContext, TestContext.Current.CancellationToken));
			Assert.False(Directory.Exists(Path.Combine(directory.Path, "source with spaces", "out with spaces")));

			var command = new TarCommand();
			var line = CommandLine.Parse(Utility.FormatCommand(arguments[0], arguments.AsSpan(1)))[0];
			Assert.Equal("source with spaces", Assert.Single(line.Options, option => option.Name == "source").Value);
			Assert.Equal("out with spaces", Assert.Single(line.Options, option => option.Name == "output").Value);
			var context = new CommandContext(new CommandExecutor(), line, command, null);
			var archive = Assert.IsType<string>(await ((ICommand)command).ExecuteAsync(context, TestContext.Current.CancellationToken));
			Assert.Contains(Path.Combine("source with spaces", "out with spaces"), archive);
			Assert.Equal((byte)0x1f, File.ReadAllBytes(archive)[0]);

			File.WriteAllText(archive, "existing artifact");
			Environment.SetEnvironmentVariable("overwrite", "true");
			var blockedArguments = new[] { arguments[0], "--overwrite=false" }.Concat(arguments.Skip(1)).ToArray();
			var blocked = new TarCommand();
			var blockedLine = CommandLine.Parse(Utility.FormatCommand(blockedArguments[0], blockedArguments.AsSpan(1)))[0];
			var blockedContext = new CommandContext(new CommandExecutor(), blockedLine, blocked, null);
			await Assert.ThrowsAsync<IOException>(async () => await ((ICommand)blocked).ExecuteAsync(blockedContext, TestContext.Current.CancellationToken));
			Assert.Equal("existing artifact", File.ReadAllText(archive));
		}
		finally
		{
			Terminal.Default = previousTerminal;
			Environment.SetEnvironmentVariable("overwrite", previousOverwrite);
			Environment.CurrentDirectory = previousDirectory;
		}
	}

	[Theory]
	[InlineData("tar", null)]
	[InlineData("tar", "--migrator")]
	[InlineData("tar", "--migrator:")]
	[InlineData("tar", "--migrator:\"\"")]
	[InlineData("tar", "--migrator:\"   \"")]
	[InlineData("tar", "--migrator:\" \t \"")]
	[InlineData("deb", null)]
	[InlineData("deb", "--migrator")]
	[InlineData("deb", "--migrator:")]
	[InlineData("deb", "--migrator:\"\"")]
	[InlineData("deb", "--migrator:\"   \"")]
	[InlineData("deb", "--migrator:\" \t \"")]
	[InlineData("rpm", null)]
	[InlineData("rpm", "--migrator")]
	[InlineData("rpm", "--migrator:")]
	[InlineData("rpm", "--migrator:\"\"")]
	[InlineData("rpm", "--migrator:\"   \"")]
	[InlineData("rpm", "--migrator:\" \t \"")]
	public async Task Command_EmptyMigrator_PreservesOrdinaryPayloadAndServiceLifecycleAsync(string format, string option)
	{
		using var directory = new MigrationTestDirectory();
		directory.Write(".version", "zongsoft.daemon@2.7.1");
		directory.Write("application.txt", "ordinary application");
		var service = directory.Write("zongsoft.daemon.service", "[Unit]\nDescription=Hosting\n[Service]\nExecStart=/bin/true\n");
		CommandBase<CommandContext> command = format switch { "tar" => new TarCommand(), "deb" => new DebCommand(), _ => new RpmCommand() };
		var arguments = new[] { format, "--source:" + directory.Path, "--output:out", "--platform:Linux", "--architecture:X64", "--framework:net10.0", "--daemon:zongsoft.daemon.service", "--install-path:/opt/zongsoft/daemon", "application.txt" };
		var context = new CommandContext(new CommandExecutor(), CommandLine.Parse(CommandLine.Get(arguments) + " " + option)[0], command, null);
		var terminalField = typeof(Terminal).GetField("_default", BindingFlags.NonPublic | BindingFlags.Static);
		var terminal = (ITerminal)terminalField.GetValue(null);
		try
		{
			Terminal.Default = DispatchProxy.Create<ITerminal, RecordingTerminal>();
			var path = Assert.IsType<string>(await ((ICommand)command).ExecuteAsync(context, TestContext.Current.CancellationToken));
			var payload = ReadPayload(Path.GetDirectoryName(path), format);
			Assert.Equal("ordinary application", Encoding.UTF8.GetString(Assert.Single(payload, entry => entry.Key.EndsWith("application.txt", StringComparison.Ordinal)).Value));
			Assert.Equal(File.ReadAllBytes(service), Assert.Single(payload, entry => entry.Key.EndsWith("zongsoft.daemon.service", StringComparison.Ordinal)).Value);
			Assert.DoesNotContain(payload.Keys, name => name.Contains(".migration", StringComparison.Ordinal));
			var script = ReadInstalled(Path.GetDirectoryName(path), format);
			Assert.DoesNotContain(".migration", script);
			Assert.DoesNotContain("20-packager-migration.conf", script);
			Assert.Contains("systemctl start", script);
		}
		finally
		{
			Terminal.Default = terminal;
		}
	}

	[Theory]
	[InlineData("tar")]
	[InlineData("deb")]
	[InlineData("rpm")]
	public void Migrator_DirectoryNameFinalIdentityAndPax_EmbedsOriginalPairAcrossFormats(string format)
	{
		using var directory = new MigrationTestDirectory();
		var package = Create(format, directory, "Enterprise", Architecture.Arm64);
		var pair = Pair(directory, "releases/zongsoft.bootstrap", "Enterprise", "2.7.1", "linux-arm64");
		Pair(directory, "releases/zongsoft.bootstrap", null, "1.0.0", "linux-x64");
		package.Migrator = Migrator.Load(package, "$(inputs)/zongsoft.bootstrap");
		Assert.Equal(pair.Archive, package.Migrator.Archive);
		Assert.Equal(pair.Script, package.Migrator.Script);
		package.Migrator.Attach(package);
		var archiveEntry = Assert.Single(package.Entries, entry => entry.EntryName.EndsWith(Path.GetFileName(pair.Archive), StringComparison.Ordinal));
		Assert.Equal((UnixFileMode)384, archiveEntry.Mode);
		Assert.Equal((UnixFileMode)493, Assert.Single(package.Entries, entry => entry.EntryName.EndsWith(Path.GetFileName(pair.Script), StringComparison.Ordinal)).Mode);
		package.Scriptor.Script();
		package.Pack(directory.Path, true);
		var payload = ReadPayload(directory.Path, format);
		var entries = payload.Where(pair => pair.Key.Contains(".migration/", StringComparison.Ordinal)).ToArray();
		Assert.Equal(2, entries.Length);
		Assert.Equal(File.ReadAllBytes(pair.Archive), Assert.Single(entries, entry => entry.Key.EndsWith(".tar.gz", StringComparison.Ordinal)).Value);
		Assert.Equal(File.ReadAllBytes(pair.Script), Assert.Single(entries, entry => entry.Key.EndsWith(".sh", StringComparison.Ordinal)).Value);
		Assert.DoesNotContain(payload.Keys, name => name.EndsWith("migration.json", StringComparison.Ordinal) || name.EndsWith("schema.sql", StringComparison.Ordinal));
		var script = ReadInstalled(directory.Path, format);
		AssertOrdered(script, "ExecStartPre=/bin/sh", Path.GetFileName(pair.Script) + "\" apply", "systemctl start");
		Assert.Contains(Path.GetFileName(pair.Script) + "\" check", script);
		Assert.Contains(Migrator.StateDirectory(package), script);
		Assert.Contains("set -e", script);
		Assert.DoesNotContain("apply || true", script);
		if(format == "deb")
			AssertOrdered(script, "configure)", Path.GetFileName(pair.Script) + "\" apply");
		if(format == "tar")
			AssertOrdered(script, "tar -xpf", Path.GetFileName(pair.Script) + "\" apply");
	}

	[Theory]
	[InlineData("zongsoft-migrate")]
	[InlineData("zongsoft-migration")]
	[InlineData("zongsoft.migrate")]
	[InlineData("zongsoft.MIGRATION")]
	public void Migrator_ExistingSuffix_DoesNotAppendAgain(string name)
	{
		using var directory = new MigrationTestDirectory();
		var package = Create("tar", directory);
		var pair = Pair(directory, "releases/" + name, null, "2.7.1", "linux-x64", suffixed: true);
		var migrator = Migrator.Load(package, "releases/" + name);
		Assert.Equal(pair.Archive, migrator.Archive);
		Assert.Equal(pair.Script, migrator.Script);
	}

	[Theory]
	[InlineData("archive")]
	[InlineData("script")]
	public void Migrator_MissingPair_ReportsMissingFileWithoutPublishing(string missing)
	{
		using var directory = new MigrationTestDirectory();
		var package = Create("tar", directory);
		var pair = Pair(directory, "releases/zongsoft.bootstrap", null, "2.7.1", "linux-x64");
		var path = missing == "archive" ? pair.Archive : pair.Script;
		File.Delete(path);
		var error = Assert.Throws<FileNotFoundException>(() => Migrator.Load(package, "releases/zongsoft.bootstrap"));
		Assert.Equal(path, error.FileName);
		Assert.Empty(package.Entries);
		Assert.Empty(Directory.GetFiles(directory.Path, "*.tar.gz"));
	}

	[Theory]
	[InlineData("missing-runtime")]
	[InlineData("missing-migrator")]
	[InlineData("empty-migrator")]
	[InlineData("runtime-mismatch")]
	[InlineData("no-global")]
	[InlineData("late-global")]
	public void Migrator_InvalidRequiredMetadata_RejectsBeforePublishing(string failure)
	{
		using var directory = new MigrationTestDirectory();
		var package = Create("tar", directory);
		var pair = Pair(directory, "releases/zongsoft.bootstrap", null, "2.7.1", "linux-x64", failure);
		var error = Assert.Throws<InvalidDataException>(() => Migrator.Load(package, "releases/zongsoft.bootstrap"));
		Assert.Contains(pair.Archive, error.Message);
		Assert.Contains("linux-x64", error.Message);
		Assert.Empty(package.Entries);
	}

	[Theory]
	[InlineData("bootstrap.tar.gz")]
	[InlineData("bootstrap.sh")]
	[InlineData("bootstrap.cmd")]
	public void Migrator_FileExtensionInsteadOfInputName_IsRejected(string name)
	{
		using var directory = new MigrationTestDirectory();
		var package = Create("tar", directory);
		Assert.Throws<ArgumentException>(() => Migrator.Load(package, "releases/" + name));
		Assert.Empty(package.Entries);
	}

	[Fact]
	public void Migrator_ReservedPayloadDirectory_RejectsWithoutAddingPair()
	{
		using var directory = new MigrationTestDirectory();
		var package = Create("tar", directory);
		Pair(directory, "releases/zongsoft.bootstrap", null, "2.7.1", "linux-x64");
		directory.Write(".migration/existing.txt", "owned payload");
		package.Entries.Load(directory.Path, [".migration/existing.txt"]);
		var before = package.Entries.Count;
		Assert.Throws<InvalidOperationException>(() => Migrator.Load(package, "releases/zongsoft.bootstrap").Attach(package));
		Assert.Equal(before, package.Entries.Count);
	}

	[Theory]
	[InlineData("tar")]
	[InlineData("deb")]
	[InlineData("rpm")]
	public void Package_WithoutMigrator_PreservesOrdinaryServiceLifecycle(string format)
	{
		using var directory = new MigrationTestDirectory();
		var package = Create(format, directory);
		package.Scriptor.Script();
		package.Pack(directory.Path, true);
		Assert.DoesNotContain(ReadPayload(directory.Path, format).Keys, name => name.Contains(".migration", StringComparison.Ordinal));
		var script = ReadInstalled(directory.Path, format);
		Assert.DoesNotContain(".migration", script);
		Assert.Contains("systemctl start", script);
	}

	[Theory]
	[InlineData("tar")]
	[InlineData("deb")]
	[InlineData("rpm")]
	public void Package_DisabledService_MigratesBeforeCustomHook(string format)
	{
		using var directory = new MigrationTestDirectory();
		var package = Create(format, directory, daemon: "disabled", installed: "text:echo custom-installed-marker");
		var pair = Pair(directory, "releases/zongsoft.bootstrap", null, "2.7.1", "linux-x64");
		package.Migrator = Migrator.Load(package, "releases/zongsoft.bootstrap");
		package.Migrator.Attach(package);
		package.Scriptor.Script();
		package.Pack(directory.Path, true);
		var script = ReadInstalled(directory.Path, format);
		AssertOrdered(script, Path.GetFileName(pair.Script) + "\" apply", "echo custom-installed-marker");
		Assert.DoesNotContain("systemctl start", script);
	}

	[Fact]
	public void Package_MigratorWithoutResolvableDaemon_FailsInsteadOfDroppingMigration()
	{
		using var directory = new MigrationTestDirectory();
		var package = Create("tar", directory, daemon: "missing-host");
		Directory.CreateDirectory(Path.Combine(directory.Path, "bin", "Release", "net10.0"));
		Pair(directory, "releases/zongsoft.bootstrap", null, "2.7.1", "linux-x64");
		package.Migrator = Migrator.Load(package, "releases/zongsoft.bootstrap");
		Assert.Throws<InvalidOperationException>(() => package.Scriptor.Script());
		Assert.Empty(Directory.GetFiles(directory.Path, "*.tar.gz"));
	}

	[Fact]
	public void PackagerAssembly_DoesNotReferenceMigratorProtocolOrDrivers()
	{
		var assembly = typeof(Package).Assembly;
		var references = assembly.GetReferencedAssemblies().Select(reference => reference.Name).ToArray();
		Assert.DoesNotContain(references, name => name.StartsWith("Zongsoft.Tools.Migrator", StringComparison.Ordinal) || name.StartsWith("AWSSDK.", StringComparison.Ordinal));
		Assert.DoesNotContain(assembly.GetTypes(), type => type.Name is "MigrationPlan" or "MigrationLoader" or "MigrationBundle" or "MigrateCommand");
		foreach(var name in new[] { "Microsoft.Data.SqlClient", "MySqlConnector", "Npgsql", "Microsoft.Data.Sqlite", "DuckDB.NET.Data", "TDengine" })
			Assert.DoesNotContain(name, references);
	}
	#endregion

	#region 辅助方法
	private static Package Create(string format, MigrationTestDirectory directory, string edition = null, Architecture architecture = Architecture.X64, string daemon = "zongsoft.daemon.service", string installed = null)
	{
		directory.Write("zongsoft.daemon.service", "[Unit]\nDescription=Hosting\n[Service]\nExecStart=/bin/true\n");
		var variables = new Variables(new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
		{
			["source"] = directory.Path,
			["framework"] = "net10.0",
			["daemon"] = daemon,
			["inputs"] = "releases",
			["version"] = "1.0.0",
			["edition"] = "WrongEdition",
			["installed"] = installed,
		});
		Package package = format switch
		{
			"tar" => new Package.Tar("zongsoft.daemon", edition, new Version(2, 7, 1), Platform.Linux, architecture, variables),
			"deb" => new Package.Deb("zongsoft.daemon", edition, new Version(2, 7, 1), Platform.Linux, architecture, variables),
			"rpm" => new Package.Rpm("zongsoft.daemon", edition, new Version(2, 7, 1), Platform.Linux, architecture, variables),
			_ => throw new ArgumentOutOfRangeException(nameof(format)),
		};
		package.InstallPath = "/opt/zongsoft/daemon";
		return package;
	}

	private static (string Archive, string Script) Pair(MigrationTestDirectory directory, string input, string edition, string version, string runtime, string failure = null, bool suffixed = false)
	{
		var name = input + (suffixed ? "" : "-migrate") + (edition == null ? "" : "-" + edition) + "@" + version + "_" + runtime;
		var archive = directory.Write(name + ".tar.gz", "");
		var script = directory.Write(name + ".sh", "#!/bin/sh\n# opaque external launcher, never executed by tests\nexit 0\n");
		using var stream = File.Create(archive);
		using var gzip = new GZipStream(stream, CompressionLevel.SmallestSize);
		using var writer = new TarWriter(gzip, TarEntryFormat.Pax, false);
		var metadata = new Dictionary<string, string>();

		if(failure != "missing-runtime")
			metadata["Runtime"] = failure == "runtime-mismatch" ? "win-x64" : runtime;
		if(failure != "missing-migrator")
			metadata["Migrator"] = failure == "empty-migrator" ? " " : "Zongsoft.Tools.Migrator@0.10.0.0";

		if(failure == "late-global")
			writer.WriteEntry(new PaxTarEntry(TarEntryType.Directory, ".migration"));
		if(failure != "no-global")
			writer.WriteEntry(new PaxGlobalExtendedAttributesTarEntry(metadata));
		using var body = new MemoryStream(Encoding.UTF8.GetBytes("opaque sql fixture: SELECT 'original';\n"));
		writer.WriteEntry(new PaxTarEntry(TarEntryType.RegularFile, ".migration/.artifacts/mysql/schema.sql") { DataStream = body, Mode = (UnixFileMode)384 });
		return (archive, script);
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

	#region 模拟终端
	public class RecordingTerminal : DispatchProxy
	{
		public StringWriter Output { get; } = new();

		protected override object Invoke(MethodInfo method, object[] arguments)
		{
			if(method.Name.StartsWith("Write", StringComparison.Ordinal))
			{
				if(arguments.Length > 0)
					this.Output.Write(arguments[^1]);
				return null;
			}

			if(method.Name is "get_Output" or "get_Writer" or "get_Error")
				return this.Output;
			if(method.Name == "get_Encoding")
				return Encoding.UTF8;
			return null;
		}
	}
	#endregion
}
