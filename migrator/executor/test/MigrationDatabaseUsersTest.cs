using System;
using System.IO;
using System.Data;
using System.Linq;
using System.Data.Common;
using System.Threading.Tasks;
using System.Collections.Generic;

using Xunit;

namespace Zongsoft.Tools.Migrator.Migration.Tests;

public sealed partial class MigrationDatabaseUsersTest
{
	[Theory]
	[InlineData("mysql")]
	[InlineData("postgres")]
	public async Task Users_CreateOnceKeepExistingPasswordAndAppendGrantsOnEveryApplyAsync(string provider)
	{
		using var directory = new MigrationTestDirectory();
		var database = Database(provider);
		var exists = false;
		var commands = new List<string>();
		DbConnection Connect(MigrationPlan.Database _, string name) => new RecordingConnection(commands, sql =>
		{
			if(sql.Contains("FROM mysql.user", StringComparison.Ordinal) || sql == "SELECT rolname FROM pg_roles")
				return exists ? [["application"]] : [];
			if(sql.Contains("pg_namespace", StringComparison.Ordinal))
				return [["public"]];
			return [];
		}, sql =>
		{
			if(sql.StartsWith("CREATE USER", StringComparison.Ordinal) || sql.StartsWith("CREATE ROLE", StringComparison.Ordinal))
				exists = true;
		});
		Migrator.Database driver = provider == "mysql" ? new Migrator.Database.MySql(Connect) : new Migrator.Database.Postgres(Connect);
		var context = new MigrationContext(directory.Path, Path.Combine(directory.Path, "state"));

		await driver.CreateUsersAsync(database, context, TestContext.Current.CancellationToken);
		await driver.GrantAsync(database, context, TestContext.Current.CancellationToken);
		database.Users[0].Password = "replacement-password-must-not-be-applied";
		await driver.CreateUsersAsync(database, context, TestContext.Current.CancellationToken);
		await driver.GrantAsync(database, context, TestContext.Current.CancellationToken);

		Assert.True(exists);
		Assert.Single(commands, sql => sql.StartsWith("CREATE USER", StringComparison.Ordinal) || sql.StartsWith("CREATE ROLE", StringComparison.Ordinal));
		Assert.DoesNotContain(commands, sql => sql.Contains("replacement-password", StringComparison.Ordinal));
		Assert.DoesNotContain(commands, sql => sql.StartsWith("REVOKE", StringComparison.Ordinal) || sql.StartsWith("ALTER USER", StringComparison.Ordinal) || sql.StartsWith("ALTER ROLE", StringComparison.Ordinal));
		Assert.Equal(2, commands.Count(sql => sql.StartsWith(provider == "mysql" ? "GRANT SELECT, INSERT, UPDATE, DELETE, EXECUTE ON" : "GRANT CONNECT ON DATABASE", StringComparison.Ordinal)));
		if(provider == "postgres")
		{
			Assert.Equal(2, commands.Count(sql => sql.Contains("ON ALL TABLES IN SCHEMA \"public\"", StringComparison.Ordinal)));
			Assert.Equal(2, commands.Count(sql => sql.StartsWith("ALTER DEFAULT PRIVILEGES FOR ROLE \"postgres\" IN SCHEMA \"public\" GRANT USAGE, SELECT ON SEQUENCES", StringComparison.Ordinal)));
		}
	}

	[Fact]
	public async Task MySql_ExistingDatabase_SkipsCreationAndCharsetValidationAsync()
	{
		using var directory = new MigrationTestDirectory();
		var database = Database("mysql");
		var commands = new List<string>();
		var driver = new Migrator.Database.MySql((_, _) => new RecordingConnection(commands, sql => sql == "SHOW DATABASES" ? [[database.Name]] : throw new InvalidOperationException("Unexpected query: " + sql)));

		await driver.InitializeAsync(database, new(directory.Path, Path.Combine(directory.Path, "state")), TestContext.Current.CancellationToken);

		Assert.Equal(new[] { "SHOW DATABASES" }, commands);
	}

	[Fact]
	public async Task MySql_NewDatabase_UsesEffectiveCharsetAndCollationAsync()
	{
		using var directory = new MigrationTestDirectory();
		var database = Database("mysql");
		var commands = new List<string>();
		var driver = new Migrator.Database.MySql((_, _) => new RecordingConnection(commands, sql => sql.Contains("information_schema.COLLATIONS", StringComparison.Ordinal) ? [["utf8mb4"]] : []));

		await driver.InitializeAsync(database, new(directory.Path, Path.Combine(directory.Path, "state")), TestContext.Current.CancellationToken);

		Assert.Contains("CREATE DATABASE `hosting` CHARACTER SET `utf8mb4` COLLATE `utf8mb4_0900_ai_ci`", commands);
		Assert.Empty(Directory.GetFiles(Path.Combine(directory.Path, "state"), "*.pending"));
	}

	[Fact]
	public async Task MySql_AdditionalRole_MergesExistingDefaultRolesAsync()
	{
		using var directory = new MigrationTestDirectory();
		var database = Database("mysql");
		database.Users[0].Roles = ["reporting"];
		var commands = new List<string>();
		var driver = new Migrator.Database.MySql((_, _) => new RecordingConnection(commands, sql => sql.StartsWith("SHOW GRANTS", StringComparison.Ordinal) ? [["GRANT SELECT ON `hosting`.* TO 'reporting'@'%'"]] : sql.Contains("mysql.default_roles", StringComparison.Ordinal) ? [["existing", "localhost"]] : []));

		await driver.GrantAsync(database, new(directory.Path, Path.Combine(directory.Path, "state")), TestContext.Current.CancellationToken);

		Assert.Contains("GRANT 'reporting'@'%' TO 'application'@'%'", commands);
		Assert.Contains("SET DEFAULT ROLE 'existing'@'localhost', 'reporting'@'%' TO 'application'@'%'", commands);
	}

	[Theory]
	[InlineData(false)]
	[InlineData(true)]
	public async Task MySql_PasswordPunctuation_UsesSessionLiteralEscapingAsync(bool noBackslashEscapes)
	{
		using var directory = new MigrationTestDirectory();
		var database = Database("mysql");
		database.Users[0].Password = "a'b\\c";
		var commands = new List<string>();
		var driver = new Migrator.Database.MySql((_, _) => new RecordingConnection(commands, sql => sql == "SELECT @@SESSION.sql_mode" ? [[noBackslashEscapes ? "NO_BACKSLASH_ESCAPES" : ""]] : []));

		await driver.CreateUsersAsync(database, new(directory.Path, Path.Combine(directory.Path, "state")), TestContext.Current.CancellationToken);

		Assert.Contains("CREATE USER 'application'@'%' IDENTIFIED BY " + (noBackslashEscapes ? "'a''b\\c'" : "'a''b\\\\c'"), commands);
	}

	[Fact]
	public async Task Postgres_InitializationFailure_RetryCompletesTimezoneWithoutRecreatingDatabaseAsync()
	{
		using var directory = new MigrationTestDirectory();
		var database = Database("postgres");
		database.Options["Timezone"] = "Asia/Shanghai";
		var exists = false;
		var fail = true;
		var commands = new List<string>();
		var driver = new Migrator.Database.Postgres((_, _) => new RecordingConnection(commands, _ => exists ? [[database.Name]] : [], sql =>
		{
			if(sql.StartsWith("CREATE DATABASE", StringComparison.Ordinal))
				exists = true;
			if(sql.StartsWith("ALTER DATABASE", StringComparison.Ordinal) && fail)
				throw new InvalidOperationException("configuration failed");
		}));
		var context = new MigrationContext(directory.Path, Path.Combine(directory.Path, "state"));

		await Assert.ThrowsAsync<InvalidOperationException>(() => driver.InitializeAsync(database, context, TestContext.Current.CancellationToken));
		Assert.True(exists);
		var pending = Assert.Single(Directory.GetFiles(context.StateDirectory, "*.pending"));
		Assert.DoesNotContain("administrator-secret", File.ReadAllText(pending));
		Assert.DoesNotContain("initial-password", File.ReadAllText(pending));
		fail = false;
		await driver.InitializeAsync(database, context, TestContext.Current.CancellationToken);

		Assert.Single(commands, sql => sql.StartsWith("CREATE DATABASE", StringComparison.Ordinal));
		Assert.Equal(2, commands.Count(sql => sql == "ALTER DATABASE \"hosting\" SET timezone TO E'Asia/Shanghai'"));
		Assert.Empty(Directory.GetFiles(context.StateDirectory, "*.pending"));
	}

	[Fact]
	public async Task MsSql_CreateLoginAndUserOnce_PreservesPasswordAndExistingRolesAsync()
	{
		using var directory = new MigrationTestDirectory();
		var database = Database("mssql");
		var login = false;
		var user = false;
		var commands = new List<string>();
		var roles = new HashSet<string>(StringComparer.Ordinal) { "db_datareader" };
		database.Users[0].Roles = ["db_datareader", "db_datawriter"];
		var driver = new Migrator.Database.MsSql((_, _) => new RecordingConnection(commands, sql =>
		{
			if(sql == "SELECT N'application' FROM sys.server_principals WHERE name = N'application'")
				return login ? [["application"]] : [];
			if(sql == "SELECT N'application' FROM sys.database_principals WHERE name = N'application'")
				return user ? [["application"]] : [];
			if(sql.Contains("dp.sid = sp.sid", StringComparison.Ordinal))
				return [["application"]];
			if(sql.Contains("sys.database_role_members", StringComparison.Ordinal))
				return roles.Any(role => sql.Contains("N'" + role + "'", StringComparison.Ordinal)) ? [["application"]] : [];
			return [];
		}, sql =>
		{
			if(sql.StartsWith("CREATE LOGIN", StringComparison.Ordinal))
				login = true;
			if(sql.StartsWith("CREATE USER", StringComparison.Ordinal))
				user = true;
			if(sql == "ALTER ROLE [db_datawriter] ADD MEMBER [application]")
				roles.Add("db_datawriter");
		}));
		var context = new MigrationContext(directory.Path, Path.Combine(directory.Path, "state"));

		await driver.CreateUsersAsync(database, context, TestContext.Current.CancellationToken);
		await driver.GrantAsync(database, context, TestContext.Current.CancellationToken);
		database.Users[0].Password = "replacement-must-not-be-applied";
		await driver.CreateUsersAsync(database, context, TestContext.Current.CancellationToken);
		await driver.GrantAsync(database, context, TestContext.Current.CancellationToken);

		Assert.Single(commands, sql => sql == "CREATE LOGIN [application] WITH PASSWORD = N'initial-password'");
		Assert.Single(commands, sql => sql == "CREATE USER [application] FOR LOGIN [application]");
		Assert.Single(commands, sql => sql == "ALTER ROLE [db_datawriter] ADD MEMBER [application]");
		Assert.DoesNotContain(commands, sql => sql.Contains("replacement-must-not-be-applied", StringComparison.Ordinal));
		Assert.DoesNotContain("ALTER ROLE [db_datareader] ADD MEMBER [application]", commands);
		Assert.DoesNotContain(commands, sql => sql.Contains("DROP MEMBER", StringComparison.Ordinal));
	}

	[Fact]
	public async Task MsSql_ExistingUserMappedToAnotherLogin_RejectsWithoutAlteringPasswordAsync()
	{
		using var directory = new MigrationTestDirectory();
		var database = Database("mssql");
		var commands = new List<string>();
		var driver = new Migrator.Database.MsSql((_, _) => new RecordingConnection(commands, sql => sql.Contains("dp.sid = sp.sid", StringComparison.Ordinal) ? [] : [["application"]]));

		await Assert.ThrowsAsync<InvalidDataException>(() => driver.CreateUsersAsync(database, new(directory.Path, Path.Combine(directory.Path, "state")), TestContext.Current.CancellationToken));

		Assert.DoesNotContain(commands, sql => sql.StartsWith("CREATE", StringComparison.Ordinal) || sql.StartsWith("ALTER", StringComparison.Ordinal));
	}

	[Theory]
	[InlineData("mysql")]
	[InlineData("postgres")]
	[InlineData("mssql")]
	public async Task Users_CatalogPermissionDenied_DoesNotTreatFailureAsAbsentAsync(string provider)
	{
		using var directory = new MigrationTestDirectory();
		var database = Database(provider);
		var commands = new List<string>();
		DbConnection Connect(MigrationPlan.Database _, string name) => new RecordingConnection(commands, _ => throw new InvalidOperationException("permission denied"));
		Migrator.Database driver = provider switch { "mysql" => new Migrator.Database.MySql(Connect), "postgres" => new Migrator.Database.Postgres(Connect), _ => new Migrator.Database.MsSql(Connect) };

		await Assert.ThrowsAsync<InvalidOperationException>(() => driver.CreateUsersAsync(database, new(directory.Path, Path.Combine(directory.Path, "state")), TestContext.Current.CancellationToken));

		Assert.DoesNotContain(commands, sql => sql.StartsWith("CREATE", StringComparison.Ordinal));
	}
	[Theory]
	[InlineData("admin", "SELECT")]
	[InlineData("admin", "CreateTable")]
	public async Task MySql_AdminWithAdditionalPrivileges_GrantsAllCapabilitiesOnceAsync(string permission, string privilege)
	{
		using var directory = new MigrationTestDirectory();
		var database = Database("mysql");
		database.Users[0].Permission = permission;
		database.Users[0].Privileges = [privilege];
		var commands = new List<string>();
		var driver = new Migrator.Database.MySql((_, _) => new RecordingConnection(commands, _ => []));

		await driver.GrantAsync(database, new(directory.Path, Path.Combine(directory.Path, "state")), TestContext.Current.CancellationToken);

		Assert.Equal("GRANT SELECT, INSERT, UPDATE, DELETE, EXECUTE, CREATE, REFERENCES, INDEX, CREATE VIEW, SHOW VIEW, CREATE ROUTINE, ALTER, DROP, ALTER ROUTINE ON `hosting`.* TO 'application'@'%'", Assert.Single(commands, sql => sql.StartsWith("GRANT", StringComparison.Ordinal)));
	}

	[Fact]
	public async Task Postgres_CreateFailedAndConfirmedAbsent_LaterExternalDatabaseKeepsItsSettingsAsync()
	{
		using var directory = new MigrationTestDirectory();
		var database = Database("postgres");
		database.Options["Timezone"] = "Asia/Shanghai";
		var externallyCreated = false;
		var commands = new List<string>();
		var driver = new Migrator.Database.Postgres((_, _) => new RecordingConnection(commands, _ => externallyCreated ? [[database.Name]] : [], sql =>
		{
			if(sql.StartsWith("CREATE DATABASE", StringComparison.Ordinal))
				throw new CatalogException();
		}));
		var context = new MigrationContext(directory.Path, Path.Combine(directory.Path, "state"));

		await Assert.ThrowsAsync<CatalogException>(() => driver.InitializeAsync(database, context, TestContext.Current.CancellationToken));
		Assert.Empty(Directory.GetFiles(context.StateDirectory, "*.pending"));
		externallyCreated = true;
		await driver.InitializeAsync(database, context, TestContext.Current.CancellationToken);

		Assert.DoesNotContain(commands, sql => sql.StartsWith("ALTER DATABASE", StringComparison.Ordinal));
	}

	[Fact]
	public async Task Postgres_PendingConfigurationChanged_RejectsWithoutApplyingDifferentSettingsAsync()
	{
		using var directory = new MigrationTestDirectory();
		var database = Database("postgres");
		database.Options["Timezone"] = "Asia/Shanghai";
		var exists = false;
		var commands = new List<string>();
		var driver = new Migrator.Database.Postgres((_, _) => new RecordingConnection(commands, _ => exists ? [[database.Name]] : [], sql =>
		{
			if(sql.StartsWith("CREATE DATABASE", StringComparison.Ordinal))
				exists = true;
			if(sql.StartsWith("ALTER DATABASE", StringComparison.Ordinal))
				throw new InvalidOperationException("interrupted");
		}));
		var context = new MigrationContext(directory.Path, Path.Combine(directory.Path, "state"));
		await Assert.ThrowsAsync<InvalidOperationException>(() => driver.InitializeAsync(database, context, TestContext.Current.CancellationToken));
		database.Options["Timezone"] = "UTC";

		await Assert.ThrowsAsync<InvalidDataException>(() => driver.InitializeAsync(database, context, TestContext.Current.CancellationToken));

		Assert.Single(Directory.GetFiles(context.StateDirectory, "*.pending"));
		Assert.Single(commands, sql => sql.StartsWith("ALTER DATABASE", StringComparison.Ordinal));
		Assert.DoesNotContain(commands, sql => sql.Contains("E'UTC'", StringComparison.Ordinal));
	}

	[Fact]
	public async Task Postgres_PendingForAnotherDatabase_DoesNotBlockIndependentInitializationAsync()
	{
		using var directory = new MigrationTestDirectory();
		var first = Database("postgres");
		first.Options["Timezone"] = "Asia/Shanghai";
		var second = Database("postgres");
		second.Name = "analytics";
		second.Options["Timezone"] = "UTC";
		var commands = new List<string>();
		var driver = new Migrator.Database.Postgres((_, _) => new RecordingConnection(commands, _ => [], sql =>
		{
			if(sql == "ALTER DATABASE \"hosting\" SET timezone TO E'Asia/Shanghai'")
				throw new InvalidOperationException("interrupted");
		}));
		var context = new MigrationContext(directory.Path, Path.Combine(directory.Path, "state"));
		await Assert.ThrowsAsync<InvalidOperationException>(() => driver.InitializeAsync(first, context, TestContext.Current.CancellationToken));
		var pending = Assert.Single(Directory.GetFiles(context.StateDirectory, "*.pending"));

		await driver.InitializeAsync(second, context, TestContext.Current.CancellationToken);

		Assert.Equal(pending, Assert.Single(Directory.GetFiles(context.StateDirectory, "*.pending")));
		Assert.Contains("ALTER DATABASE \"analytics\" SET timezone TO E'UTC'", commands);
	}

	[Theory]
	[InlineData(false, "`tenant\\_20\\%`.*")]
	[InlineData(true, "`tenant_20%`.*")]
	public async Task MySql_DatabaseWildcardCharacters_GrantTargetsOnlyLiteralDatabaseAsync(bool partialRevokes, string scope)
	{
		using var directory = new MigrationTestDirectory();
		var database = Database("mysql");
		database.Name = "tenant_20%";
		database.Users[0].Permission = "readonly";
		var commands = new List<string>();
		var driver = new Migrator.Database.MySql((_, _) => new RecordingConnection(commands, sql => sql == "SELECT @@GLOBAL.partial_revokes" ? [[partialRevokes ? "1" : "0"]] : []));

		await driver.GrantAsync(database, new(directory.Path, Path.Combine(directory.Path, "state")), TestContext.Current.CancellationToken);

		Assert.Equal("GRANT SELECT ON " + scope + " TO 'application'@'%'", Assert.Single(commands, sql => sql.StartsWith("GRANT", StringComparison.Ordinal)));
	}
	[Theory]
	[InlineData(false)]
	[InlineData(true)]
	public async Task MsSql_ExistingNamesDifferByCase_UsesServerCatalogComparisonAsync(bool caseSensitive)
	{
		using var directory = new MigrationTestDirectory();
		var database = Database("mssql");
		var databaseExists = !caseSensitive;
		var loginExists = !caseSensitive;
		var userExists = !caseSensitive;
		var commands = new List<string>();
		var driver = new Migrator.Database.MsSql((_, _) => new RecordingConnection(commands, sql =>
		{
			if(sql == "SELECT N'hosting' FROM sys.databases WHERE name = N'hosting'")
				return databaseExists ? [["hosting"]] : [];
			if(sql == "SELECT N'application' FROM sys.server_principals WHERE name = N'application'")
				return loginExists ? [["application"]] : [];
			if(sql == "SELECT N'application' FROM sys.database_principals WHERE name = N'application'")
				return userExists ? [["application"]] : [];
			if(sql.Contains("dp.sid = sp.sid", StringComparison.Ordinal))
				return [["Application"]];
			if(sql == "SELECT name FROM sys.databases")
				return [["Hosting"]];
			if(sql == "SELECT name FROM sys.server_principals" || sql == "SELECT name FROM sys.database_principals")
				return [["Application"]];
			throw new InvalidOperationException("Unexpected catalog query");
		}, sql =>
		{
			if(sql.StartsWith("CREATE DATABASE", StringComparison.Ordinal))
				databaseExists = true;
			if(sql.StartsWith("CREATE LOGIN", StringComparison.Ordinal))
				loginExists = true;
			if(sql.StartsWith("CREATE USER", StringComparison.Ordinal))
				userExists = true;
		}));
		var context = new MigrationContext(directory.Path, Path.Combine(directory.Path, "state"));

		await driver.InitializeAsync(database, context, TestContext.Current.CancellationToken);
		await driver.CreateUsersAsync(database, context, TestContext.Current.CancellationToken);

		Assert.Equal(caseSensitive ? 3 : 0, commands.Count(sql => sql.StartsWith("CREATE", StringComparison.Ordinal)));
		Assert.Contains("SELECT N'hosting' FROM sys.databases WHERE name = N'hosting'", commands);
		Assert.Contains("SELECT N'application' FROM sys.server_principals WHERE name = N'application'", commands);
		Assert.Contains("SELECT N'application' FROM sys.database_principals WHERE name = N'application'", commands);
		Assert.DoesNotContain(commands, sql => sql.StartsWith("ALTER", StringComparison.Ordinal));
	}
	private sealed class CatalogException : DbException;
	private static MigrationPlan.Database Database(string provider)
	{
		var database = new MigrationPlan.Database
		{
			Provider = provider,
			Name = "hosting",
			Settings = new(StringComparer.OrdinalIgnoreCase) { ["Server"] = "localhost", ["Password"] = "administrator-secret" },
			Users = [new() { Name = "application", Password = "initial-password" }],
		};
		MigrationProvider.Get(provider).Prepare(database, "linux-x64");
		database.Users[0].Privileges = [];
		return database;
	}

	private sealed class RecordingConnection(List<string> commands, Func<string, string[][]> query, Action<string> execute = null) : DbConnection
	{
		private ConnectionState _state;
		public override string ConnectionString { get; set; }
		public override string Database => "test";
		public override string DataSource => "test";
		public override string ServerVersion => "1";
		public override ConnectionState State => _state;
		public override void Open() => _state = ConnectionState.Open;
		public override void Close() => _state = ConnectionState.Closed;
		public override void ChangeDatabase(string databaseName) => throw new NotSupportedException();
		protected override DbTransaction BeginDbTransaction(IsolationLevel isolationLevel) => throw new NotSupportedException();
		protected override DbCommand CreateDbCommand() => new RecordingCommand(this, commands, query, execute);
	}

	private sealed class RecordingCommand(DbConnection connection, List<string> commands, Func<string, string[][]> query, Action<string> execute) : DbCommand
	{
		public override string CommandText { get; set; }
		public override int CommandTimeout { get; set; }
		public override CommandType CommandType { get; set; }
		public override bool DesignTimeVisible { get; set; }
		public override UpdateRowSource UpdatedRowSource { get; set; }
		protected override DbConnection DbConnection { get; set; } = connection;
		protected override DbTransaction DbTransaction { get; set; }
		protected override DbParameterCollection DbParameterCollection => throw new NotSupportedException();
		public override void Cancel() { }
		public override void Prepare() { }
		public override object ExecuteScalar() => throw new NotSupportedException();
		protected override DbParameter CreateDbParameter() => throw new NotSupportedException();
		public override int ExecuteNonQuery()
		{
			commands.Add(this.CommandText);
			execute?.Invoke(this.CommandText);
			return 1;
		}
		protected override DbDataReader ExecuteDbDataReader(CommandBehavior behavior)
		{
			commands.Add(this.CommandText);
			var rows = query(this.CommandText);
			var table = new DataTable();
			for(var column = 0; column < (rows.Length == 0 ? 1 : rows[0].Length); column++)
				table.Columns.Add("c" + column, typeof(string));

			foreach(var row in rows)
				table.Rows.Add(row.Cast<object>().ToArray());

			return table.CreateDataReader();
		}
	}
}
