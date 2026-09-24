using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;

using Xunit;

using Zongsoft.Configuration.Profiles;

namespace Zongsoft.Tools.Migrator.Tests;

public sealed partial class MigrateCommandTest
{
	[Theory]
	[InlineData(false)]
	[InlineData(true)]
	public async Task Execute_EnvironmentFilesSupplyVersionAndParametersForAllInputsAsync(bool emptySecret)
	{
		using var directory = new MigrationTestDirectory();
		directory.Write(".env", "zongsoft_env_secret=ancestor\nzongsoft_env_version=versions/release.version\nzongsoft_env_db=hosting\nzongsoft_env_host=ancestor-host\n");
		directory.Write("workspace/missing/current/.env", "ZONGSOFT_ENV_SECRET=near\nversion=9.9.9\nzongsoft_env_title=$(name) $(version)\n[io rustfs]\naccess_key=fixture-access\nsecret_key=fixture-secret\n");
		directory.Write("workspace/missing/current/versions/release.version", "Ignored.Name@2.3.4\n");
		directory.Write("workspace/missing/current/db.migration", "[postgres]\n./schema.sql\n");
		directory.Write("workspace/missing/current/db.ini", "#@import settings/database.ini\n[postgres]\nPassword=$(zongsoft_env_secret)\n");
		directory.Write("workspace/missing/current/settings/database.ini", "[postgres]\nServer=$(zongsoft_env_host)\nDatabase=$(zongsoft_env_db)\nUserName=fixture-user\n");
		directory.Write("workspace/missing/current/schema.sql", "SELECT 1;");
		directory.Write("workspace/missing/current/fs.migration", "[amazon.s3]\nattachments=private\n");
		directory.Write("workspace/missing/current/amazon.s3.ini", "Server=http://$(zongsoft_env_host):9000\nRegion=us-east-1\nAccessKey=$(io_rustfs_access_key)\nSecretKey=%io_rustfs_secret_key%\n");
		var previous = Environment.GetEnvironmentVariable("zongsoft_env_secret");
		try
		{
			Environment.SetEnvironmentVariable("zongsoft_env_secret", "process");
			var arguments = Arguments("zongsoft.daemon", "Linux", "X64");
			arguments.Remove("--version:1.2.3");
			arguments.Add("--version:$(zongsoft_env_version)");
			arguments.Add("--title:$(zongsoft_env_title)");
			arguments.Add("--zongsoft_env_host:command-host");
			if(emptySecret)
				arguments.Add("--zongsoft_env_secret:");
			arguments.Add("db.migration");
			arguments.Add("fs.migration");
			var workingDirectory = Path.Combine(directory.Path, "workspace", "missing", "current");

			var result = await RunAsync(directory, arguments, workingDirectory);

			Assert.True(result.Code == 0, result.Output);
			var archive = Path.Combine(workingDirectory, "out", "zongsoft.daemon-migrate@2.3.4_linux-x64.tar.gz");
			using var plan = JsonDocument.Parse(Assert.Single(ReadArchive(archive), entry => entry.Name == ".migration/migration.json").Content);
			Assert.Equal("2.3.4", plan.RootElement.GetProperty("Version").GetString());
			Assert.Equal("zongsoft.daemon 2.3.4", plan.RootElement.GetProperty("Title").GetString());
			var database = Assert.Single(plan.RootElement.GetProperty("Databases").EnumerateArray());
			Assert.Equal("hosting", database.GetProperty("Name").GetString());
			Assert.Equal("command-host", database.GetProperty("Settings").GetProperty("Server").GetString());
			Assert.Equal(emptySecret ? string.Empty : "near", database.GetProperty("Settings").GetProperty("Password").GetString());
			var steps = plan.RootElement.GetProperty("Steps").EnumerateArray().ToArray();
			Assert.Equal(2, steps.Length);
			var storage = Assert.Single(steps, step => step.GetProperty("Provider").GetString() == "amazon.s3");
			Assert.Equal("http://command-host:9000", storage.GetProperty("Settings").GetProperty("Server").GetString());
			Assert.Equal("fixture-access", storage.GetProperty("Settings").GetProperty("AccessKey").GetString());
			Assert.Equal("fixture-secret", storage.GetProperty("Settings").GetProperty("SecretKey").GetString());
			Assert.Equal("process", Environment.GetEnvironmentVariable("zongsoft_env_secret"));
			Assert.False(Directory.Exists(Path.Combine(directory.Path, "out")));
		}
		finally
		{
			Environment.SetEnvironmentVariable("zongsoft_env_secret", previous);
		}
	}

	[Fact]
	public async Task Execute_EnvironmentFileFailurePreservesArtifactsAsync()
	{
		using var directory = new MigrationTestDirectory();
		directory.Write(".env", "#@import .env\n");
		var archive = directory.Write("out/zongsoft.daemon-migrate@1.2.3_linux-x64.tar.gz", "previous archive");
		var launcher = directory.Write("out/zongsoft.daemon-migrate@1.2.3_linux-x64.sh", "previous launcher");
		PrepareMigration(directory, "/data/hosting.db");
		var arguments = Arguments("zongsoft.daemon", "Linux", "X64");
		arguments.Add("--overwrite");
		arguments.Add("db.migration");

		var result = await RunAsync(directory, arguments);

		Assert.NotEqual(0, result.Code);
		Assert.IsType<ProfileException>(result.Error);
		Assert.Equal("previous archive", File.ReadAllText(archive));
		Assert.Equal("previous launcher", File.ReadAllText(launcher));
		Assert.Equal(2, Directory.GetFiles(Path.Combine(directory.Path, "out")).Length);
	}
}
