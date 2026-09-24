using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Reflection;
using System.Diagnostics;
using System.Formats.Tar;
using System.IO.Compression;
using System.Threading.Tasks;
using System.Collections.Generic;

using Xunit;

using Zongsoft.Terminals;
using Zongsoft.Components;

namespace Zongsoft.Tools.Migrator.Tests;

public sealed partial class MigrateCommandTest
{
	#region 环境属性
	public static bool IsWindows => OperatingSystem.IsWindows();
	#endregion

	#region 生成测试
	[Theory]
	[InlineData("zongsoft.daemon", "Linux", "X64", null, "zongsoft.daemon-migrate", "zongsoft.daemon-migrate@1.2.3_linux-x64.sh", "linux-x64")]
	[InlineData("zongsoft.daemon-migrate", "Linux", "X64", null, "zongsoft.daemon-migrate", "zongsoft.daemon-migrate.sh", "linux-x64")]
	[InlineData("zongsoft.daemon-migration", "Linux", "Arm64", "Enterprise", "zongsoft.daemon-migration-Enterprise", "zongsoft.daemon-migration-Enterprise.sh", "linux-arm64")]
	[InlineData("zongsoft.daemon.migrate", "win", "X64", null, "zongsoft.daemon.migrate", "zongsoft.daemon.migrate.cmd", "win-x64")]
	[InlineData("zongsoft.daemon.MIGRATION", "windows", "X64", "Community", "zongsoft.daemon.MIGRATION-Community", "zongsoft.daemon.MIGRATION-Community.cmd", "win-x64")]
	public async Task Execute_PlatformSuffixAndEdition_GeneratesStandalonePairWithoutRunningSqlOrChangingVersionAsync(string name, string platform, string architecture, string edition, string identity, string launcher, string runtime)
	{
		using var directory = new MigrationTestDirectory();
		var version = directory.Write(".version", "invalid source version must not be read\n");
		var versionBytes = File.ReadAllBytes(version);
		var database = runtime == "win-x64" ? OperatingSystem.IsWindows() ? Path.Combine(directory.Path, "not-created.db") : "C:/Zongsoft/not-created.db" : "/var/lib/zongsoft/not-created.db";
		PrepareMigration(directory, database);
		var arguments = Arguments(name, platform, architecture);
		arguments.Add("--summary:Initial bootstrap");
		arguments.Add("--description:Zongsoft hosting migration");
		if(edition != null)
			arguments.Add("--edition:" + edition);
		arguments.Add("db.migration");

		var result = await RunAsync(directory, arguments);

		Assert.True(result.Code == 0, result.Output);
		var archive = Path.Combine(directory.Path, "out", identity + "@1.2.3_" + runtime + ".tar.gz");
		Assert.True(File.Exists(archive), archive);
		using(var input = File.OpenRead(archive))
		using(var gzip = new GZipStream(input, CompressionMode.Decompress))
		using(var reader = new TarReader(gzip))
		{
			var metadata = Assert.IsType<PaxGlobalExtendedAttributesTarEntry>(reader.GetNextEntry());
			Assert.Equal(runtime, metadata.GlobalExtendedAttributes["Runtime"]);
			var assembly = typeof(MigrateCommand).Assembly.GetName();
			Assert.Equal(assembly.Name + "@" + assembly.Version, metadata.GlobalExtendedAttributes["Migrator"]);
			Assert.DoesNotContain("Packager", metadata.GlobalExtendedAttributes.Keys);
		}

		Assert.Equal(2, Directory.GetFiles(Path.Combine(directory.Path, "out")).Length);
		var script = File.ReadAllText(Path.Combine(directory.Path, "out", identity + "@1.2.3_" + runtime + Path.GetExtension(launcher)));
		Assert.Contains(Path.GetFileName(archive), script);
		Assert.DoesNotContain("systemctl", script);
		if(runtime == "win-x64")
			Assert.Contains("\r\n", script);
		else
			Assert.DoesNotContain("\r", script);
		var payload = ReadArchive(archive);
		var planEntry = Assert.Single(payload, entry => entry.Name.EndsWith(".migration/migration.json", StringComparison.Ordinal));
		Assert.Equal((UnixFileMode)384, planEntry.Mode);
		using var plan = JsonDocument.Parse(planEntry.Content);
		Assert.Equal(identity, plan.RootElement.GetProperty("Name").GetString());
		Assert.Equal(runtime, plan.RootElement.GetProperty("Runtime").GetString());
		Assert.Equal("1.2.3", plan.RootElement.GetProperty("Version").GetString());
		Assert.Equal(name, plan.RootElement.GetProperty("Title").GetString());
		Assert.Equal("Initial bootstrap", plan.RootElement.GetProperty("Summary").GetString());
		Assert.Equal("Zongsoft hosting migration", plan.RootElement.GetProperty("Description").GetString());
		var sql = Assert.Single(payload, entry => entry.Name.EndsWith(".migration/.artifacts/sqlite/1.sql", StringComparison.Ordinal));
		Assert.Equal("CREATE TABLE must_not_run (id INTEGER);", Encoding.UTF8.GetString(sql.Content));
		Assert.DoesNotContain(payload, entry => entry.Name.EndsWith(".version", StringComparison.Ordinal));
		Assert.Contains(payload, entry => entry.Name.EndsWith(runtime == "win-x64" ? ".migration/migrate.cmd" : ".migration/migrate.sh", StringComparison.Ordinal));
		Assert.Equal(versionBytes, File.ReadAllBytes(version));
		Assert.False(File.Exists(Path.Combine(directory.Path, "not-created.db")));
		Assert.False(Directory.Exists(Path.Combine(directory.Path, "state")));
	}

	[Fact]
	public async Task Execute_DefaultArchitectureAndOutput_UsesCurrentDirectoryAndExplicitTitleAsync()
	{
		using var directory = new MigrationTestDirectory();
		PrepareMigration(directory, "/data/hosting.db");
		var arguments = new[] { "migrate", "--name:zongsoft.daemon", "--version:1.2.3", "--platform:Linux", "--title:Hosting bootstrap", "db.migration" };

		var result = await RunAsync(directory, arguments);

		Assert.True(result.Code == 0, result.Output);
		var archive = Path.Combine(directory.Path, "zongsoft.daemon-migrate@1.2.3_linux-x64.tar.gz");
		Assert.True(File.Exists(Path.Combine(directory.Path, "zongsoft.daemon-migrate@1.2.3_linux-x64.sh")));
		var entry = Assert.Single(ReadArchive(archive), entry => entry.Name.EndsWith(".migration/migration.json", StringComparison.Ordinal));
		using var plan = JsonDocument.Parse(entry.Content);
		Assert.Equal("Hosting bootstrap", plan.RootElement.GetProperty("Title").GetString());
		Assert.Equal("linux-x64", plan.RootElement.GetProperty("Runtime").GetString());
		Assert.False(Directory.Exists(Path.Combine(directory.Path, "out")));
	}

	[Theory]
	[InlineData(false)]
	[InlineData(true)]
	public async Task Execute_OneOutputAlreadyExists_LeavesExistingFileAndDoesNotCreatePartnerAsync(bool archiveExists)
	{
		using var directory = new MigrationTestDirectory();
		var archive = "out/zongsoft.daemon-migrate@1.2.3_linux-x64.tar.gz";
		var launcher = "out/zongsoft.daemon-migrate@1.2.3_linux-x64.sh";
		var existing = directory.Write(archiveExists ? archive : launcher, "existing-user-content");
		var arguments = Arguments("zongsoft.daemon", "Linux", "X64");
		arguments.Add("missing.migration");

		var result = await RunAsync(directory, arguments);

		Assert.NotEqual(0, result.Code);
		Assert.Contains(existing, Assert.IsType<IOException>(result.Error).Message);
		Assert.Equal("existing-user-content", File.ReadAllText(existing));
		Assert.False(File.Exists(Path.Combine(directory.Path, archiveExists ? launcher : archive)));
	}

	[Fact]
	public async Task Execute_Overwrite_ReplacesBothOutputsAfterValidInputAsync()
	{
		using var directory = new MigrationTestDirectory();
		PrepareMigration(directory, "/data/hosting.db");
		var archive = directory.Write("out/zongsoft.daemon-migrate@1.2.3_linux-x64.tar.gz", "old-archive");
		var launcher = directory.Write("out/zongsoft.daemon-migrate@1.2.3_linux-x64.sh", "old-launcher");
		var arguments = Arguments("zongsoft.daemon", "Linux", "X64");
		arguments.Add("--overwrite");
		arguments.Add("db.migration");

		var result = await RunAsync(directory, arguments);

		Assert.True(result.Code == 0, result.Output);
		Assert.NotEmpty(ReadArchive(archive));
		Assert.StartsWith("#!/bin/sh\n", File.ReadAllText(launcher));
		Assert.DoesNotContain("old-launcher", File.ReadAllText(launcher));
	}

	[Fact]
	public async Task Execute_ExplicitOptionsOverrideEnvironment_WhileEnvironmentExpandsInputAsync()
	{
		using var directory = new MigrationTestDirectory();
		PrepareMigration(directory, "/data/hosting.db");
		var values = new Dictionary<string, string> { ["name"] = "wrong-name", ["version"] = "9.9.9", ["platform"] = "windows", ["architecture"] = "Arm64", ["output"] = "wrong-output", ["zongsoft_test_input"] = "db" };
		var previous = values.ToDictionary(pair => pair.Key, pair => Environment.GetEnvironmentVariable(pair.Key));
		try
		{
			foreach(var pair in values)
				Environment.SetEnvironmentVariable(pair.Key, pair.Value);
			var arguments = Arguments("zongsoft.daemon", "Linux", "X64");
			arguments.Add("$(zongsoft_test_input).migration");

			var result = await RunAsync(directory, arguments);

			Assert.True(result.Code == 0, result.Output);
			var archive = Path.Combine(directory.Path, "out", "zongsoft.daemon-migrate@1.2.3_linux-x64.tar.gz");
			var entry = Assert.Single(ReadArchive(archive), entry => entry.Name.EndsWith(".migration/migration.json", StringComparison.Ordinal));
			using var plan = JsonDocument.Parse(entry.Content);
			Assert.Equal("linux-x64", plan.RootElement.GetProperty("Runtime").GetString());
			Assert.Equal("zongsoft.daemon-migrate", plan.RootElement.GetProperty("Name").GetString());
			Assert.Equal("1.2.3", plan.RootElement.GetProperty("Version").GetString());
			Assert.False(Directory.Exists(Path.Combine(directory.Path, "wrong-output")));
		}
		finally { foreach(var pair in previous) Environment.SetEnvironmentVariable(pair.Key, pair.Value); }
	}

	[Fact(Skip = "Requires Windows file-sharing semantics.", SkipUnless = nameof(IsWindows))]
	public async Task Execute_SecondOutputLocked_RollsBackPublishedArchiveAndPreservesBothOriginalsAsync()
	{
		using var directory = new MigrationTestDirectory();
		PrepareMigration(directory, "/data/hosting.db");
		var archive = directory.Write("out/zongsoft.daemon-migrate@1.2.3_linux-x64.tar.gz", "original-archive");
		var launcher = directory.Write("out/zongsoft.daemon-migrate@1.2.3_linux-x64.sh", "original-launcher");
		using var held = new FileStream(launcher, FileMode.Open, FileAccess.Read, FileShare.Read);
		var arguments = Arguments("zongsoft.daemon", "Linux", "X64");
		arguments.Add("--overwrite");
		arguments.Add("db.migration");

		var result = await RunAsync(directory, arguments);

		Assert.NotEqual(0, result.Code);
		Assert.Equal(unchecked((int)0x80070020), Assert.IsType<IOException>(result.Error).HResult);
		Assert.Equal("original-archive", File.ReadAllText(archive));
		Assert.Equal("original-launcher", File.ReadAllText(launcher));
		Assert.Empty(Directory.GetDirectories(Path.Combine(directory.Path, "out")));
	}

	[Fact]
	public async Task Launcher_CheckUsesSelectedStateWithoutArchiveOrExtractionAsync()
	{
		using var directory = new MigrationTestDirectory();
		var windows = OperatingSystem.IsWindows();
		var runtime = MigrationTestDirectory.CurrentRuntime;
		PrepareMigration(directory, windows ? Path.Combine(directory.Path, "not-created.db") : "/tmp/not-created.db");
		var arguments = Arguments("zongsoft.daemon", windows ? "win" : "Linux", System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture.ToString());
		arguments.Add("db.migration");
		var generated = await RunAsync(directory, arguments);
		Assert.True(generated.Code == 0, generated.Output);
		var prefix = "out/zongsoft.daemon-migrate@1.2.3_" + runtime;
		var archive = Path.Combine(directory.Path, prefix + ".tar.gz");
		var fingerprint = Encoding.UTF8.GetString(Assert.Single(ReadArchive(archive), entry => entry.Name == ".migration/id").Content);
		var launcher = Path.Combine(directory.Path, prefix + (windows ? ".cmd" : ".sh"));
		var defaultReady = directory.Write("out/.migration/zongsoft.daemon-migrate/ready", fingerprint);
		var overrideReady = directory.Write("selected-state/ready", "wrong-fingerprint");
		File.Delete(archive);
		var before = Directory.GetDirectories(Path.GetTempPath(), "zongsoft-migrate-*").Order(StringComparer.Ordinal).ToArray();

		await RunLauncherAsync(launcher, "check", null, 0);
		await RunLauncherAsync(launcher, "check", Path.GetDirectoryName(overrideReady), 1);
		File.WriteAllText(defaultReady, "wrong-fingerprint");
		File.WriteAllText(overrideReady, fingerprint);
		await RunLauncherAsync(launcher, "check", null, 1);
		await RunLauncherAsync(launcher, "check", Path.GetDirectoryName(overrideReady), 0);

		Assert.Equal(before, Directory.GetDirectories(Path.GetTempPath(), "zongsoft-migrate-*").Order(StringComparer.Ordinal));
		Assert.False(File.Exists(archive));
		Assert.False(File.Exists(Path.Combine(directory.Path, "not-created.db")));
		Assert.Equal(new[] { overrideReady }, Directory.GetFiles(Path.GetDirectoryName(overrideReady)));
	}

	[Fact]
	public async Task Launcher_ApplyForwardsActionAndStateAndPreservesExitCodeAsync()
	{
		using var directory = new MigrationTestDirectory();
		var windows = OperatingSystem.IsWindows();
		PrepareMigration(directory, windows ? Path.Combine(directory.Path, "not-created.db") : "/tmp/not-created.db");
		var arguments = Arguments("zongsoft.daemon", windows ? "win" : "Linux", System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture.ToString());
		arguments.Add("db.migration");
		var generated = await RunAsync(directory, arguments);
		Assert.True(generated.Code == 0, generated.Output);
		var prefix = "out/zongsoft.daemon-migrate@1.2.3_" + MigrationTestDirectory.CurrentRuntime;
		var archive = Path.Combine(directory.Path, prefix + ".tar.gz");
		var extension = windows ? ".cmd" : ".sh";
		var launcher = Path.Combine(directory.Path, prefix + extension);
		var state = Path.GetDirectoryName(directory.Write("state with spaces/marker", "unchanged"));
		// Replace only the isolated execution boundary with a recording script; no native database process runs.
		using(var file = File.Create(archive))
		using(var gzip = new GZipStream(file, CompressionLevel.Optimal))
		using(var writer = new TarWriter(gzip, TarEntryFormat.Pax, false))
		using(var content = new MemoryStream(Encoding.UTF8.GetBytes(windows ? "@echo off\r\n> \"%~2\\observed.txt\" echo %~1\r\nexit /b 7\r\n" : "#!/bin/sh\nprintf '%s' \"$1\" > \"$2/observed.txt\"\nexit 7\n")))
			writer.WriteEntry(new PaxTarEntry(TarEntryType.RegularFile, ".migration/migrate" + extension) { DataStream = content, Mode = (UnixFileMode)493 });
		var before = Directory.GetDirectories(Path.GetTempPath(), "zongsoft-migrate-*").Order(StringComparer.Ordinal).ToArray();

		await RunLauncherAsync(launcher, "apply", state, 7);

		Assert.Equal("apply", File.ReadAllText(Path.Combine(state, "observed.txt")).Trim());
		Assert.Equal("unchanged", File.ReadAllText(Path.Combine(state, "marker")));
		Assert.False(Directory.Exists(Path.Combine(directory.Path, "out", ".migration")));
		Assert.False(File.Exists(Path.Combine(directory.Path, "not-created.db")));
		Assert.Equal(before, Directory.GetDirectories(Path.GetTempPath(), "zongsoft-migrate-*").Order(StringComparer.Ordinal));
	}

	[Theory]
	[InlineData("linux-x64", "Linux", "X64")]
	[InlineData("linux-arm64", "Linux", "Arm64")]
	[InlineData("win-x64", "win", "X64")]
	public async Task Launcher_ForwardsExplicitStateAndChecksBeforeExtractionAsync(string runtime, string platform, string architecture)
	{
		using var directory = new MigrationTestDirectory();
		PrepareMigration(directory, runtime == "win-x64" ? "C:/Zongsoft/hosting.db" : "/var/lib/zongsoft/hosting.db");
		var arguments = Arguments("zongsoft.daemon", platform, architecture);
		arguments.Add("db.migration");
		var generated = await RunAsync(directory, arguments);
		Assert.True(generated.Code == 0, generated.Output);
		var prefix = "out/zongsoft.daemon-migrate@1.2.3_" + runtime;
		var script = File.ReadAllText(Path.Combine(directory.Path, prefix + (runtime == "win-x64" ? ".cmd" : ".sh")));
		if(runtime == "win-x64")
		{
			Assert.Contains("set \"MIGRATION_STATE=%~2\"", script);
			Assert.Contains("$state=$env:MIGRATION_STATE", script);
			Assert.True(script.IndexOf("if($action -eq 'check')", StringComparison.Ordinal) < script.IndexOf("tar.exe -xzf", StringComparison.Ordinal));
			Assert.Contains("$action $state", script);
		}
		else
		{
			Assert.Contains("STATE_DIR=${2:-", script);
			Assert.True(script.IndexOf("if [ \"$ACTION\" = check ]", StringComparison.Ordinal) < script.IndexOf("WORK_DIR=$(mktemp", StringComparison.Ordinal));
			Assert.Contains("\"$ACTION\" \"$STATE_DIR\"", script);
		}
	}
	#endregion

	#region 错误测试
	[Theory]
	[InlineData("name")]
	[InlineData("platform")]
	public async Task Execute_MissingRequiredOption_DoesNotInferItFromSourceVersionAsync(string option)
	{
		using var directory = new MigrationTestDirectory();
		var version = directory.Write(".version", "Zongsoft.Hosting@9.0.0");
		PrepareMigration(directory, "/data/hosting.db");
		var arguments = Arguments("zongsoft.daemon", "Linux", "X64");
		arguments.RemoveAll(argument => argument.StartsWith("--" + option + ":", StringComparison.Ordinal));
		arguments.Add("db.migration");

		var result = await RunAsync(directory, arguments);

		Assert.NotEqual(0, result.Code);
		Assert.IsType<InvalidOperationException>(result.Error);
		Assert.Equal("Zongsoft.Hosting@9.0.0", File.ReadAllText(version));
		Assert.False(Directory.Exists(Path.Combine(directory.Path, "out")));
	}

	[Theory]
	[InlineData("--version:0.0.0")]
	[InlineData("--name: ")]
	public async Task Execute_ZeroVersionOrBlankName_RejectsIdentityBeforeCreatingOutputsAsync(string invalid)
	{
		using var directory = new MigrationTestDirectory();
		PrepareMigration(directory, "/data/hosting.db");
		var arguments = Arguments("zongsoft.daemon", "Linux", "X64");
		var option = invalid[..invalid.IndexOf(':')];
		arguments.RemoveAll(argument => argument.StartsWith(option + ":", StringComparison.Ordinal));
		arguments.Add(invalid);
		arguments.Add("db.migration");

		var result = await RunAsync(directory, arguments);

		Assert.NotEqual(0, result.Code);
		Assert.IsType<InvalidOperationException>(result.Error);
		Assert.False(Directory.Exists(Path.Combine(directory.Path, "out")));
	}

	[Fact]
	public async Task Execute_UndefinedInputVariable_ReportsVariableWithoutCreatingOutputsAsync()
	{
		using var directory = new MigrationTestDirectory();
		var arguments = Arguments("zongsoft.daemon", "Linux", "X64");
		arguments.Add("$(zongsoft_test_undefined_variable).migration");

		var result = await RunAsync(directory, arguments);

		Assert.NotEqual(0, result.Code);
		Assert.Contains("zongsoft_test_undefined_variable", Assert.IsType<InvalidOperationException>(result.Error).Message);
		Assert.False(Directory.Exists(Path.Combine(directory.Path, "out")));
	}

	[Theory]
	[InlineData("macos", "X64")]
	[InlineData("osx", "X64")]
	[InlineData("xos", "X64")]
	[InlineData("unix", "X64")]
	[InlineData("windows", "Arm64")]
	[InlineData("Linux", "X86")]
	public async Task Execute_UnsupportedPlatformOrArchitecture_FailsWithoutOutputsAsync(string platform, string architecture)
	{
		using var directory = new MigrationTestDirectory();
		var arguments = Arguments("zongsoft.daemon", platform, architecture);
		arguments.Add("missing.migration");

		var result = await RunAsync(directory, arguments);

		Assert.NotEqual(0, result.Code);
		if(platform is "windows" or "Linux")
			Assert.IsType<InvalidDataException>(result.Error);
		else
			Assert.IsType<InvalidOperationException>(result.Error);
		Assert.False(Directory.Exists(Path.Combine(directory.Path, "out")));
	}

	[Theory]
	[InlineData(false)]
	[InlineData(true)]
	public async Task Execute_NoInputsOrAllMissing_FailsWithoutOutputsAsync(bool missing)
	{
		using var directory = new MigrationTestDirectory();
		var arguments = Arguments("zongsoft.daemon", "Linux", "X64");
		if(missing)
			arguments.Add("absent/*.migration");

		var result = await RunAsync(directory, arguments);

		Assert.NotEqual(0, result.Code);
		Assert.IsType<InvalidOperationException>(result.Error);
		Assert.False(Directory.Exists(Path.Combine(directory.Path, "out")));
	}
	#endregion

	#region 辅助方法
	private static async Task RunLauncherAsync(string launcher, string action, string state, int expected)
	{
		using var process = new Process
		{
			StartInfo = new()
			{
				FileName = OperatingSystem.IsWindows() ? Environment.GetEnvironmentVariable("COMSPEC") ?? "cmd.exe" : "/bin/sh",
				UseShellExecute = false,
				CreateNoWindow = true,
				RedirectStandardOutput = true,
				RedirectStandardError = true,
			},
		};
		if(OperatingSystem.IsWindows())
		{
			process.StartInfo.ArgumentList.Add("/d");
			process.StartInfo.ArgumentList.Add("/c");
		}
		process.StartInfo.ArgumentList.Add(launcher);
		process.StartInfo.ArgumentList.Add(action);
		if(state != null)
			process.StartInfo.ArgumentList.Add(state);
		Assert.True(process.Start());
		var output = process.StandardOutput.ReadToEndAsync(TestContext.Current.CancellationToken);
		var error = process.StandardError.ReadToEndAsync(TestContext.Current.CancellationToken);
		try
		{
			await process.WaitForExitAsync(TestContext.Current.CancellationToken).WaitAsync(TimeSpan.FromSeconds(30), TestContext.Current.CancellationToken);
			Assert.True(process.ExitCode == expected, $"Expected {expected}, got {process.ExitCode}: {await output} {await error}");
		}
		finally
		{
			if(!process.HasExited)
				process.Kill(true);
		}
	}

	private static void PrepareMigration(MigrationTestDirectory directory, string database)
	{
		directory.Write("db.migration", "[sqlite]\n./schema.sql\n");
		directory.Write("db.env", "[sqlite]\nDatabase=" + database + "\n");
		directory.Write("schema.sql", "CREATE TABLE must_not_run (id INTEGER);");
	}

	private static List<string> Arguments(string name, string platform, string architecture) => ["migrate", "--name:" + name, "--version:1.2.3", "--platform:" + platform, "--architecture:" + architecture, "--output:out"];

	private static async Task<(int Code, string Output, Exception Error)> RunAsync(MigrationTestDirectory directory, IEnumerable<string> arguments, string workingDirectory = null)
	{
		var tool = Path.Combine(directory.Path, "tool");
		Directory.CreateDirectory(tool);
		var runtime = directory.CreateRuntime();
		foreach(var file in Directory.EnumerateFiles(runtime, "*", SearchOption.AllDirectories))
		{
			var target = Path.Combine(tool, ".migrator", Path.GetRelativePath(runtime, file));
			Directory.CreateDirectory(Path.GetDirectoryName(target));
			File.Copy(file, target, true);
		}

		var previousDirectory = Environment.CurrentDirectory;
		var previousBase = AppContext.GetData("APP_CONTEXT_BASE_DIRECTORY");
		// Reading Terminal.Default would initialize the console even when no input handle exists.
		var terminalField = typeof(Terminal).GetField("_default", BindingFlags.NonPublic | BindingFlags.Static);
		Assert.NotNull(terminalField);
		var previousTerminal = (ITerminal)terminalField.GetValue(null);
		var terminal = DispatchProxy.Create<ITerminal, RecordingTerminal>();
		try
		{
			Environment.CurrentDirectory = workingDirectory ?? directory.Path;
			AppContext.SetData("APP_CONTEXT_BASE_DIRECTORY", tool);
			Terminal.Default = terminal;
			var command = new MigrateCommand();
			var context = new CommandContext(new CommandExecutor(), CommandLine.Parse(Program.GetCommandLine(arguments.Skip(1).ToArray()))[0], command, null);
			var result = await ((ICommand)command).ExecuteAsync(context, TestContext.Current.CancellationToken);
			return (result == null ? 1 : 0, ((RecordingTerminal)terminal).Output.ToString(), null);
		}
		catch(Exception error) { return (1, error.ToString(), error); }
		finally
		{
			Terminal.Default = previousTerminal;
			AppContext.SetData("APP_CONTEXT_BASE_DIRECTORY", previousBase);
			Environment.CurrentDirectory = previousDirectory;
		}
	}

	private static List<(string Name, UnixFileMode Mode, byte[] Content)> ReadArchive(string path)
	{
		using var file = File.OpenRead(path);
		using var gzip = new GZipStream(file, CompressionMode.Decompress);
		using var reader = new TarReader(gzip);
		var result = new List<(string, UnixFileMode, byte[])>();
		TarEntry entry;
		while((entry = reader.GetNextEntry()) != null)
		{
			if(entry.DataStream == null)
				continue;
			using var content = new MemoryStream();
			entry.DataStream.CopyTo(content);
			result.Add((entry.Name, entry.Mode, content.ToArray()));
		}
		return result;
	}
	#endregion

	#region 嵌套类型
	public class RecordingTerminal : DispatchProxy
	{
		#region 属性定义
		public StringWriter Output { get; } = new();
		#endregion

		#region 模拟方法
		protected override object Invoke(MethodInfo method, object[] arguments)
		{
			if(method.Name.StartsWith("Write", StringComparison.Ordinal))
			{
				if(arguments.Length > 0)
					this.Output.Write(arguments[^1]);
				if(method.Name == "WriteLine")
					this.Output.WriteLine();
			}

			if(method.Name is "get_Output" or "get_Writer" or "get_Error")
				return this.Output;
			if(method.Name == "get_Encoding")
				return Encoding.UTF8;
			return null;
		}
		#endregion
	}
	#endregion
}
