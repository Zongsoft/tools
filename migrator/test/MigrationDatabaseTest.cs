using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;

using Xunit;

using Zongsoft.Tools.Migrator.Migration;

namespace Zongsoft.Tools.Migrator.Tests;

public sealed class MigrationDatabaseTest
{
	[Theory]
	[InlineData("[mysql]\n", "")]
	[InlineData("[mysql hosting]\n", "")]
	[InlineData("[mysql]\n", "[mysql hosting]\n")]
	public void Load_DefaultDatabaseWithoutSettings_InitializesEmptyMigrationWithDefaults(string migration, string settings)
	{
		using var directory = new MigrationTestDirectory();
		var plan = Load(directory, migration, "[mysql]\nServer=localhost\nDatabase=hosting\nPassword=root-secret\n" + settings);

		var database = Assert.Single(plan.Databases);
		var task = Assert.Single(plan.Steps);
		Assert.Equal("hosting", database.Name);
		Assert.Equal(0, task.DatabaseIndex);
		Assert.Empty(task.Scripts);
		Assert.Empty(task.Settings);
		Assert.Equal("root", database.Settings["UserName"]);
		Assert.Equal("root-secret", database.Settings["Password"]);
		Assert.Equal("mysql", database.Settings["Bootstrap"]);
		Assert.Equal("3306", database.Settings["Port"]);
		Assert.Equal("30s", database.Settings["Timeout"]);
		Assert.Equal("300s", database.Options["CommandTimeout"]);
		Assert.Equal("utf8mb4", database.Options["Charset"]);
		Assert.Equal("utf8mb4_0900_ai_ci", database.Options["Collation"]);
	}

	[Fact]
	public void Load_UserSubsectionWithoutDatabaseHeader_ExpandsCredentialsAndRetainsPermissions()
	{
		using var directory = new MigrationTestDirectory();
		directory.Write("main.migration", "[mysql]\n");
		directory.Write("main.ini", "[mysql]\nServer=localhost\nDatabase=hosting\nPassword=$(root)\n[mysql hosting AppUser]\nPassword=$(app)\nPermission=READONLY\nPrivileges=createTABLE|SELECT\nRoles=Reporting,Audit\nHost=10.%\n");
		var variables = new Dictionary<string, string> { ["root"] = "operator-secret", ["app"] = "app'quoted;secret" };
		var loader = new MigrationLoader(value => Normalizer.Normalize(value, variables).Value);

		var database = Assert.Single(loader.Load("main.migration", directory.Path, "test", "1.0.0").Databases);

		var user = Assert.Single(database.Users);
		Assert.Equal("AppUser", user.Name);
		Assert.Equal("app'quoted;secret", user.Password);
		Assert.Equal("readonly", user.Permission);
		Assert.Equal(new[] { "Select", "CreateTable" }, user.Privileges);
		Assert.Equal(new[] { "Reporting", "Audit" }, user.Roles);
		Assert.Equal("10.%", user.Host);
		Assert.Equal("operator-secret", database.Settings["Password"]);
		Assert.Equal("utf8mb4", database.Options["Charset"]);
	}

	[Fact]
	public void Load_ProviderNamedIniWithoutProviderSection_UsesRootSettingsAndDatabaseUserSections()
	{
		using var directory = new MigrationTestDirectory();
		directory.Write("1.0.0/mysql.migration", "[mysql]\n");
		directory.Write("mysql.ini", "Server=localhost\nPort=3306\nDatabase=zongsoft\nUserName=root\nPassword=operator-secret\nSecured=false\nTimeout=30s\nCommandTimeout=10m\n[zongsoft program]\nPassword=application-secret\nPermission=ReadWrite\nPrivileges=CreateTable\n");

		var plan = new MigrationLoader(null).Load("1.0.0/mysql.migration", directory.Path, "test", "1.0.0");
		var database = Assert.Single(plan.Databases);
		var user = Assert.Single(database.Users);

		Assert.Equal("zongsoft", database.Name);
		Assert.Equal("localhost", database.Settings["Server"]);
		Assert.Equal("operator-secret", database.Settings["Password"]);
		Assert.Equal("10m", database.Settings["CommandTimeout"]);
		Assert.Equal("program", user.Name);
		Assert.Equal("application-secret", user.Password);
		Assert.Equal("readwrite", user.Permission);
		Assert.Contains("CreateTable", user.Privileges);
		Assert.Equal(0, Assert.Single(plan.Steps).DatabaseIndex);
	}

	[Fact]
	public void Load_ProviderNamedIniWithExplicitProviderSection_PrefersSectionOverRootSettings()
	{
		using var directory = new MigrationTestDirectory();
		directory.Write("main.migration", "[mysql]\n");
		directory.Write("mysql.ini", "Server=root-host\nDatabase=ignored\nPassword=root-secret\n[mysql]\nServer=section-host\nDatabase=hosting\nPassword=section-secret\n[mysql hosting program]\nPassword=application-secret\n");

		var database = Assert.Single(new MigrationLoader(null).Load("main.migration", directory.Path, "test", "1.0.0").Databases);
		Assert.Equal("hosting", database.Name);
		Assert.Equal("section-host", database.Settings["Server"]);
		Assert.Equal("section-secret", database.Settings["Password"]);
		Assert.Equal("program", Assert.Single(database.Users).Name);
	}

	[Fact]
	public void Load_GenericIniWithoutProviderSection_DoesNotTreatRootAsDatabaseSettings()
	{
		using var directory = new MigrationTestDirectory();
		directory.Write("main.migration", "[mysql]\n");
		directory.Write("main.ini", "Server=localhost\nDatabase=hosting\nPassword=operator-secret\n");

		Assert.Throws<FileNotFoundException>(() => new MigrationLoader(null).Load("main.migration", directory.Path, "test", "1.0.0"));
	}

	[Fact]
	public void Load_ExplicitAndDefaultTargets_RoutesSharedSqlAndExcludesUnusedDeclarations()
	{
		using var directory = new MigrationTestDirectory();
		directory.Write("schema.sql", "SELECT 'shared';");
		var plan = Load(directory, "[mysql]\nschema.sql\n[mysql hosting]\nschema.sql\n[mysql analytics]\nschema.sql\n",
			"[mysql]\nServer=localhost\nDatabase=hosting\nPassword=root\n[mysql hosting App]\nPassword=app\n[mysql analytics App]\nPassword=app\nPermission=readonly\n[mysql unused]\n[mysql unused Unused]\nPassword=unused\n");

		Assert.Equal(new[] { "hosting", "analytics" }, plan.Databases.Select(database => database.Name));
		Assert.All(plan.Databases, database => Assert.Equal("App", Assert.Single(database.Users).Name));
		Assert.DoesNotContain("unused", plan.Serialize(), StringComparison.OrdinalIgnoreCase);
		Assert.Equal(new[] { "hosting", "analytics" }, plan.Steps.Where(task => task.Scripts.Count > 0).Select(task => plan.Databases[task.DatabaseIndex.Value].Name).Distinct());
		Assert.All(plan.Steps.SelectMany(task => task.Scripts), script => Assert.Equal("SELECT 'shared';", script.Content));
		Assert.Equal(2, plan.Steps.SelectMany(task => task.Scripts).Count());
	}

	[Fact]
	public void Load_ExplicitDatabaseBeforeDefaultSection_PreservesSqlDeclarationOrder()
	{
		using var directory = new MigrationTestDirectory();
		directory.Write("first.sql", "SELECT 'analytics-first';");
		directory.Write("second.sql", "SELECT 'hosting-second';");
		var plan = Load(directory, "[mysql analytics]\nfirst.sql\n[mysql]\nsecond.sql\n",
			"[mysql]\nServer=localhost\nDatabase=hosting\nPassword=root\n[mysql analytics]\n");

		Assert.Equal(new[] { "analytics", "hosting" }, plan.Steps.Select(task => plan.Databases[task.DatabaseIndex.Value].Name));
		Assert.Equal(new[] { "SELECT 'analytics-first';", "SELECT 'hosting-second';" }, plan.Steps.SelectMany(task => task.Scripts).Select(script => script.Content));
		Assert.Equal(new[] { ".migration/.artifacts/mysql/1.sql", ".migration/.artifacts/mysql/2.sql" }, plan.Steps.SelectMany(task => task.Scripts).Select(script => script.Path));
	}

	[Fact]
	public void Load_ImportedEmptyDatabase_UsesItsDeclarationSourceForParameters()
	{
		using var directory = new MigrationTestDirectory();
		directory.Write("main.migration", "#@import child/part.migration\n[mysql]\n");
		directory.Write("main.ini", "[mysql]\nServer=root-host\nDatabase=hosting\nPassword=root\n[mysql archive]\nCharset=latin1\n");
		directory.Write("child/part.migration", "[mysql archive]\n");
		directory.Write("child/part.ini", "[mysql]\nServer=child-host\nPassword=child\n[mysql archive]\nCollation=utf8mb4_bin\n");

		var plan = new MigrationLoader(null).Load("main.migration", directory.Path, "test", "1.0.0");

		var archive = Assert.Single(plan.Databases, database => database.Name == "archive");
		Assert.Equal("child-host", archive.Settings["Server"]);
		Assert.Equal("utf8mb4_bin", archive.Options["Collation"]);
		Assert.False(archive.Options.ContainsKey("Charset"));
		Assert.Equal("root-host", Assert.Single(plan.Databases, database => database.Name == "hosting").Settings["Server"]);
		Assert.All(plan.Steps, task => Assert.Empty(task.Scripts));
	}

	[Fact]
	public void Load_SameEmptyDatabaseInMultipleImports_ValidatesEverySourceAndRejectsConflicts()
	{
		using var directory = new MigrationTestDirectory();
		directory.Write("main.migration", "#@import first/part.migration second/part.migration\n");
		directory.Write("first/part.migration", "[mysql archive]\n");
		directory.Write("first/part.ini", "[mysql]\nServer=localhost\nPassword=private-root\n[mysql archive]\nCollation=utf8mb4_bin\n");
		directory.Write("second/part.migration", "[mysql archive]\n");
		directory.Write("second/part.ini", "[mysql]\nServer=localhost\nPassword=private-root\n[mysql archive]\nCollation=utf8mb4_0900_ai_ci\n");

		var error = Assert.Throws<InvalidDataException>(() => new MigrationLoader(null).Load("main.migration", directory.Path, "test", "1.0.0"));

		Assert.Contains("Database", error.Message, StringComparison.OrdinalIgnoreCase);
		Assert.DoesNotContain("private-root", error.ToString());
	}

	[Theory]
	[InlineData(false)]
	[InlineData(true)]
	public void Load_EmptyAndNonemptyDefaultSectionsInDifferentSources_RetainsBothTargets(bool rootHasSql)
	{
		using var directory = new MigrationTestDirectory();
		directory.Write("main.migration", "#@import child/part.migration\n[mysql]\n" + (rootHasSql ? "schema.sql\n" : ""));
		directory.Write("main.ini", "[mysql]\nServer=localhost\nDatabase=hosting\nPassword=root\n");
		directory.Write("child/part.migration", "[mysql]\n" + (rootHasSql ? "" : "schema.sql\n"));
		directory.Write("child/part.ini", "[mysql]\nServer=localhost\nDatabase=archive\nPassword=root\n");
		directory.Write(rootHasSql ? "schema.sql" : "child/schema.sql", "SELECT 'one-target-only';");

		var plan = new MigrationLoader(null).Load("main.migration", directory.Path, "test", "1.0.0");

		Assert.Equal(new[] { "archive", "hosting" }, plan.Databases.Select(database => database.Name));
		var scripted = Assert.Single(plan.Steps, task => task.Scripts.Count > 0);
		Assert.Equal(rootHasSql ? "hosting" : "archive", plan.Databases[scripted.DatabaseIndex.Value].Name);
		Assert.Single(scripted.Scripts);
		Assert.Single(plan.Steps, task => task.Scripts.Count == 0);
	}

	[Theory]
	[InlineData(false)]
	[InlineData(true)]
	public void Load_EmptyAndNonemptyExplicitSectionsInDifferentSources_RejectsConflictingInitialization(bool rootHasSql)
	{
		using var directory = new MigrationTestDirectory();
		directory.Write("main.migration", "#@import child/part.migration\n[mysql archive]\n" + (rootHasSql ? "schema.sql\n" : ""));
		directory.Write("main.ini", "[mysql]\nServer=localhost\nPassword=root\n[mysql archive]\nCollation=utf8mb4_bin\n");
		directory.Write("child/part.migration", "[mysql archive]\n" + (rootHasSql ? "" : "schema.sql\n"));
		directory.Write("child/part.ini", "[mysql]\nServer=localhost\nPassword=root\n[mysql archive]\nCollation=utf8mb4_0900_ai_ci\n");
		directory.Write(rootHasSql ? "schema.sql" : "child/schema.sql", "SELECT 1;");

		var error = Assert.Throws<InvalidDataException>(() => new MigrationLoader(null).Load("main.migration", directory.Path, "test", "1.0.0"));

		Assert.Contains("Database", error.Message, StringComparison.OrdinalIgnoreCase);
	}

	[Fact]
	public void Load_ImportedOtherDatabaseBetweenRootEntries_PreservesInterleavedSqlOrder()
	{
		using var directory = new MigrationTestDirectory();
		directory.Write("main.migration", "[mysql analytics]\nfirst.sql\n#@import child/part.migration\nthird.sql\n");
		directory.Write("mysql.ini", "[mysql]\nServer=localhost\nPassword=root\n[mysql analytics]\n[mysql hosting]\n");
		directory.Write("child/part.migration", "[mysql hosting]\nsecond.sql\n");
		directory.Write("first.sql", "SELECT 'first';");
		directory.Write("child/second.sql", "SELECT 'second';");
		directory.Write("third.sql", "SELECT 'third';");

		var plan = new MigrationLoader(null).Load("main.migration", directory.Path, "test", "1.0.0");

		Assert.Equal(new[] { "SELECT 'first';", "SELECT 'second';", "SELECT 'third';" }, plan.Steps.SelectMany(task => task.Scripts).Select(script => script.Content));
		Assert.Equal(new[] { "analytics", "hosting", "analytics" }, plan.Steps.Select(task => plan.Databases[task.DatabaseIndex.Value].Name));
	}

	[Theory]
	[InlineData("[mysql]\n", "[mysql]\nServer=localhost\nPassword=root\n", "Database")]
	[InlineData("[mysql other]\n", "[mysql]\nServer=localhost\nDatabase=hosting\nPassword=root\n", "Database")]
	[InlineData("[mysql]\n", "[mysql]\nServer=localhost\nDatabase=hosting\n", "Password")]
	[InlineData("[mysql]\n", "[mysql]\nServer=localhost\nDatabase=hosting\nPassword=root\nBootstrapDatabase=mysql\n", "BootstrapDatabase")]
	public void Load_MissingTargetOrRequiredCredentialOrObsoleteParameter_RejectsBeforeOutput(string migration, string settings, string key)
	{
		using var directory = new MigrationTestDirectory();

		var error = Assert.Throws<InvalidDataException>(() => Load(directory, migration, settings));

		Assert.Contains(key, error.Message, StringComparison.OrdinalIgnoreCase);
		Assert.False(Directory.Exists(Path.Combine(directory.Path, ".migration")));
	}

	[Theory]
	[InlineData("", "utf8mb4", "utf8mb4_0900_ai_ci")]
	[InlineData("Charset=utf8mb4\n", "utf8mb4", "utf8mb4_0900_ai_ci")]
	[InlineData("Charset=latin1\n", "latin1", null)]
	[InlineData("Collation=utf8mb4_bin\n", null, "utf8mb4_bin")]
	[InlineData("Charset=utf8mb4\nCollation=utf8mb4_bin\n", "utf8mb4", "utf8mb4_bin")]
	public void Load_MySqlCharsetSelection_AppliesOnlyTheRelevantDefaults(string settings, string charset, string collation)
	{
		using var directory = new MigrationTestDirectory();
		var plan = Load(directory, "[mysql]\n", "[mysql]\nServer=localhost\nDatabase=hosting\nPassword=\n[mysql hosting]\nCommandTimeout=2m\n" + settings);

		var database = Assert.Single(plan.Databases);
		Assert.Equal(charset, database.Options.GetValueOrDefault("Charset"));
		Assert.Equal(collation, database.Options.GetValueOrDefault("Collation"));
		Assert.Equal("2m", database.Options["CommandTimeout"]);
		Assert.Equal("300s", database.Settings["CommandTimeout"]);
		Assert.Equal("", database.Settings["Password"]);
	}

	[Theory]
	[InlineData("postgres", "postgres", "postgres", "5432", "Charset", "UTF8")]
	[InlineData("mssql", "sa", "master", "1433", null, null)]
	[InlineData("tdengine", "root", "", "6041", "Precision", "ms")]
	public void Load_ProviderDefaults_UsesBuiltinAdministratorAndCreationSettings(string provider, string user, string bootstrap, string port, string option, string value)
	{
		using var directory = new MigrationTestDirectory();
		var plan = Load(directory, $"[{provider}]\n", $"[{provider}]\nServer=localhost\nDatabase=hosting\nPassword=operator-secret\n");

		var database = Assert.Single(plan.Databases);
		Assert.Equal(user, database.Settings["UserName"]);
		Assert.Equal(bootstrap, database.Settings["Bootstrap"]);
		Assert.Equal(port, database.Settings["Port"]);
		if(option != null)
			Assert.Equal(value, database.Options[option]);

		if(provider == "tdengine")
		{
			Assert.Equal("3650", database.Options["Keep"]);
			Assert.Equal("10", database.Options["Duration"]);
			Assert.Equal("1", database.Options["Replica"]);
		}

		if(provider == "postgres")
		{
			Assert.Equal("template0", database.Options["Template"]);
			Assert.Equal("-1", database.Options["ConnectionLimit"]);
		}
	}

	[Theory]
	[InlineData(1, "1")]
	[InlineData(10, "10")]
	[InlineData(30, "10")]
	public void Load_TDengineRetentionOverride_ClampsOnlyDefaultDuration(int keep, string expected)
	{
		using var directory = new MigrationTestDirectory();
		var plan = Load(directory, "[tdengine]\n", $"[tdengine]\nServer=localhost\nDatabase=hosting\nPassword=operator\n[tdengine hosting]\nKeep={keep}\n");

		var database = Assert.Single(plan.Databases);
		Assert.Equal(expected, database.Options["Duration"]);
		Assert.Equal(keep.ToString(System.Globalization.CultureInfo.InvariantCulture), database.Options["Keep"]);
		Assert.Equal("ms", database.Options["Precision"]);
	}

	[Theory]
	[InlineData("mysql", "Permission=owner\n", "Permission")]
	[InlineData("mysql", "Password=\n", "Users")]
	[InlineData("mysql", "Privileges=SELECT; DROP USER somebody\n", "Privileges")]
	[InlineData("postgres", "Host=%\n", "Host")]
	[InlineData("mysql", "Unexpected=value\n", "Unexpected")]
	public void Load_InvalidUserFields_RejectsWithoutIncludingInitialPassword(string provider, string settings, string key)
	{
		using var directory = new MigrationTestDirectory();
		var password = settings.StartsWith("Password=", StringComparison.Ordinal) ? "" : "Password=private-user-secret\n";

		var error = Assert.Throws<InvalidDataException>(() => Load(directory, $"[{provider}]\n", $"[{provider}]\nServer=localhost\nDatabase=hosting\nPassword=operator\n[{provider} hosting App]\n{password}{settings}"));

		Assert.Contains(key, error.Message, StringComparison.OrdinalIgnoreCase);
		Assert.DoesNotContain("private-user-secret", error.ToString());
	}

	[Theory]
	[InlineData("mysql", "Timezone=UTC\n")]
	[InlineData("mssql", "Charset=UTF8\n")]
	[InlineData("postgres", "ConnectionLimit=0\n")]
	[InlineData("tdengine", "Keep=5\nDuration=6\n")]
	[InlineData("tdengine", "Replica=2\n")]
	public void Load_UnsupportedOrInvalidCreationSettings_RejectsWithParameterContext(string provider, string settings)
	{
		using var directory = new MigrationTestDirectory();

		var error = Assert.Throws<InvalidDataException>(() => Load(directory, $"[{provider}]\n", $"[{provider}]\nServer=localhost\nDatabase=hosting\nPassword=operator-secret\n[{provider} hosting]\n" + settings));

		Assert.DoesNotContain("operator-secret", error.ToString());
		Assert.Contains("main.ini", error.Message);
	}

	[Theory]
	[InlineData("sqlite", "linux-x64", "/data/hosting.db")]
	[InlineData("duckdb", "linux-arm64", "/data/hosting.db")]
	[InlineData("sqlite", "win-x64", "C:/Data/hosting.db")]
	public void Load_NamedFileDatabase_UsesExplicitPathAndDefaultDatabaseAlias(string provider, string runtime, string path)
	{
		using var directory = new MigrationTestDirectory();
		var plan = Load(directory, $"[{provider}]\n", $"[{provider}]\nDatabase=hosting\n[{provider} hosting]\nPath={path}\n", runtime);

		var database = Assert.Single(plan.Databases);
		Assert.Equal("hosting", database.Name);
		Assert.Equal(path, database.Options["Path"]);
		Assert.Empty(database.Users);
	}

	[Theory]
	[InlineData("C:\\Data\\hosting.db")]
	[InlineData("c:/data/HOSTING.db")]
	public void Load_WindowsPathAliases_RejectsConflictingSettingsForSameDatabaseFile(string secondPath)
	{
		using var directory = new MigrationTestDirectory();
		directory.Write("first.migration", "[sqlite]\n");
		directory.Write("first.ini", "[sqlite]\nDatabase=hosting\n[sqlite hosting]\nPath=C:/Data/hosting.db\nCharset=UTF-8\n");
		directory.Write("second.migration", "[sqlite]\n");
		directory.Write("second.ini", $"[sqlite]\nDatabase=hosting\n[sqlite hosting]\nPath={secondPath}\nCharset=UTF-16le\n");

		var error = Assert.Throws<InvalidDataException>(() => new MigrationLoader(null).Load("first.migration;second.migration", directory.Path, "test", "1.0.0", "win-x64"));

		Assert.Contains("Database", error.Message, StringComparison.OrdinalIgnoreCase);
	}

	[Theory]
	[InlineData("sqlite")]
	[InlineData("duckdb")]
	public void Load_FileDatabaseUsers_RejectsUnsupportedAccountConfiguration(string provider)
	{
		using var directory = new MigrationTestDirectory();

		var error = Assert.Throws<InvalidDataException>(() => Load(directory, $"[{provider}]\n", $"[{provider}]\nDatabase=hosting\n[{provider} hosting]\nPath=/data/hosting.db\n[{provider} hosting App]\nPassword=app-secret\n"));

		Assert.DoesNotContain("app-secret", error.ToString());
	}

	[Theory]
	[InlineData("same", true)]
	[InlineData("different", false)]
	public void Load_CrossDatabaseUserPasswords_RequireConsistencyWithoutLeakingValues(string password, bool valid)
	{
		using var directory = new MigrationTestDirectory();
		const string MIGRATION = "[mysql first]\n[mysql second]\n";
		var settings = $"[mysql]\nServer=localhost\nPassword=root-secret\n[mysql first App]\nPassword=same\n[mysql second App]\nPassword={password}\nPermission=readonly\n";

		if(valid)
		{
			var plan = Load(directory, MIGRATION, settings);
			Assert.Equal(2, plan.Databases.Count);
			Assert.Equal(new[] { "readwrite", "readonly" }, plan.Databases.Select(database => Assert.Single(database.Users).Permission));
		}
		else
		{
			var error = Assert.Throws<InvalidDataException>(() => Load(directory, MIGRATION, settings));
			Assert.DoesNotContain("root-secret", error.ToString());
			Assert.DoesNotContain("different", error.ToString());
			Assert.DoesNotContain("same", error.ToString());
		}
	}

	[Fact]
	public void Fingerprint_UserAndCreationChanges_AffectPlanAndRoundTripPreservesCredentialBytes()
	{
		using var directory = new MigrationTestDirectory();
		var plan = Load(directory, "[mysql]\n", "[mysql]\nServer=localhost\nDatabase=hosting\nPassword=root\n[mysql hosting App]\nPassword=initial\n");
		var database = Assert.Single(plan.Databases);
		var user = Assert.Single(database.Users);
		var original = plan.Fingerprint();
		user.Password = "first\r\nsecond";
		var credential = plan.Fingerprint();
		Assert.NotEqual(original, credential);
		user.Permission = "readonly";
		var permission = plan.Fingerprint();
		Assert.NotEqual(credential, permission);
		database.Options["Collation"] = "utf8mb4_bin";
		Assert.NotEqual(permission, plan.Fingerprint());

		var loaded = MigrationPlan.Load(directory.Write("plan.json", plan.Serialize()));

		Assert.Equal(plan.Fingerprint(), loaded.Fingerprint());
		Assert.Equal("first\r\nsecond", Assert.Single(Assert.Single(loaded.Databases).Users).Password);
		Assert.Equal(0, Assert.Single(loaded.Steps).DatabaseIndex);
	}

	[Fact]
	public void Load_SameDatabaseNameAcrossServers_IndexesTargetsAndReusesPriorTarget()
	{
		using var directory = new MigrationTestDirectory();
		directory.Write("main.migration", "[mysql]\nfirst.sql\n#@import remote/part.migration\nthird.sql\n");
		directory.Write("main.ini", "[mysql]\nServer=first-server\nDatabase=hosting\nPassword=first-admin\n");
		directory.Write("remote/part.migration", "[mysql]\nsecond.sql\n");
		directory.Write("remote/part.ini", "[mysql]\nServer=second-server\nDatabase=hosting\nPassword=second-admin\n");
		directory.Write("first.sql", "SELECT 'first';");
		directory.Write("remote/second.sql", "SELECT 'second';");
		directory.Write("third.sql", "SELECT 'third';");

		var plan = new MigrationLoader(null).Load("main.migration", directory.Path, "test", "1.0.0");
		var loaded = MigrationPlan.Load(directory.Write("plan.json", plan.Serialize()));

		Assert.Equal(new[] { "hosting", "hosting" }, loaded.Databases.Select(database => database.Name));
		Assert.Equal(new[] { "first-server", "second-server" }, loaded.Databases.Select(database => database.Settings["Server"]));
		Assert.Equal(new int?[] { 0, 1, 0 }, loaded.Steps.Select(step => step.DatabaseIndex));
		Assert.Equal(new[] { "first-server", "second-server", "first-server" }, loaded.Steps.Select(step => loaded.Databases[step.DatabaseIndex.Value].Settings["Server"]));
		Assert.Equal(new[] { "SELECT 'first';", "SELECT 'second';", "SELECT 'third';" }, plan.Steps.SelectMany(step => step.Scripts).Select(script => script.Content));
		Assert.Equal(plan.Fingerprint(), loaded.Fingerprint());
	}

	private static MigrationPlan Load(MigrationTestDirectory directory, string migration, string settings, string runtime = "linux-x64")
	{
		directory.Write("main.migration", migration);
		directory.Write("main.ini", settings);
		return new MigrationLoader(null).Load("main.migration", directory.Path, "test", "1.0.0", runtime);
	}
}
