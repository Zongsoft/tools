using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Collections.Generic;
using System.Security.Cryptography;

using Zongsoft.Tools.Packager.Migration;

using Xunit;

namespace Zongsoft.Tools.Packager.Tests;

public sealed class MigrationLoaderTests
{
	#region 测试方法
	[Theory]
	[InlineData(";")]
	[InlineData("|")]
	public void Load_MultipleFilesVariablesAndGlobs_PreservesOrderAndLiteralSql(string separator)
	{
		using var directory = new MigrationTestDirectory();
		directory.Write("migration/sql/020-postgres.sql", "SELECT '$(literal);%unchanged%';");
		directory.Write("migration/sql/010-postgres.sql", "SELECT 1;");
		directory.Write("migration/db-production.ini", "[POSTGRESQL]\n./sql/*-postgres.sql\n./sql/010-postgres.sql\n");
		directory.Write("migration/postgres.env", "Server=localhost\nDatabase=hosting\nUserName=operator\nPassword=$(secret)\n");
		directory.Write("migration/fs.ini", "[AMAZON.S3]\nattachments=Private\ndownloads=PUBLIC\n");
		directory.Write("migration/amazon.s3.env", "Server=http://localhost:9000\nRegion=us-east-1\nAccessKey=test\nSecretKey=test\n");
		var loader = Loader(new() { ["environment"] = "production", ["secret"] = "value=with;punctuation\n[new]" });

		var plan = loader.Load($"migration/db-$(environment).ini{separator}migration/fs.ini", directory.Path, "zongsoft.web", "1.1.0");

		Assert.Equal(new[] { "postgres", "amazon.s3" }, plan.Tasks.Select(task => task.Provider));
		Assert.Equal(new[] { "010-postgres.sql", "020-postgres.sql" }, plan.Tasks[0].Scripts.Select(script => Path.GetFileName(script.Source)));
		Assert.Equal("value=with;punctuation\n[new]", plan.Tasks[0].Parameters["Password"]);
		Assert.Equal("SELECT '$(literal);%unchanged%';", File.ReadAllText(plan.Tasks[0].Scripts[1].Source));
		Assert.Equal(new[] { "attachments", "downloads" }, plan.Tasks[1].Buckets.Select(bucket => bucket.Name));
		Assert.False(plan.Tasks[1].Buckets[0].Public);
		Assert.True(plan.Tasks[1].Buckets[1].Public);
		Assert.Equal("zongsoft.web", plan.Package);
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
		directory.Write("hosting/.deploy/default/migration/1.1.0/zongsoft.db-production.ini", "[postgresql]\n./sql/schema.sql\n");
		directory.Write("hosting/.deploy/default/migration/1.1.0/Zongsoft.fs-production.ini", "[amazon.s3]\nattachments=private\n");
		directory.Write("hosting/.deploy/default/migration/1.1.0/zongsoft.db-development.ini", "[must-not-be-selected]\ninvalid\n");
		var sql = directory.Write("hosting/.deploy/default/migration/1.1.0/sql/schema.sql", "SELECT 'hosting';");
		directory.Write("hosting/.deploy/default/migration/postgres.env", "Server=ancestor-db\nDatabase=hosting\nUserName=operator\n");
		directory.Write("hosting/.deploy/default/migration/amazon.s3.env", "Server=http://ancestor-fs:9000\nRegion=us-east-1\nAccessKey=test\nSecretKey=test\n");
		var loader = Loader(new() { ["scheme"] = "default", ["version"] = "1.1.0", ["environment"] = "production" });

		var plan = loader.Load("../../.deploy/$(scheme)/migration/$(version)/*-$(environment).ini" + separator, source, "zongsoft.web", "1.1.0");

		Assert.Equal(new[] { "amazon.s3", "postgres" }, plan.Tasks.Select(task => task.Provider));
		Assert.Equal(new[] { "0001-amazon.s3", "0002-postgres" }, plan.Tasks.Select(task => task.Id));
		Assert.Equal("attachments", Assert.Single(plan.Tasks[0].Buckets).Name);
		Assert.False(plan.Tasks[0].Buckets[0].Public);
		Assert.Equal("http://ancestor-fs:9000", plan.Tasks[0].Parameters["Server"]);
		Assert.Equal("ancestor-db", plan.Tasks[1].Parameters["Server"]);
		Assert.Equal(sql, Assert.Single(plan.Tasks[1].Scripts).Source);
		Assert.Equal("SELECT 'hosting';", plan.Tasks[1].Scripts[0].Content);
	}

	[Fact]
	public void Load_MigrationIniGlob_RespectsArgumentOrderAndRetainsRepeatedExplicitFile()
	{
		using var directory = new MigrationTestDirectory();
		directory.Write("migration/01-db.ini", "[sqlite]\n./schema.sql\n");
		directory.Write("migration/02-fs.ini", "[amazon.s3]\nattachments=private\n");
		directory.Write("migration/sqlite.env", "Database=/var/lib/zongsoft/hosting.db\n");
		directory.Write("migration/amazon.s3.env", "Server=http://localhost:9000\nRegion=us-east-1\nAccessKey=test\nSecretKey=test\n");
		directory.Write("migration/schema.sql", "SELECT 1;");

		var plan = Loader().Load("migration/02-fs.ini;migration/*.ini|migration/02-fs.ini;", directory.Path, "zongsoft.daemon", "1.1.0");

		Assert.Equal(new[] { "amazon.s3", "sqlite", "amazon.s3", "amazon.s3" }, plan.Tasks.Select(task => task.Provider));
		Assert.Equal(new[] { "0001-amazon.s3", "0002-sqlite", "0003-amazon.s3", "0004-amazon.s3" }, plan.Tasks.Select(task => task.Id));
		Assert.Equal("attachments", Assert.Single(plan.Tasks[0].Buckets).Name);
		Assert.Equal("attachments", Assert.Single(plan.Tasks[2].Buckets).Name);
		Assert.Equal("attachments", Assert.Single(plan.Tasks[3].Buckets).Name);
	}

	[Theory]
	[InlineData(false)]
	[InlineData(true)]
	public void Load_MigrationIniGlob_WithoutMatchesReportsRequestedPattern(bool directoryExists)
	{
		using var directory = new MigrationTestDirectory();
		if(directoryExists) directory.Write("migration/readme.txt", "No migration INI exists.");
		var pattern = Path.Combine(directory.Path, "migration", "*.ini");

		var warnings = new List<string>();

		var plan = Loader(warning: warnings.Add).Load("migration/*.ini", directory.Path, "zongsoft.daemon", "1.1.0");

		Assert.Null(plan);
		Assert.Contains(pattern, Assert.Single(warnings));
	}

	[Fact]
	public void Load_MissingFilesAmongExistingFiles_WarnsAndPreservesValidTasksInOrder()
	{
		using var directory = new MigrationTestDirectory();
		directory.Write("db.ini", "[sqlite]\n./schema.sql\n");
		directory.Write("sqlite.env", "Database=/var/lib/zongsoft/hosting.db\n");
		directory.Write("schema.sql", "SELECT 1;");
		directory.Write("fs.ini", "[amazon.s3]\nattachments=private\n");
		directory.Write("amazon.s3.env", "Server=http://localhost:9000\nRegion=us-east-1\nAccessKey=test\nSecretKey=test\n");
		var warnings = new List<string>();
		var loader = Loader(new() { ["environment"] = "production" }, warnings.Add);

		var plan = loader.Load("missing-$(environment).ini;db.ini|absent/*.ini;fs.ini|", directory.Path, "zongsoft.daemon", "1.1.0");

		Assert.Equal(new[] { "sqlite", "amazon.s3" }, plan.Tasks.Select(task => task.Provider));
		Assert.Equal(new[] { "0001-sqlite", "0002-amazon.s3" }, plan.Tasks.Select(task => task.Id));
		Assert.Equal("SELECT 1;", Assert.Single(plan.Tasks[0].Scripts).Content);
		Assert.Equal("attachments", Assert.Single(plan.Tasks[1].Buckets).Name);
		Assert.Equal(2, warnings.Count);
		Assert.Contains(Path.Combine(directory.Path, "missing-production.ini"), warnings[0]);
		Assert.Contains(Path.Combine(directory.Path, "absent", "*.ini"), warnings[1]);
	}

	[Fact]
	public void Load_AllRequestedFilesMissing_ReturnsNoPlanAndWarnsForEveryExpandedPath()
	{
		using var directory = new MigrationTestDirectory();
		var warnings = new List<string>();
		var loader = Loader(new() { ["environment"] = "production" }, warnings.Add);

		var plan = loader.Load("missing-$(environment).ini;absent/*.ini|", directory.Path, "zongsoft.daemon", "1.1.0");

		Assert.Null(plan);
		Assert.Equal(2, warnings.Count);
		Assert.Contains(Path.Combine(directory.Path, "missing-production.ini"), warnings[0]);
		Assert.Contains(Path.Combine(directory.Path, "absent", "*.ini"), warnings[1]);
	}

	[Theory]
	[InlineData("db.ini", "")]
	[InlineData("db.ini", "[sqlite]\n")]
	[InlineData("db.txt", "[sqlite]\n./schema.sql\n")]
	[InlineData("db.ini", "[unknown]\ninvalid\n")]
	public void Load_ExistingInvalidInputAfterMissingFile_StillFails(string name, string content)
	{
		using var directory = new MigrationTestDirectory();
		directory.Write(name, content);
		directory.Write("sqlite.env", "Database=/var/lib/zongsoft/hosting.db\n");
		directory.Write("schema.sql", "SELECT 1;");
		var warnings = new List<string>();

		Assert.Throws<InvalidDataException>(() => Loader(warning: warnings.Add).Load("missing.ini;" + name, directory.Path, "zongsoft.daemon", "1.1.0"));

		Assert.Contains(Path.Combine(directory.Path, "missing.ini"), Assert.Single(warnings));
	}

	[Theory]
	[InlineData(false)]
	[InlineData(true)]
	public void Load_MissingParametersOrSqlAfterMissingIni_StillFails(bool parametersExist)
	{
		using var directory = new MigrationTestDirectory();
		var migration = directory.Write("db.ini", "[sqlite]\n./missing.sql\n");
		if(parametersExist) directory.Write("sqlite.env", "Database=/var/lib/zongsoft/hosting.db\n");
		var warnings = new List<string>();

		var error = Assert.Throws<InvalidDataException>(() => Loader(warning: warnings.Add).Load("missing.ini;db.ini", directory.Path, "zongsoft.daemon", "1.1.0"));

		Assert.Contains(migration, error.Message);
		Assert.Contains("sqlite", error.Message, StringComparison.OrdinalIgnoreCase);
		Assert.Contains(Path.Combine(directory.Path, "missing.ini"), Assert.Single(warnings));
	}

	[Theory]
	[InlineData("migration/*/*.ini")]
	[InlineData("migration/release?/*.ini")]
	public void Load_MigrationIniGlob_RejectsWildcardParentDirectory(string pattern)
	{
		using var directory = new MigrationTestDirectory();

		Assert.Throws<InvalidDataException>(() => Loader().Load(pattern, directory.Path, "zongsoft.daemon", "1.1.0"));
	}

	[Fact]
	public void Load_ParametersNearestProviderBeatsParentShared_AndMissingSectionContinues()
	{
		using var directory = new MigrationTestDirectory();
		var migration = directory.Write("nested/migration/db.ini", "[postgres]\n");
		directory.Write("nested/migration/db.env", "[mysql]\nServer=wrong\n");
		directory.Write("nested/migration/postgresql.env", "Server=nearest\nDatabase=hosting\nUserName=operator\n");
		directory.Write("nested/db.env", "[postgres]\nServer=parent\nDatabase=hosting\nUserName=operator\n");

		var parameters = Parameters(directory, migration);

		Assert.Equal("nearest", parameters["Server"]);
		Assert.Equal(3, parameters.Count);
	}

	[Fact]
	public void Load_ParametersSameNameSharedWins_OnlySelectedSectionIsCollected()
	{
		using var directory = new MigrationTestDirectory();
		var migration = directory.Write("db.ini", "[postgres]\n");
		directory.Write("db.env", "[POSTGRESQL]\nServer=shared\nDatabase=hosting\nUserName=operator\n[mysql]\nPassword=must-not-be-collected\n");
		directory.Write("postgres.env", "Server=provider\nDatabase=hosting\nUserName=operator\nPassword=ignored\n");

		var parameters = Parameters(directory, migration);

		Assert.Equal("shared", parameters["server"]);
		Assert.False(parameters.ContainsKey("Password"));
		Assert.Equal(3, parameters.Count);
	}

	[Fact]
	public void Load_ParametersSelectedIncompleteFile_DoesNotMergeOrFallback()
	{
		using var directory = new MigrationTestDirectory();
		var migration = directory.Write("nested/db.ini", "[postgres]\n");
		var selected = directory.Write("nested/postgres.env", "Server=nearest\nUserName=operator\n");
		directory.Write("postgres.env", "Server=parent\nDatabase=hosting\nUserName=operator\n");

		var error = Assert.Throws<InvalidDataException>(() => Parameters(directory, migration));

		Assert.Contains(selected, error.Message);
		Assert.Contains("Database", error.Message);
	}

	[Fact]
	public void Load_ParametersProviderSection_DoesNotMergeRootEntries()
	{
		using var directory = new MigrationTestDirectory();
		var migration = directory.Write("db.ini", "[postgres]\n");
		directory.Write("postgres.env", "Database=root-only\n[postgres]\nServer=localhost\nUserName=operator\n");

		var error = Assert.Throws<InvalidDataException>(() => Parameters(directory, migration));

		Assert.Contains("Database", error.Message);
	}

	[Fact]
	public void Load_ParametersParentAliasFile_IsFoundWithoutOtherSections()
	{
		using var directory = new MigrationTestDirectory();
		var migration = directory.Write("deep/child/db.ini", "[postgres]\n");
		directory.Write("postgresql.env", "[POSTGRES]\nServer=parent\nDatabase=hosting\nUserName=operator\n[mysql]\nPassword=other\n");

		var parameters = Parameters(directory, migration);

		Assert.Equal("parent", parameters["Server"]);
		Assert.False(parameters.ContainsKey("Password"));
	}

	[Fact]
	public void Load_ParametersDuplicatePostgresAliases_FailsWithFileContext()
	{
		using var directory = new MigrationTestDirectory();
		var migration = directory.Write("db.ini", "[postgres]\n");
		var parameters = directory.Write("db.env", "[postgres]\nDatabase=one\n[postgresql]\nDatabase=two\n");

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
		var migration = directory.Write("db.ini", ini);
		directory.Write("sqlite.env", "Database=/var/lib/zongsoft/test.db\n");
		directory.Write("schema.sql", "SELECT 1;");

		var error = Assert.Throws<InvalidDataException>(() => Loader().Load("db.ini", directory.Path, "zongsoft.daemon", "1.1.0"));

		Assert.Contains(migration + ":", error.Message);
		Assert.Contains("[" + section + "]", error.Message);
	}

	[Fact]
	public void Load_DuplicatePostgresAliases_RejectsAmbiguousTasks()
	{
		using var directory = new MigrationTestDirectory();
		var migration = directory.Write("db.ini", "[postgres]\n./schema.sql\n[postgresql]\n./schema.sql\n");
		directory.Write("postgres.env", "Server=localhost\nDatabase=hosting\nUserName=operator\n");
		directory.Write("schema.sql", "SELECT 1;");

		var error = Assert.Throws<InvalidDataException>(() => Loader().Load("db.ini", directory.Path, "zongsoft.web", "1.1.0"));

		Assert.Contains(migration, error.Message);
	}

	[Fact]
	public void Load_ParametersUndefinedSecret_ReportsNameAndLocationWithoutValue()
	{
		using var directory = new MigrationTestDirectory();
		var migration = directory.Write("db.ini", "[postgres]\n");
		var parameters = directory.Write("postgres.env", "Server=localhost\nDatabase=hosting\nUserName=operator\nPassword=$(missing)\n");

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
		var migration = directory.Write("nested/zongsoft-missing-" + Guid.NewGuid().ToString("N") + ".ini", "[tdengine]\n");

		var error = Assert.Throws<InvalidDataException>(() => Parameters(directory, migration));

		Assert.Contains(migration, error.Message);
		Assert.Contains("tdengine", error.Message);
		Assert.Contains(Path.Combine(directory.Path, "nested", "tdengine.env"), error.Message);
		Assert.Contains(Path.Combine(directory.Path, "tdengine.env"), error.Message);
	}

	[Fact]
	public void Load_ProviderArtifacts_ShareCounterAcrossFilesAliasesAndTargets()
	{
		using var directory = new MigrationTestDirectory();
		directory.Write("first.ini", "[POSTGRESQL]\n./first*.sql\n./first.sql\n");
		directory.Write("first.env", "[postgres]\nServer=localhost\nDatabase=first\nUserName=operator\n");
		directory.Write("first.sql", "SELECT 'first';");
		directory.Write("parts/20-postgres.ini", "[postgres]\n./second.sql\n");
		directory.Write("parts/20-postgres.env", "[postgres]\nServer=localhost\nDatabase=second\nUserName=operator\n");
		directory.Write("parts/second.sql", "SELECT 'second';");
		directory.Write("parts/10-mysql.ini", "[mysql]\n./mysql.sql\n");
		directory.Write("mysql.env", "Server=localhost\nDatabase=hosting\nUserName=operator\n");
		directory.Write("parts/mysql.sql", "SELECT 'mysql';");

		var plan = Loader().Load("first.ini;parts/*.ini|first.ini", directory.Path, "zongsoft.web", "1.1.0");

		Assert.Equal(new[] { "postgres", "mysql", "postgres", "postgres" }, plan.Tasks.Select(task => task.Provider));
		Assert.Equal(new[] { "first", "hosting", "second", "first" }, plan.Tasks.Select(task => task.Parameters["Database"]));
		Assert.Equal(4, plan.Tasks.Select(task => task.Id).Distinct().Count());
		var scripts = plan.Tasks.Select(task => Assert.Single(task.Scripts)).ToArray();
		Assert.Equal(new[] { ".migration/.artifacts/postgres/0001.sql", ".migration/.artifacts/mysql/0001.sql", ".migration/.artifacts/postgres/0002.sql", ".migration/.artifacts/postgres/0003.sql" }, scripts.Select(script => script.Path));
		Assert.Equal(new[] { "SELECT 'first';", "SELECT 'mysql';", "SELECT 'second';", "SELECT 'first';" }, scripts.Select(script => script.Content));
		Assert.All(scripts, script => Assert.Equal(Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(script.Content))), script.Checksum));
		Assert.Equal(scripts[0].Source, scripts[3].Source);
		Assert.Equal(scripts[0].Checksum, scripts[3].Checksum);
	}

	[Fact]
	public void Load_ReusedLoader_RestartsProviderCounters()
	{
		using var directory = new MigrationTestDirectory();
		directory.Write("db.ini", "[sqlite]\n./schema.sql\n");
		directory.Write("sqlite.env", "Database=/var/lib/zongsoft/hosting.db\n");
		directory.Write("schema.sql", "SELECT 1;");
		var loader = Loader();

		var first = loader.Load("db.ini;db.ini", directory.Path, "zongsoft.daemon", "1.1.0");
		var second = loader.Load("db.ini;db.ini", directory.Path, "zongsoft.daemon", "1.1.0");

		Assert.Equal(new[] { ".migration/.artifacts/sqlite/0001.sql", ".migration/.artifacts/sqlite/0002.sql" }, second.Tasks.SelectMany(task => task.Scripts).Select(script => script.Path));
		Assert.Equal(first.Serialize(), second.Serialize());
		Assert.Equal(first.Fingerprint(), second.Fingerprint());
	}

	[Fact]
	public void Load_SameSqlAcrossSeparateInis_RemainsTwoIndependentTasks()
	{
		using var directory = new MigrationTestDirectory();
		directory.Write("one.ini", "[sqlite]\n./schema.sql\n");
		directory.Write("two.ini", "[sqlite]\n./schema.sql\n");
		directory.Write("sqlite.env", "Database=/var/lib/zongsoft/hosting.db\n");
		directory.Write("schema.sql", "SELECT 1;");

		var plan = Loader().Load("one.ini;two.ini", directory.Path, "zongsoft.daemon", "1.1.0");

		Assert.Equal(new[] { "0001-sqlite", "0002-sqlite" }, plan.Tasks.Select(task => task.Id));
		Assert.All(plan.Tasks, task => Assert.Single(task.Scripts));
		Assert.NotEqual(plan.Tasks[0].Scripts[0].Path, plan.Tasks[1].Scripts[0].Path);
		Assert.Equal(plan.Tasks[0].Scripts[0].Checksum, plan.Tasks[1].Scripts[0].Checksum);
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
		directory.Write("db.ini", "; SQL comment\n# another comment\n[PoStGrEs]\n./schema.sql\n");
		directory.Write("postgres.env", "# config comment\nServer=localhost\nDatabase=hosting\nUserName=operator\nPassword=value=with;semi#hash\n");
		var sql = directory.Write("schema.sql", "SELECT 1;");

		var plan = Loader().Load("db.ini", directory.Path, "zongsoft.web", "1.1.0");

		Assert.Single(plan.Tasks);
		Assert.Equal(sql, Assert.Single(plan.Tasks[0].Scripts).Source);
		Assert.Equal("value=with;semi#hash", plan.Tasks[0].Parameters["password"]);
		Assert.Equal(4, plan.Tasks[0].Parameters.Count);
	}

	[Fact]
	public void Profile_MixedCaseS3SharedParameterSection_ResolvesDottedProvider()
	{
		using var directory = new MigrationTestDirectory();
		directory.Write("fs.ini", "[aMaZoN.S3]\nattachments=private\n");
		directory.Write("fs.env", "[AMAZON.s3]\nServer=http://localhost:9000\nRegion=us-east-1\nAccessKey=test\nSecretKey=secret=with;punctuation\n");

		var task = Assert.Single(Loader().Load("fs.ini", directory.Path, "zongsoft.web", "1.1.0").Tasks);

		Assert.Equal("amazon.s3", task.Provider);
		Assert.Equal("secret=with;punctuation", task.Parameters["SecretKey"]);
		Assert.Equal("attachments", Assert.Single(task.Buckets).Name);
	}

	[Theory]
	[InlineData("Database=hosting\nDATABASE=another")]
	[InlineData("Password=first-secret\npassword=second-secret")]
	public void Profile_CaseInsensitiveDuplicateParameterKey_FailsWithoutLeakingValues(string duplicate)
	{
		using var directory = new MigrationTestDirectory();
		var migration = directory.Write("db.ini", "[postgres]\n./schema.sql\n");
		var parameters = directory.Write("postgres.env", "Server=localhost\nUserName=operator\n" + duplicate + "\n");

		var error = Assert.Throws<InvalidDataException>(() => Parameters(directory, migration));

		Assert.Contains(parameters, error.Message);
		Assert.DoesNotContain("first-secret", error.Message);
		Assert.DoesNotContain("second-secret", error.Message);
	}

	[Fact]
	public void Profile_CaseInsensitiveDuplicateSqlEntry_IsRejectedByCoreParser()
	{
		using var directory = new MigrationTestDirectory();
		var migration = directory.Write("db.ini", "[sqlite]\n./schema.sql\n./SCHEMA.SQL\n");
		directory.Write("sqlite.env", "Database=/var/lib/zongsoft/hosting.db\n");
		directory.Write("schema.sql", "SELECT 1;");

		var error = Assert.Throws<InvalidDataException>(() => Loader().Load("db.ini", directory.Path, "zongsoft.web", "1.1.0"));

		Assert.Contains(migration, error.Message);
	}

	[Theory]
	[InlineData("postgres", "DO $body$ BEGIN PERFORM '$(literal);'; END $body$;\nSELECT E'it\\'s;ready';")]
	[InlineData("mysql", "SET @value=1;\nDELIMITER $$\nCREATE PROCEDURE p() BEGIN SELECT @value; END$$\nDELIMITER ;\nCALL p();")]
	public void Load_ComplexDatabaseSql_PreservesSourceAndPreparesExecutableContent(string provider, string sql)
	{
		using var directory = new MigrationTestDirectory();
		directory.Write("db.ini", "[" + provider + "]\n./schema.sql\n");
		directory.Write(provider + ".env", "Server=localhost\nDatabase=hosting\nUserName=operator\n");
		var source = directory.Write("schema.sql", sql);

		var plan = Loader().Load("db.ini", directory.Path, "zongsoft.web", "1.1.0");

		var script = Assert.Single(Assert.Single(plan.Tasks).Scripts);
		Assert.Equal(source, script.Source);
		Assert.Equal(sql.ReplaceLineEndings("\r\n"), File.ReadAllText(script.Source));
		Assert.Equal(Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(script.Content))), script.Checksum);
		if(provider == "mysql")
		{
			Assert.DoesNotContain("DELIMITER", script.Content);
			Assert.Contains("CREATE PROCEDURE p() BEGIN SELECT @value; END;", script.Content);
		}
		else Assert.Equal(File.ReadAllText(source), script.Content);
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
		directory.Write("db.ini", "[" + provider + "]\n./schema.sql\n");
		directory.Write(provider + ".env", provider is "sqlite" or "duckdb" ? "Database=/var/lib/zongsoft/hosting.db\n" : "Server=localhost\nDatabase=hosting\nUserName=operator\n");
		var source = directory.Write("schema.sql", "SELECT 'unfinished;");

		var task = Assert.Single(Loader().Load("db.ini", directory.Path, "zongsoft.daemon", "1.1.0").Tasks);

		Assert.Equal(provider, task.Provider);
		Assert.Equal(source, Assert.Single(task.Scripts).Source);
		Assert.Equal("SELECT 'unfinished;", File.ReadAllText(task.Scripts[0].Source));
	}

	[Fact]
	public void Load_SqlServerBatches_SortsFilesRemovesBomAndHashesPreparedUtf8Content()
	{
		using var directory = new MigrationTestDirectory();
		directory.Write("db.ini", "[mssql]\n./sql/*.sql\n");
		directory.Write("mssql.env", "Server=localhost\nDatabase=hosting\nUserName=operator\n");
		var later = directory.Write("sql/020-seed.sql", "SELECT 3;\r\nGO\r\n");
		var first = directory.Write("sql/010-schema.sql", "");
		File.WriteAllText(first, "SELECT N'附件';\r\n-- retained\r\nGO -- client separator\r\nSELECT 2;\r\n", new UTF8Encoding(true));

		var plan = Loader().Load("db.ini", directory.Path, "zongsoft.daemon", "1.1.0");

		var step = Assert.Single(plan.Tasks);
		Assert.Equal(new[] { "SELECT N'附件';\r\n-- retained", "SELECT 2;", "SELECT 3;" }, step.Scripts.Select(script => script.Content));
		Assert.Equal(new[] { first, first, later }, step.Scripts.Select(script => script.Source));
		Assert.Equal(new[] { ".migration/.artifacts/mssql/0001.sql", ".migration/.artifacts/mssql/0002.sql", ".migration/.artifacts/mssql/0003.sql" }, step.Scripts.Select(script => script.Path));
		foreach(var script in step.Scripts)
		{
			Assert.Equal(Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(script.Content))), script.Checksum);
			Assert.DoesNotContain('\uFEFF', script.Content);
		}
		Assert.DoesNotContain("Source", plan.Serialize());
		Assert.DoesNotContain("Content", plan.Serialize());
	}
	#endregion

	#region 辅助方法
	private static Dictionary<string, string> Parameters(MigrationTestDirectory directory, string migration)
	{
		var content = File.ReadAllText(migration);
		if(!content.Contains(".sql", StringComparison.Ordinal)) File.AppendAllText(migration, "./schema.sql\n");
		directory.Write(Path.GetRelativePath(directory.Path, Path.Combine(Path.GetDirectoryName(migration), "schema.sql")), "SELECT 1;");
		return Assert.Single(Loader().Load(migration, directory.Path, "zongsoft.daemon", "1.1.0").Tasks).Parameters;
	}

	private static MigrationPlan.Bucket Bucket(string options)
	{
		using var directory = new MigrationTestDirectory();
		directory.Write("fs.ini", "[amazon.s3]\nattachments=" + options + "\n");
		directory.Write("amazon.s3.env", "Server=http://localhost:9000\nRegion=us-east-1\nAccessKey=test\nSecretKey=test\n");
		return Assert.Single(Assert.Single(Loader().Load("fs.ini", directory.Path, "zongsoft.daemon", "1.1.0").Tasks).Buckets);
	}

	private static MigrationLoader Loader(Dictionary<string, string> variables = null, Action<string> warning = null) => new(value =>
	{
		var result = Normalizer.Normalize(value, variables ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase));
		if(!result.Succeed) throw new InvalidDataException("Undefined variable: " + result.Value);
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
	#endregion

	#region 辅助方法
	public static string GetRuntimeDirectory()
	{
		var root = new DirectoryInfo(AppContext.BaseDirectory);
		while(root != null && !File.Exists(System.IO.Path.Combine(root.FullName, "Zongsoft.Tools.Packager.slnx"))) root = root.Parent;
		Assert.NotNull(root);
		var configuration = new DirectoryInfo(AppContext.BaseDirectory.TrimEnd(System.IO.Path.DirectorySeparatorChar)).Parent.Name;
		var runtime = System.IO.Path.Combine(root.FullName, "migrator", "src", "bin", configuration, "net10.0");
		Assert.True(File.Exists(System.IO.Path.Combine(runtime, "Zongsoft.Tools.Packager.Migrator.deps.json")), runtime);
		return runtime;
	}

	public string CreateRuntime()
	{
		foreach(var runtime in new[] { "linux-x64", "linux-arm64" })
		{
			var header = new byte[64];
			header[0] = 0x7f; header[1] = (byte)'E'; header[2] = (byte)'L'; header[3] = (byte)'F';
			header[4] = 2; header[5] = 1; header[6] = 1;
			header[16] = 3;
			header[18] = runtime == "linux-x64" ? (byte)62 : (byte)183;
			header[20] = 1;
			header[52] = 64;
			var executable = this.Write("runtime/" + runtime + "/Zongsoft.Tools.Packager.Migrator", "");
			File.WriteAllBytes(executable, header);
			this.Write("runtime/" + runtime + "/libe_sqlite3.so", "sqlite-" + runtime);
			this.Write("runtime/" + runtime + "/libduckdb.so", "duckdb-" + runtime);
			this.Write("runtime/" + runtime + "/assets/manifest.txt", runtime);
		}
		return System.IO.Path.Combine(this.Path, "runtime");
	}

	public string Write(string relative, string content)
	{
		var path = System.IO.Path.GetFullPath(System.IO.Path.Combine(this.Path, relative));
		if(!path.StartsWith(this.Path + System.IO.Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("Fixture path escapes its owned directory.");
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
		if(Directory.Exists(this.Path)) Directory.Delete(this.Path, true);
	}
	#endregion
}
