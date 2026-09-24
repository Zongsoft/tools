using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Collections.Generic;
using System.Security.Cryptography;

using Xunit;

using Zongsoft.Tools.Migrator.Migration;

namespace Zongsoft.Tools.Migrator.Tests;

public sealed class MigrationLoaderTest
{
	#region 测试方法
	[Theory]
	[InlineData("sqlite", "[sqlite]\n./schema.sql\n", "[sqlite]\nDatabase=/data/legacy.db\n")]
	[InlineData("amazon.s3", "[amazon.s3]\nattachments=private\n", "[amazon.s3]\nServer=http://legacy.invalid:9000\nRegion=us-east-1\nAccessKey=legacy\nSecretKey=legacy\n")]
	public void Load_LegacyEnvironmentParametersAreNotSelected(string provider, string migration, string settings)
	{
		using var directory = new MigrationTestDirectory();
		directory.Write("main.migration", migration);
		directory.Write("main.env", settings);
		directory.Write(provider + ".env", settings);
		directory.Write("schema.sql", "SELECT 1;");

		var error = Assert.Throws<InvalidDataException>(() => Loader().Load("main.migration", directory.Path, "zongsoft.daemon", "1.0.0"));

		Assert.Contains(Path.Combine(directory.Path, "main.ini"), error.Message);
		Assert.Contains(Path.Combine(directory.Path, provider + ".ini"), error.Message);
		Assert.DoesNotContain("main.env", error.Message);
		Assert.DoesNotContain(provider + ".env", error.Message);
	}

	[Theory]
	[InlineData(false)]
	[InlineData(true)]
	public void Load_ExistingIniAsRootOrImport_RejectsItsExactPath(bool imported)
	{
		using var directory = new MigrationTestDirectory();
		var invalid = directory.Write("legacy.ini", "[sqlite]\n./schema.sql\n");
		directory.Write("main.migration", "#@import legacy.ini\n");
		directory.Write("sqlite.ini", "[sqlite]\nDatabase=/data/hosting.db\n");
		directory.Write("schema.sql", "SELECT 1;");

		var error = Assert.Throws<InvalidDataException>(() => Loader().Load(imported ? "main.migration" : "legacy.ini", directory.Path, "zongsoft.daemon", "1.0.0"));

		Assert.Contains(invalid, error.Message);
	}

	[Fact]
	public void Load_GlobSelectsExistingIni_RejectsMatchedFile()
	{
		using var directory = new MigrationTestDirectory();
		var invalid = directory.Write("parts/old.ini", "[sqlite]\n./schema.sql\n");

		var error = Assert.Throws<InvalidDataException>(() => Loader().Load("parts/*.ini", directory.Path, "zongsoft.daemon", "1.0.0"));

		Assert.Contains(invalid, error.Message);
	}

	[Theory]
	[InlineData(@"\\server\share\hosting.db", true)]
	[InlineData(@"relative\hosting.db", false)]
	[InlineData(@"C:hosting.db", false)]
	[InlineData(@"\\server", false)]
	[InlineData(@"\\?\C:\hosting.db", false)]
	[InlineData(@"\\.\device", false)]
	public void Load_WindowsDatabasePath_AcceptsUncAndRejectsRelativeOrDevicePaths(string database, bool valid)
	{
		using var directory = new MigrationTestDirectory();
		directory.Write("db.migration", "[sqlite]\n./schema.sql\n");
		directory.Write("db.ini", "[sqlite]\nDatabase=" + database + "\n");
		directory.Write("schema.sql", "SELECT 1;");

		if(valid)
		{
			var plan = Loader().Load("db.migration", directory.Path, "zongsoft.daemon", "1.0.0", "win-x64");
			Assert.Equal(database, Assert.Single(plan.Databases).Options["Path"]);
			Assert.Equal("win-x64", plan.Runtime);
		}
		else
		{
			var error = Assert.Throws<InvalidDataException>(() => Loader().Load("db.migration", directory.Path, "zongsoft.daemon", "1.0.0", "win-x64"));
			Assert.Contains("Path", error.Message);
		}
	}

	[Fact]
	public void Load_EnumerableExpressions_PreservesInputPositionsGlobOrderAndRepeatedSources()
	{
		using var directory = new MigrationTestDirectory();
		foreach(var name in new[] { "last", "parts/b", "parts/a" })
		{
			directory.Write(name + ".migration", "[sqlite]\n./schema.sql\n");
			directory.Write(name + ".ini", "[sqlite]\nDatabase=/data/" + name.Replace('/', '-') + ".db\n");
		}
		directory.Write("schema.sql", "SELECT 'root';");
		directory.Write("parts/schema.sql", "SELECT 'parts';");
		IEnumerable<string> inputs = new[] { "last.migration", "parts/*.migration", "last.migration" }.Select(value => value);

		var plan = Loader().Load(inputs, directory.Path, "zongsoft.daemon", "1.0.0", "linux-arm64");

		Assert.Equal("linux-arm64", plan.Runtime);
		Assert.Equal(new[] { "/data/last.db", "/data/parts-a.db", "/data/parts-b.db", "/data/last.db" }, plan.Steps.Select(step => plan.Databases[step.DatabaseIndex.Value].Options["Path"]));
		Assert.Equal(new[] { ".migration/.artifacts/sqlite/1.sql", ".migration/.artifacts/sqlite/2.sql", ".migration/.artifacts/sqlite/3.sql", ".migration/.artifacts/sqlite/4.sql" }, plan.Steps.SelectMany(step => step.Scripts).Select(script => script.Path));
	}

	[Fact]
	public void Load_WindowsRuntime_AcceptsAbsoluteDatabasePathAndUsesIniParameters()
	{
		using var directory = new MigrationTestDirectory();
		directory.Write("db.migration", "[sqlite]\n./schema.sql\n");
		directory.Write("db.ini", "[sqlite]\nDatabase=C:\\Zongsoft\\hosting.db\n");
		directory.Write("schema.sql", "SELECT 1;");

		var plan = Loader().Load("db.migration", directory.Path, "zongsoft.daemon", "1.0.0", "win-x64");

		Assert.Equal("win-x64", plan.Runtime);
		Assert.Equal(@"C:\Zongsoft\hosting.db", Assert.Single(plan.Databases).Options["Path"]);
	}

	[Fact]
	public void Load_ImportDirectivesLoadIniAndParameters()
	{
		using var directory = new MigrationTestDirectory();
		directory.Write("migration/main.migration", "#@import imported.migration\n[sqlite]\n./schema.sql\n");
		directory.Write("migration/imported.migration", "[sqlite]\n./imported.sql\n");
		directory.Write("migration/sqlite.ini", "[sqlite]\nDatabase=/var/lib/zongsoft/original.db\n#@import imported.ini\n");
		directory.Write("migration/imported.ini", "[sqlite]\nDatabase=/var/lib/zongsoft/overridden.db\n");
		var sql = directory.Write("migration/schema.sql", "SELECT 'intended';");
		var imported = directory.Write("migration/imported.sql", "SELECT 'imported';");

		var plan = Loader().Load("migration/main.migration", directory.Path, "zongsoft.test", "1.0.0");

		Assert.Equal(2, plan.Steps.Count);
		Assert.All(plan.Steps, task => Assert.Equal("sqlite", task.Provider));
		Assert.All(plan.Steps, task => Assert.Equal("/var/lib/zongsoft/overridden.db", plan.Databases[task.DatabaseIndex.Value].Options["Path"]));
		Assert.Equal(new[] { imported, sql }, plan.Steps.SelectMany(task => task.Scripts).Select(script => script.Source));
		Assert.Equal(new[] { "SELECT 'imported';", "SELECT 'intended';" }, plan.Steps.SelectMany(task => task.Scripts).Select(script => script.Content));
	}

	[Theory]
	[InlineData(";")]
	[InlineData("|")]
	public void Load_MultipleFilesVariablesAndGlobs_PreservesOrderAndLiteralSql(string separator)
	{
		using var directory = new MigrationTestDirectory();
		directory.Write("migration/sql/020-postgres.sql", "SELECT '$(literal);%unchanged%';");
		directory.Write("migration/sql/010-postgres.sql", "SELECT 1;");
		directory.Write("migration/db-production.migration", "[POSTGRESQL]\n./sql/*-postgres.sql\n./sql/010-postgres.sql\n");
		directory.Write("migration/postgres.ini", "[postgres]\nServer=localhost\nDatabase=hosting\nUserName=operator\nPassword=$(secret)\n");
		directory.Write("migration/fs.migration", "[AMAZON.S3]\nattachments=Private\ndownloads=PUBLIC\n");
		directory.Write("migration/amazon.s3.ini", "Server=http://localhost:9000\nRegion=us-east-1\nAccessKey=test\nSecretKey=test\n");
		var loader = Loader(new() { ["environment"] = "production", ["secret"] = "value=with;punctuation\n[new]" });

		var plan = loader.Load($"migration/db-$(environment).migration{separator}migration/fs.migration", directory.Path, "zongsoft.web", "1.1.0");

		Assert.Equal(new[] { "postgres", "amazon.s3" }, plan.Steps.Select(task => task.Provider));
		Assert.Equal(new[] { "010-postgres.sql", "020-postgres.sql" }, plan.Steps[0].Scripts.Select(script => Path.GetFileName(script.Source)));
		Assert.Equal("value=with;punctuation\n[new]", plan.Databases[0].Settings["Password"]);
		Assert.Equal("SELECT '$(literal);%unchanged%';", File.ReadAllText(plan.Steps[0].Scripts[1].Source));
		Assert.Equal(new[] { "attachments", "downloads" }, plan.Steps[1].Buckets.Select(bucket => bucket.Name));
		Assert.False(plan.Steps[1].Buckets[0].Public);
		Assert.True(plan.Steps[1].Buckets[1].Public);
		Assert.Equal("zongsoft.web", plan.Name);
		Assert.Equal("1.1.0", plan.Version);
	}

	[Theory]
	[InlineData(";")]
	[InlineData("|")]
	public void Load_MigrationIniGlob_ExpandsHostingVariablesOrdersFilesAndUsesAncestorParameters(string separator)
	{
		using var directory = new MigrationTestDirectory();
		var source = Path.Combine(directory.Path, "hosting", "web", "default");
		Directory.CreateDirectory(source);
		directory.Write("hosting/.deploy/default/migration/1.1.0/zongsoft.db-production.migration", "[postgresql]\n./sql/schema.sql\n");
		directory.Write("hosting/.deploy/default/migration/1.1.0/Zongsoft.fs-production.migration", "[amazon.s3]\nattachments=private\n");
		directory.Write("hosting/.deploy/default/migration/1.1.0/zongsoft.db-development.migration", "[must-not-be-selected]\ninvalid\n");
		var sql = directory.Write("hosting/.deploy/default/migration/1.1.0/sql/schema.sql", "SELECT 'hosting';");
		directory.Write("hosting/.deploy/default/migration/postgres.ini", "[postgres]\nServer=ancestor-db\nDatabase=hosting\nUserName=operator\nPassword=\n");
		directory.Write("hosting/.deploy/default/migration/amazon.s3.ini", "Server=http://ancestor-fs:9000\nRegion=us-east-1\nAccessKey=test\nSecretKey=test\n");
		var loader = Loader(new() { ["scheme"] = "default", ["version"] = "1.1.0", ["environment"] = "production" });

		var plan = loader.Load("../../.deploy/$(scheme)/migration/$(version)/*-$(environment).migration" + separator, source, "zongsoft.web", "1.1.0");

		Assert.Equal(new[] { "amazon.s3", "postgres" }, plan.Steps.Select(task => task.Provider));

		Assert.Equal("attachments", Assert.Single(plan.Steps[0].Buckets).Name);
		Assert.False(plan.Steps[0].Buckets[0].Public);
		Assert.Equal("http://ancestor-fs:9000", plan.Steps[0].Settings["Server"]);
		Assert.Equal("ancestor-db", plan.Databases[0].Settings["Server"]);
		Assert.Equal(sql, Assert.Single(plan.Steps[1].Scripts).Source);
		Assert.Equal("SELECT 'hosting';", plan.Steps[1].Scripts[0].Content);
	}

	[Fact]
	public void Load_MigrationIniGlob_RespectsArgumentOrderAndRetainsRepeatedExplicitFile()
	{
		using var directory = new MigrationTestDirectory();
		directory.Write("migration/01-db.migration", "[sqlite]\n./schema.sql\n");
		directory.Write("migration/02-fs.migration", "[amazon.s3]\nattachments=private\n");
		directory.Write("migration/sqlite.ini", "[sqlite]\nDatabase=/var/lib/zongsoft/hosting.db\n");
		directory.Write("migration/amazon.s3.ini", "Server=http://localhost:9000\nRegion=us-east-1\nAccessKey=test\nSecretKey=test\n");
		directory.Write("migration/schema.sql", "SELECT 1;");

		var plan = Loader().Load("migration/02-fs.migration;migration/*.migration|migration/02-fs.migration;", directory.Path, "zongsoft.daemon", "1.1.0");

		Assert.Equal(new[] { "amazon.s3", "sqlite", "amazon.s3", "amazon.s3" }, plan.Steps.Select(task => task.Provider));

		Assert.Equal("attachments", Assert.Single(plan.Steps[0].Buckets).Name);
		Assert.Equal("attachments", Assert.Single(plan.Steps[2].Buckets).Name);
		Assert.Equal("attachments", Assert.Single(plan.Steps[3].Buckets).Name);
	}

	[Theory]
	[InlineData(false)]
	[InlineData(true)]
	public void Load_MigrationIniGlob_WithoutMatchesReportsRequestedPattern(bool directoryExists)
	{
		using var directory = new MigrationTestDirectory();
		if(directoryExists)
			directory.Write("migration/readme.txt", "No migration INI exists.");
		var pattern = Path.Combine(directory.Path, "migration", "*.migration");

		var warnings = new List<string>();

		var plan = Loader(warning: warnings.Add).Load("migration/*.migration", directory.Path, "zongsoft.daemon", "1.1.0");

		Assert.Null(plan);
		Assert.Contains(pattern, Assert.Single(warnings));
	}

	[Fact]
	public void Load_MissingFilesAmongExistingFiles_WarnsAndPreservesValidTasksInOrder()
	{
		using var directory = new MigrationTestDirectory();
		directory.Write("db.migration", "[sqlite]\n./schema.sql\n");
		directory.Write("sqlite.ini", "[sqlite]\nDatabase=/var/lib/zongsoft/hosting.db\n");
		directory.Write("schema.sql", "SELECT 1;");
		directory.Write("fs.migration", "[amazon.s3]\nattachments=private\n");
		directory.Write("amazon.s3.ini", "Server=http://localhost:9000\nRegion=us-east-1\nAccessKey=test\nSecretKey=test\n");
		var warnings = new List<string>();
		var loader = Loader(new() { ["environment"] = "production" }, warnings.Add);

		var plan = loader.Load("missing-$(environment).migration;db.migration|absent/*.migration;fs.migration|", directory.Path, "zongsoft.daemon", "1.1.0");

		Assert.Equal(new[] { "sqlite", "amazon.s3" }, plan.Steps.Select(task => task.Provider));

		Assert.Equal("SELECT 1;", Assert.Single(plan.Steps[0].Scripts).Content);
		Assert.Equal("attachments", Assert.Single(plan.Steps[1].Buckets).Name);
		Assert.Equal(2, warnings.Count);
		Assert.Contains(Path.Combine(directory.Path, "missing-production.migration"), warnings[0]);
		Assert.Contains(Path.Combine(directory.Path, "absent", "*.migration"), warnings[1]);
	}

	[Fact]
	public void Load_AllRequestedFilesMissing_ReturnsNoPlanAndWarnsForEveryExpandedPath()
	{
		using var directory = new MigrationTestDirectory();
		var warnings = new List<string>();
		var loader = Loader(new() { ["environment"] = "production" }, warnings.Add);

		var plan = loader.Load("missing-$(environment).migration;absent/*.migration|", directory.Path, "zongsoft.daemon", "1.1.0");

		Assert.Null(plan);
		Assert.Equal(2, warnings.Count);
		Assert.Contains(Path.Combine(directory.Path, "missing-production.migration"), warnings[0]);
		Assert.Contains(Path.Combine(directory.Path, "absent", "*.migration"), warnings[1]);
	}

	[Theory]
	[InlineData("db.migration", "")]
	[InlineData("db.migration", "[sqlite hosting user]\n")]
	[InlineData("db.txt", "[sqlite]\n./schema.sql\n")]
	[InlineData("db.migration", "[unknown]\ninvalid\n")]
	public void Load_ExistingInvalidInputAfterMissingFile_StillFails(string name, string content)
	{
		using var directory = new MigrationTestDirectory();
		directory.Write(name, content);
		directory.Write("sqlite.ini", "[sqlite]\nDatabase=/var/lib/zongsoft/hosting.db\n");
		directory.Write("schema.sql", "SELECT 1;");
		var warnings = new List<string>();

		Assert.Throws<InvalidDataException>(() => Loader(warning: warnings.Add).Load("missing.migration;" + name, directory.Path, "zongsoft.daemon", "1.1.0"));

		Assert.Contains(Path.Combine(directory.Path, "missing.migration"), Assert.Single(warnings));
	}

	[Theory]
	[InlineData(false)]
	[InlineData(true)]
	public void Load_MissingParametersOrSqlAfterMissingIni_StillFails(bool parametersExist)
	{
		using var directory = new MigrationTestDirectory();
		var migration = directory.Write("db.migration", "[sqlite]\n./missing.sql\n");
		if(parametersExist)
			directory.Write("sqlite.ini", "[sqlite]\nDatabase=/var/lib/zongsoft/hosting.db\n");
		var warnings = new List<string>();

		var error = Assert.Throws<InvalidDataException>(() => Loader(warning: warnings.Add).Load("missing.migration;db.migration", directory.Path, "zongsoft.daemon", "1.1.0"));

		Assert.Contains(migration, error.Message);
		Assert.Contains("sqlite", error.Message, StringComparison.OrdinalIgnoreCase);
		Assert.Contains(Path.Combine(directory.Path, "missing.migration"), Assert.Single(warnings));
	}

	[Theory]
	[InlineData("migration/*/*.migration")]
	[InlineData("migration/release?/*.migration")]
	public void Load_MigrationIniGlob_MissingWildcardParentWarnsAndSkips(string pattern)
	{
		using var directory = new MigrationTestDirectory();

		var warnings = new List<string>();

		Assert.Null(Loader(warning: warnings.Add).Load(pattern, directory.Path, "zongsoft.daemon", "1.1.0"));
		Assert.Single(warnings);
	}

	[Fact]
	public void Load_ParametersNearestProviderBeatsParentShared_AndMissingSectionContinues()
	{
		using var directory = new MigrationTestDirectory();
		var migration = directory.Write("nested/migration/db.migration", "[postgres]\n");
		directory.Write("nested/migration/db.ini", "[mysql]\nServer=wrong\n");
		directory.Write("nested/migration/postgresql.ini", "[postgres]\nServer=nearest\nDatabase=hosting\nUserName=operator\nPassword=\n");
		directory.Write("nested/db.ini", "[postgres]\nServer=parent\nDatabase=hosting\nUserName=operator\nPassword=\n");

		var parameters = Parameters(directory, migration);

		Assert.Equal("nearest", parameters["Server"]);
		Assert.Equal("30s", parameters["Timeout"]);
	}

	[Fact]
	public void Load_ParametersSameNameSharedWins_OnlySelectedSectionIsCollected()
	{
		using var directory = new MigrationTestDirectory();
		var migration = directory.Write("db.migration", "[postgres]\n");
		directory.Write("db.ini", "[POSTGRESQL]\nServer=shared\nDatabase=hosting\nUserName=operator\nPassword=\n[mysql]\nPassword=must-not-be-collected\n");
		directory.Write("postgres.ini", "[postgres]\nServer=provider\nDatabase=hosting\nUserName=operator\nPassword=ignored\n");

		var parameters = Parameters(directory, migration);

		Assert.Equal("shared", parameters["server"]);
		Assert.Equal("", parameters["Password"]);
		Assert.Equal("30s", parameters["Timeout"]);
	}

	[Fact]
	public void Load_ParametersSelectedIncompleteFile_DoesNotMergeOrFallback()
	{
		using var directory = new MigrationTestDirectory();
		var migration = directory.Write("nested/db.migration", "[postgres]\n");
		var selected = directory.Write("nested/postgres.ini", "[postgres]\nServer=nearest\nUserName=operator\nPassword=\n");
		directory.Write("postgres.ini", "[postgres]\nServer=parent\nDatabase=hosting\nUserName=operator\nPassword=\n");

		var error = Assert.Throws<InvalidDataException>(() => Parameters(directory, migration));

		Assert.Contains(selected, error.Message);
		Assert.Contains("Database", error.Message);
	}

	[Fact]
	public void Load_ParametersProviderSection_DoesNotMergeRootEntries()
	{
		using var directory = new MigrationTestDirectory();
		var migration = directory.Write("db.migration", "[postgres]\n");
		directory.Write("postgres.ini", "Database=root-only\n[postgres]\nServer=localhost\nUserName=operator\nPassword=\n");

		var error = Assert.Throws<InvalidDataException>(() => Parameters(directory, migration));

		Assert.Contains("Database", error.Message);
	}

	[Fact]
	public void Load_ParametersParentAliasFile_IsFoundWithoutOtherSections()
	{
		using var directory = new MigrationTestDirectory();
		var migration = directory.Write("deep/child/db.migration", "[postgres]\n");
		directory.Write("postgresql.ini", "[POSTGRES]\nServer=parent\nDatabase=hosting\nUserName=operator\nPassword=\n[mysql]\nPassword=other\n");

		var parameters = Parameters(directory, migration);

		Assert.Equal("parent", parameters["Server"]);
		Assert.Equal("", parameters["Password"]);
	}

	[Fact]
	public void Load_ParametersDuplicatePostgresAliases_FailsWithFileContext()
	{
		using var directory = new MigrationTestDirectory();
		var migration = directory.Write("db.migration", "[postgres]\n");
		var parameters = directory.Write("db.ini", "[postgres]\nDatabase=one\n[postgresql]\nDatabase=two\n");

		var error = Assert.Throws<InvalidDataException>(() => Parameters(directory, migration));

		Assert.Contains(parameters, error.Message);
	}

	[Theory]
	[InlineData("[unknown]\n./schema.sql\n", "unknown")]
	[InlineData("[sqlite]\n./absent*.sql\n", "sqlite")]
	[InlineData("[sqlite]\n./schema.sql=unexpected\n", "sqlite")]
	[InlineData("[sqlite]\n./$(missing).sql\n", "sqlite")]
	public void Load_InvalidMigration_FailsWithFileAndSectionContext(string ini, string section)
	{
		using var directory = new MigrationTestDirectory();
		var migration = directory.Write("db.migration", ini);
		directory.Write("sqlite.ini", "[sqlite]\nDatabase=/var/lib/zongsoft/test.db\n");
		directory.Write("schema.sql", "SELECT 1;");

		var error = Assert.Throws<InvalidDataException>(() => Loader().Load("db.migration", directory.Path, "zongsoft.daemon", "1.1.0"));

		Assert.Contains(migration + ":", error.Message);
		Assert.Contains("[" + section + "]", error.Message);
	}

	[Fact]
	public void Load_DuplicatePostgresAliases_RejectsAmbiguousTasks()
	{
		using var directory = new MigrationTestDirectory();
		var migration = directory.Write("db.migration", "[postgres]\n./schema.sql\n[postgresql]\n./schema.sql\n");
		directory.Write("postgres.ini", "[postgres]\nServer=localhost\nDatabase=hosting\nUserName=operator\nPassword=\n");
		directory.Write("schema.sql", "SELECT 1;");

		var error = Assert.Throws<InvalidDataException>(() => Loader().Load("db.migration", directory.Path, "zongsoft.web", "1.1.0"));

		Assert.Contains(migration, error.Message);
	}

	[Fact]
	public void Load_ParametersUndefinedSecret_ReportsNameAndLocationWithoutValue()
	{
		using var directory = new MigrationTestDirectory();
		var migration = directory.Write("db.migration", "[postgres]\n");
		var parameters = directory.Write("postgres.ini", "[postgres]\nServer=localhost\nDatabase=hosting\nUserName=operator\nPassword=$(missing)\n");

		var error = Assert.Throws<InvalidDataException>(() => Parameters(directory, migration));

		Assert.Contains("Password", error.Message);
		Assert.Contains(parameters + ":", error.Message);
	}

	[Theory]
	[InlineData("public|private")]
	[InlineData("loggable:true")]
	[InlineData("internal")]
	[InlineData("pulic")]
	public void Bucket_UnsupportedOrConflictingOptions_Fails(string options)
	{
		var error = Assert.Throws<InvalidDataException>(() => Bucket(options));
		Assert.Contains("attachments", error.Message);
	}

	[Theory]
	[InlineData("", false)]
	[InlineData("private", false)]
	[InlineData("PUBLIC", true)]
	public void Bucket_ValidOptions_UsesPrivateDefault(string options, bool isPublic)
	{
		var bucket = Bucket(options);
		Assert.Equal("attachments", bucket.Name);
		Assert.Equal(isPublic, bucket.Public);
	}

	[Fact]
	public void Load_ParametersMissingEveryCandidate_ReportsMigratorAndSearchPaths()
	{
		using var directory = new MigrationTestDirectory();
		var migration = directory.Write("nested/zongsoft-missing-" + Guid.NewGuid().ToString("N") + ".migration", "[tdengine]\n");

		var error = Assert.Throws<InvalidDataException>(() => Parameters(directory, migration));

		Assert.Contains(migration, error.Message);
		Assert.Contains("tdengine", error.Message);
		Assert.Contains(Path.Combine(directory.Path, "nested", "tdengine.ini"), error.Message);
		Assert.Contains(Path.Combine(directory.Path, "tdengine.ini"), error.Message);
	}

	[Fact]
	public void Load_ProviderArtifacts_ShareCounterAcrossFilesAliasesAndTargets()
	{
		using var directory = new MigrationTestDirectory();
		directory.Write("first.migration", "[POSTGRESQL]\n./first*.sql\n./first.sql\n");
		directory.Write("first.ini", "[postgres]\nServer=localhost\nDatabase=first\nUserName=operator\nPassword=\n");
		directory.Write("first.sql", "SELECT 'first';");
		directory.Write("parts/20-postgres.migration", "[postgres]\n./second.sql\n");
		directory.Write("parts/20-postgres.ini", "[postgres]\nServer=localhost\nDatabase=second\nUserName=operator\nPassword=\n");
		directory.Write("parts/second.sql", "SELECT 'second';");
		directory.Write("parts/10-mysql.migration", "[mysql]\n./mysql.sql\n");
		directory.Write("mysql.ini", "[mysql]\nServer=localhost\nDatabase=hosting\nUserName=operator\nPassword=\n");
		directory.Write("parts/mysql.sql", "SELECT 'mysql';");

		var plan = Loader().Load("first.migration;parts/*.migration|first.migration", directory.Path, "zongsoft.web", "1.1.0");

		Assert.Equal(new[] { "postgres", "mysql", "postgres", "postgres" }, plan.Steps.Select(task => task.Provider));
		Assert.Equal(new[] { "first", "hosting", "second", "first" }, plan.Steps.Select(task => plan.Databases[task.DatabaseIndex.Value].Name));
		Assert.Equal(4, plan.Steps.Count);
		var scripts = plan.Steps.Select(task => Assert.Single(task.Scripts)).ToArray();
		Assert.Equal(new[] { ".migration/.artifacts/postgres/1.sql", ".migration/.artifacts/mysql/1.sql", ".migration/.artifacts/postgres/2.sql", ".migration/.artifacts/postgres/3.sql" }, scripts.Select(script => script.Path));
		Assert.Equal(new[] { "SELECT 'first';", "SELECT 'mysql';", "SELECT 'second';", "SELECT 'first';" }, scripts.Select(script => script.Content));
		Assert.All(scripts, script => Assert.Equal(Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(script.Content))), script.Checksum));
		Assert.Equal(scripts[0].Source, scripts[3].Source);
		Assert.Equal(scripts[0].Checksum, scripts[3].Checksum);
	}

	[Fact]
	public void Load_ReusedLoader_RestartsProviderCounters()
	{
		using var directory = new MigrationTestDirectory();
		directory.Write("db.migration", "[sqlite]\n./schema.sql\n");
		directory.Write("sqlite.ini", "[sqlite]\nDatabase=/var/lib/zongsoft/hosting.db\n");
		directory.Write("schema.sql", "SELECT 1;");
		var loader = Loader();

		var first = loader.Load("db.migration;db.migration", directory.Path, "zongsoft.daemon", "1.1.0");
		var second = loader.Load("db.migration;db.migration", directory.Path, "zongsoft.daemon", "1.1.0");

		Assert.Equal(new[] { ".migration/.artifacts/sqlite/1.sql", ".migration/.artifacts/sqlite/2.sql" }, second.Steps.SelectMany(task => task.Scripts).Select(script => script.Path));
		Assert.Equal(first.Serialize(), second.Serialize());
		Assert.Equal(first.Fingerprint(), second.Fingerprint());
	}

	[Fact]
	public void Load_SameSqlAcrossSeparateInis_RemainsTwoIndependentTasks()
	{
		using var directory = new MigrationTestDirectory();
		directory.Write("one.migration", "[sqlite]\n./schema.sql\n");
		directory.Write("two.migration", "[sqlite]\n./schema.sql\n");
		directory.Write("sqlite.ini", "[sqlite]\nDatabase=/var/lib/zongsoft/hosting.db\n");
		directory.Write("schema.sql", "SELECT 1;");

		var plan = Loader().Load("one.migration;two.migration", directory.Path, "zongsoft.daemon", "1.1.0");

		Assert.All(plan.Steps, task => Assert.Single(task.Scripts));
		Assert.NotEqual(plan.Steps[0].Scripts[0].Path, plan.Steps[1].Scripts[0].Path);
		Assert.Equal(plan.Steps[0].Scripts[0].Checksum, plan.Steps[1].Scripts[0].Checksum);
	}

	[Theory]
	[InlineData("MsSql", "mssql")]
	[InlineData("MySQL", "mysql")]
	[InlineData("Sqlite", "sqlite")]
	[InlineData("DuckDB", "duckdb")]
	[InlineData("PostgreSQL", "postgres")]
	[InlineData("POSTGRES", "postgres")]
	[InlineData("TDengine", "tdengine")]
	[InlineData("Amazon.S3", "amazon.s3")]
	public void Provider_SupportedNamesAndAliases_AreCaseInsensitive(string name, string expected)
	{
		Assert.Equal(expected, MigrationProvider.Get(name).Name);
	}

	[Theory]
	[InlineData("0")]
	[InlineData("-1")]
	[InlineData("86401s")]
	[InlineData("1441m")]
	[InlineData("1h")]
	public void Parameters_InvalidTimeout_FailsWithoutEchoingOtherValues(string timeout)
	{
		var parameters = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["Database"] = "/var/lib/zongsoft/hosting.db", ["CommandTimeout"] = timeout };
		var error = Assert.Throws<InvalidDataException>(() => MigrationProvider.Get("sqlite").Validate(parameters));
		Assert.Contains("CommandTimeout", error.Message);
	}

	[Fact]
	public void Profile_CommentsBareSqlAndParameterPunctuation_UseCoreParsingSemantics()
	{
		using var directory = new MigrationTestDirectory();
		directory.Write("db.migration", "; SQL comment\n# another comment\n[PoStGrEs]\n./schema.sql\n");
		directory.Write("postgres.ini", "# config comment\n[postgres]\nServer=localhost\nDatabase=hosting\nUserName=operator\nPassword=value=with;semi#hash\n");
		var sql = directory.Write("schema.sql", "SELECT 1;");

		var plan = Loader().Load("db.migration", directory.Path, "zongsoft.web", "1.1.0");

		Assert.Single(plan.Steps);
		Assert.Equal(sql, Assert.Single(plan.Steps[0].Scripts).Source);
		Assert.Equal("value=with;semi#hash", plan.Databases[0].Settings["password"]);
		Assert.Empty(plan.Steps[0].Settings);
		Assert.Equal("operator", plan.Databases[0].Settings["UserName"]);
	}

	[Fact]
	public void Profile_MixedCaseS3SharedParameterSection_ResolvesDottedProvider()
	{
		using var directory = new MigrationTestDirectory();
		directory.Write("fs.migration", "[aMaZoN.S3]\nattachments=private\n");
		directory.Write("fs.ini", "[AMAZON.s3]\nServer=http://localhost:9000\nRegion=us-east-1\nAccessKey=test\nSecretKey=secret=with;punctuation\n");

		var task = Assert.Single(Loader().Load("fs.migration", directory.Path, "zongsoft.web", "1.1.0").Steps);

		Assert.Equal("amazon.s3", task.Provider);
		Assert.Equal("secret=with;punctuation", task.Settings["SecretKey"]);
		Assert.Equal("attachments", Assert.Single(task.Buckets).Name);
	}

	[Theory]
	[InlineData("[sqlite]\nDatabase=hosting\nDATABASE=another")]
	[InlineData("Password=first-secret\npassword=second-secret")]
	public void Profile_CaseInsensitiveDuplicateParameterKey_FailsWithoutLeakingValues(string duplicate)
	{
		using var directory = new MigrationTestDirectory();
		var migration = directory.Write("db.migration", "[postgres]\n./schema.sql\n");
		var parameters = directory.Write("postgres.ini", "[postgres]\nServer=localhost\nUserName=operator\nPassword=\n" + duplicate + "\n");

		var error = Assert.Throws<InvalidDataException>(() => Parameters(directory, migration));

		Assert.Contains(parameters, error.Message);
		Assert.DoesNotContain("first-secret", error.Message);
		Assert.DoesNotContain("second-secret", error.Message);
	}

	[Fact]
	public void Profile_CaseInsensitiveDuplicateSqlEntry_IsRejectedByCoreParser()
	{
		using var directory = new MigrationTestDirectory();
		var migration = directory.Write("db.migration", "[sqlite]\n./schema.sql\n./SCHEMA.SQL\n");
		directory.Write("sqlite.ini", "[sqlite]\nDatabase=/var/lib/zongsoft/hosting.db\n");
		directory.Write("schema.sql", "SELECT 1;");

		var error = Assert.Throws<InvalidDataException>(() => Loader().Load("db.migration", directory.Path, "zongsoft.web", "1.1.0"));

		Assert.Contains(migration, error.Message);
	}

	[Theory]
	[InlineData("postgres", "DO $body$ BEGIN PERFORM '$(literal);'; END $body$;\nSELECT E'it\\'s;ready';")]
	[InlineData("mysql", "SET @value=1;\nDELIMITER $$\nCREATE PROCEDURE p() BEGIN SELECT @value; END$$\nDELIMITER ;\nCALL p();")]
	public void Load_ComplexDatabaseSql_PreservesSourceAndPreparesExecutableContent(string provider, string sql)
	{
		using var directory = new MigrationTestDirectory();
		directory.Write("db.migration", "[" + provider + "]\n./schema.sql\n");
		directory.Write(provider + ".ini", "[" + provider + "]\nServer=localhost\nDatabase=hosting\nUserName=operator\nPassword=\n");
		var source = directory.Write("schema.sql", sql);

		var plan = Loader().Load("db.migration", directory.Path, "zongsoft.web", "1.1.0");

		var script = Assert.Single(Assert.Single(plan.Steps).Scripts);
		Assert.Equal(source, script.Source);
		Assert.Equal(sql.ReplaceLineEndings("\r\n"), File.ReadAllText(script.Source));
		Assert.Equal(Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(script.Content))), script.Checksum);
		if(provider == "mysql")
		{
			Assert.DoesNotContain("DELIMITER", script.Content);
			Assert.Contains("CREATE PROCEDURE p() BEGIN SELECT @value; END;", script.Content);
		}
		else
			Assert.Equal(File.ReadAllText(source), script.Content);
	}

	[Theory]
	[InlineData("mssql")]
	[InlineData("mysql")]
	[InlineData("postgres")]
	[InlineData("sqlite")]
	[InlineData("duckdb")]
	[InlineData("tdengine")]
	public void Load_IncompleteSql_LeavesSyntaxValidationToInstallationDatabase(string provider)
	{
		using var directory = new MigrationTestDirectory();
		directory.Write("db.migration", "[" + provider + "]\n./schema.sql\n");
		directory.Write(provider + ".ini", provider is "sqlite" or "duckdb" ? "[" + provider + "]\nDatabase=/var/lib/zongsoft/hosting.db\n" : "[" + provider + "]\nServer=localhost\nDatabase=hosting\nUserName=operator\nPassword=\n");
		var source = directory.Write("schema.sql", "SELECT 'unfinished;");

		var task = Assert.Single(Loader().Load("db.migration", directory.Path, "zongsoft.daemon", "1.1.0").Steps);

		Assert.Equal(provider, task.Provider);
		Assert.Equal(source, Assert.Single(task.Scripts).Source);
		Assert.Equal("SELECT 'unfinished;", File.ReadAllText(task.Scripts[0].Source));
	}

	[Fact]
	public void Load_SqlServerBatches_SortsFilesRemovesBomAndHashesPreparedUtf8Content()
	{
		using var directory = new MigrationTestDirectory();
		directory.Write("db.migration", "[mssql]\n./sql/*.sql\n");
		directory.Write("mssql.ini", "[mssql]\nServer=localhost\nDatabase=hosting\nUserName=operator\nPassword=\n");
		var later = directory.Write("sql/020-seed.sql", "SELECT 3;\r\nGO\r\n");
		var first = directory.Write("sql/010-schema.sql", "");
		File.WriteAllText(first, "SELECT N'附件';\r\n-- retained\r\nGO -- client separator\r\nSELECT 2;\r\n", new UTF8Encoding(true));

		var plan = Loader().Load("db.migration", directory.Path, "zongsoft.daemon", "1.1.0");

		var step = Assert.Single(plan.Steps);
		Assert.Equal(new[] { "SELECT N'附件';\r\n-- retained", "SELECT 2;", "SELECT 3;" }, step.Scripts.Select(script => script.Content));
		Assert.Equal(new[] { first, first, later }, step.Scripts.Select(script => script.Source));
		Assert.Equal(new[] { ".migration/.artifacts/mssql/1.sql", ".migration/.artifacts/mssql/2.sql", ".migration/.artifacts/mssql/3.sql" }, step.Scripts.Select(script => script.Path));
		foreach(var script in step.Scripts)
		{
			Assert.Equal(Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(script.Content))), script.Checksum);
			Assert.DoesNotContain('\uFEFF', script.Content);
		}
		Assert.DoesNotContain("Source", plan.Serialize());
		Assert.DoesNotContain("Content", plan.Serialize());
	}
	[Fact]
	public void Migration_RecursiveGlob_PreservesSqlOrderAndIndependentTasks()
	{
		using var directory = new MigrationTestDirectory();
		directory.Write("sqlite.ini", "[sqlite]\nDatabase=/tmp/zongsoft-artifact-hosting.db\n");
		directory.Write("first.migration", "[sqlite]\nsql/z.sql\nsql/**/0?.sql\n");
		directory.Write("parts/b/db.migration", "[sqlite]\n../../sql/z.sql\n");
		directory.Write("parts/a/db.migration", "[sqlite]\n../../sql/A/01.sql\n");
		directory.Write("sql/z.sql", "SELECT 'explicit';");
		directory.Write("sql/B/deep/02.sql", "SELECT 'nested';");
		directory.Write("sql/A/01.sql", "SELECT 'first';");

		var plan = new MigrationLoader(null).Load("first.migration;parts/*/db.migration;first.migration", directory.Path, "zongsoft.daemon", "1.0.0");

		Assert.Equal(4, plan.Steps.Count);
		Assert.Equal(new[] { "SELECT 'explicit';", "SELECT 'first';", "SELECT 'nested';" }, plan.Steps[0].Scripts.Select(script => script.Content));
		Assert.Equal("SELECT 'first';", Assert.Single(plan.Steps[1].Scripts).Content);
		Assert.Equal("SELECT 'explicit';", Assert.Single(plan.Steps[2].Scripts).Content);
		Assert.Equal(plan.Steps[0].Scripts.Select(script => script.Content), plan.Steps[3].Scripts.Select(script => script.Content));
		var scripts = plan.Steps.SelectMany(task => task.Scripts).ToArray();
		Assert.Equal(Enumerable.Range(1, 8).Select(index => $".migration/.artifacts/sqlite/{index}.sql"), scripts.Select(script => script.Path));
		Assert.Equal(4, plan.Steps.Count);
		Assert.All(scripts, script => Assert.Equal(Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(script.Content))), script.Checksum));
	}
	#endregion

	#region 辅助方法
	private static Dictionary<string, string> Parameters(MigrationTestDirectory directory, string migration)
	{
		var content = File.ReadAllText(migration);
		if(!content.Contains(".sql", StringComparison.Ordinal))
			File.AppendAllText(migration, "./schema.sql\n");
		directory.Write(Path.GetRelativePath(directory.Path, Path.Combine(Path.GetDirectoryName(migration), "schema.sql")), "SELECT 1;");
		return Assert.Single(Loader().Load(migration, directory.Path, "zongsoft.daemon", "1.1.0").Databases).Settings;
	}

	private static MigrationPlan.Bucket Bucket(string options)
	{
		using var directory = new MigrationTestDirectory();
		directory.Write("fs.migration", "[amazon.s3]\nattachments=" + options + "\n");
		directory.Write("amazon.s3.ini", "Server=http://localhost:9000\nRegion=us-east-1\nAccessKey=test\nSecretKey=test\n");
		return Assert.Single(Assert.Single(Loader().Load("fs.migration", directory.Path, "zongsoft.daemon", "1.1.0").Steps).Buckets);
	}

	private static MigrationLoader Loader(Dictionary<string, string> variables = null, Action<string> warning = null) => new(value =>
	{
		var result = Normalizer.Normalize(value, variables ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase));
		if(!result.Succeed)
			throw new InvalidDataException("Undefined variable: " + result.Value);
		return result.Value;
	}, warning);
	#endregion
}

internal sealed class MigrationTestDirectory : IDisposable
{
	#region 构造函数
	public MigrationTestDirectory()
	{
		this.Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "ZongsoftMigrationTests", Guid.NewGuid().ToString("N"));
		Directory.CreateDirectory(this.Path);
	}
	#endregion

	#region 属性定义
	public string Path { get; }
	public static string CurrentRuntime => (OperatingSystem.IsWindows() ? "win" : "linux") + "-" + System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture.ToString().ToLowerInvariant();
	#endregion

	#region 辅助方法
	public static string GetRuntimeDirectory()
	{
		var root = new DirectoryInfo(AppContext.BaseDirectory);
		while(root != null && !File.Exists(System.IO.Path.Combine(root.FullName, "Zongsoft.Tools.Migrator.slnx")))
			root = root.Parent;
		Assert.NotNull(root);
		var configuration = new DirectoryInfo(AppContext.BaseDirectory.TrimEnd(System.IO.Path.DirectorySeparatorChar)).Parent.Name;
		var runtime = System.IO.Path.Combine(root.FullName, "executor", "src", "bin", configuration, "net10.0");
		Assert.True(File.Exists(System.IO.Path.Combine(runtime, "Zongsoft.Tools.Migrator.Executor.deps.json")), runtime);
		return runtime;
	}

	public string CreateRuntime()
	{
		foreach(var runtime in new[] { "linux-x64", "linux-arm64" })
		{
			var header = new byte[64];
			header[0] = 0x7f;
			header[1] = (byte)'E';
			header[2] = (byte)'L';
			header[3] = (byte)'F';
			header[4] = 2;
			header[5] = 1;
			header[6] = 1;
			header[16] = 3;
			header[18] = runtime == "linux-x64" ? (byte)62 : (byte)183;
			header[20] = 1;
			header[52] = 64;
			var executable = this.Write("runtime/" + runtime + "/Zongsoft.Tools.Migrator.Executor", "");
			File.WriteAllBytes(executable, header);
			this.Write("runtime/" + runtime + "/libe_sqlite3.so", "sqlite-" + runtime);
			this.Write("runtime/" + runtime + "/libduckdb.so", "duckdb-" + runtime);
			this.Write("runtime/" + runtime + "/assets/manifest.txt", runtime);
		}
		var windows = new byte[128];
		windows[0] = (byte)'M';
		windows[1] = (byte)'Z';
		windows[60] = 64;
		windows[64] = (byte)'P';
		windows[65] = (byte)'E';
		windows[68] = 0x64;
		windows[69] = 0x86;
		File.WriteAllBytes(this.Write("runtime/win-x64/Zongsoft.Tools.Migrator.Executor.exe", ""), windows);
		this.Write("runtime/win-x64/e_sqlite3.dll", "sqlite-win-x64");
		return System.IO.Path.Combine(this.Path, "runtime");
	}

	public string Write(string relative, string content)
	{
		var path = System.IO.Path.GetFullPath(System.IO.Path.Combine(this.Path, relative));
		if(!path.StartsWith(this.Path + System.IO.Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
			throw new InvalidOperationException("Fixture path escapes its owned directory.");
		Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path));
		File.WriteAllText(path, content.Replace("\r\n", "\n").Replace("\n", "\r\n"), new UTF8Encoding(false));
		return path;
	}

	public MigrationPlan.Script Script(string relative, string sql)
	{
		var path = this.Write(relative, sql);
		return new() { Path = relative, Source = path, Content = File.ReadAllText(path), Checksum = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))) };
	}

	public void Dispose()
	{
		if(Directory.Exists(this.Path))
			Directory.Delete(this.Path, true);
	}
	#endregion
}
