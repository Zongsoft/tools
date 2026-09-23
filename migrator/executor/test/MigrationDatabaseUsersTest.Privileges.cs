using System;
using System.IO;
using System.Linq;
using System.Data.Common;
using System.Text.Json;
using System.Threading.Tasks;
using System.Collections.Generic;

using Xunit;

namespace Zongsoft.Tools.Migrator.Migration.Tests;

public sealed partial class MigrationDatabaseUsersTest
{
	[Theory]
	[InlineData("mysql")]
	[InlineData("mssql")]
	[InlineData("postgres")]
	public async Task Grants_NoneWithoutExtras_DoesNotAppendPermissionsAsync(string provider)
	{
		using var directory = new MigrationTestDirectory();
		var database = Database(provider);
		database.Users[0].Permission = "none";
		var commands = new List<string>();
		DbConnection Connect(MigrationPlan.Database _, string name) => new RecordingConnection(commands, _ => []);
		Migrator.Database driver = provider switch { "mysql" => new Migrator.Database.MySql(Connect), "postgres" => new Migrator.Database.Postgres(Connect), _ => new Migrator.Database.MsSql(Connect) };
		await driver.GrantAsync(database, new(directory.Path, Path.Combine(directory.Path, "state")), TestContext.Current.CancellationToken);
		Assert.DoesNotContain(commands, sql => sql.StartsWith("GRANT", StringComparison.Ordinal) || sql.StartsWith("ALTER", StringComparison.Ordinal));
	}

	[Theory]
	[InlineData("Select", "SELECT")]
	[InlineData("Insert", "INSERT")]
	[InlineData("Update", "UPDATE")]
	[InlineData("Delete", "DELETE")]
	[InlineData("Execute", "EXECUTE")]
	[InlineData("CreateTable", "CREATE, REFERENCES")]
	[InlineData("CreateIndex", "INDEX")]
	[InlineData("CreateView", "CREATE VIEW, SHOW VIEW, SELECT")]
	[InlineData("CreateProcedure", "CREATE ROUTINE")]
	[InlineData("CreateFunction", "CREATE ROUTINE")]
	[InlineData("AlterTable", "ALTER, CREATE, INSERT, REFERENCES, INDEX")]
	[InlineData("AlterIndex", "ALTER, CREATE, INSERT, INDEX")]
	[InlineData("AlterView", "CREATE VIEW, DROP, SHOW VIEW, SELECT")]
	[InlineData("AlterProcedure", "ALTER ROUTINE, CREATE ROUTINE")]
	[InlineData("AlterFunction", "ALTER ROUTINE, CREATE ROUTINE")]
	[InlineData("DropTable", "DROP")]
	[InlineData("DropIndex", "INDEX")]
	[InlineData("DropView", "DROP")]
	[InlineData("DropProcedure", "ALTER ROUTINE")]
	[InlineData("DropFunction", "ALTER ROUTINE")]
	public async Task MySql_UnifiedOperations_ExpandIntoDatabaseScopedDependenciesAsync(string privilege, string expected)
	{
		using var directory = new MigrationTestDirectory();
		var database = Database("mysql");
		database.Users[0].Permission = "none";
		database.Users[0].Privileges = [privilege];
		var commands = new List<string>();
		var driver = new Migrator.Database.MySql((_, name) =>
		{
			Assert.Equal("hosting", name);
			return new RecordingConnection(commands, _ => []);
		});

		await driver.GrantAsync(database, new(directory.Path, Path.Combine(directory.Path, "state")), TestContext.Current.CancellationToken);

		Assert.Equal($"GRANT {expected} ON `hosting`.* TO 'application'@'%'", Assert.Single(commands, sql => sql.StartsWith("GRANT", StringComparison.Ordinal)));
	}

	[Fact]
	public async Task MySql_ProcedureAndFunctionAliases_GrantOnceAsync()
	{
		using var directory = new MigrationTestDirectory();
		var database = Database("mysql");
		database.Users[0].Permission = "none";
		database.Users[0].Privileges = ["CreateProcedure", "CreateFunction", "createFUNCTION"];
		var commands = new List<string>();
		await new Migrator.Database.MySql((_, _) => new RecordingConnection(commands, _ => []))
			.GrantAsync(database, new(directory.Path, Path.Combine(directory.Path, "state")), TestContext.Current.CancellationToken);
		Assert.Equal("GRANT CREATE ROUTINE ON `hosting`.* TO 'application'@'%'", Assert.Single(commands, sql => sql.StartsWith("GRANT", StringComparison.Ordinal)));
	}

	[Theory]
	[InlineData("postgres", "readonly", null)]
	[InlineData("postgres", "none", "Insert")]
	[InlineData("mssql", "readonly", null)]
	[InlineData("mssql", "none", "Delete")]
	public async Task Grants_BasicDataPermissions_IncludeSequenceValuesWithoutTableWriteExpansionAsync(string provider, string permission, string privilege)
	{
		using var directory = new MigrationTestDirectory();
		var database = Database(provider);
		database.Users[0].Permission = permission;
		database.Users[0].Privileges = privilege == null ? [] : [privilege];
		var commands = new List<string>();
		string[][] Query(string sql) => sql.Contains("pg_namespace", StringComparison.Ordinal) ? [["public"]] :
			sql.Contains("sys.sequences", StringComparison.Ordinal) ? [["dbo", "number"]] : [];
		Migrator.Database driver = provider == "postgres" ? new Migrator.Database.Postgres((_, _) => new RecordingConnection(commands, Query)) : new Migrator.Database.MsSql((_, _) => new RecordingConnection(commands, Query));

		await driver.GrantAsync(database, new(directory.Path, Path.Combine(directory.Path, "state")), TestContext.Current.CancellationToken);

		Assert.Contains(provider == "postgres" ? "GRANT USAGE, SELECT ON ALL SEQUENCES IN SCHEMA \"public\" TO \"application\"" : "GRANT UPDATE ON OBJECT::[dbo].[number] TO [application]", commands);
		Assert.DoesNotContain(commands, sql => sql.StartsWith("GRANT UPDATE ON DATABASE", StringComparison.Ordinal) || sql.Contains("GRANT SELECT, INSERT, UPDATE", StringComparison.Ordinal));
		if(permission == "readonly")
			Assert.DoesNotContain(commands, sql => sql.StartsWith("GRANT EXECUTE", StringComparison.Ordinal));
	}

	[Theory]
	[InlineData("CreateTable", "CREATE TABLE")]
	[InlineData("CreateView", "CREATE VIEW")]
	[InlineData("CreateProcedure", "CREATE PROCEDURE")]
	[InlineData("CreateFunction", "CREATE FUNCTION")]
	[InlineData("CreateIndex", null)]
	[InlineData("AlterTable", null)]
	[InlineData("AlterIndex", null)]
	[InlineData("AlterView", null)]
	[InlineData("AlterProcedure", null)]
	[InlineData("AlterFunction", null)]
	[InlineData("DropTable", null)]
	[InlineData("DropIndex", null)]
	[InlineData("DropView", null)]
	[InlineData("DropProcedure", null)]
	[InlineData("DropFunction", null)]
	public async Task MsSql_DdlCapabilities_GrantCreationAndSchemaDependenciesOnlyInTargetDatabaseAsync(string privilege, string creation)
	{
		using var directory = new MigrationTestDirectory();
		var database = Database("mssql");
		database.Users[0].Permission = "none";
		database.Users[0].Privileges = [privilege];
		var commands = new List<string>();
		var driver = new Migrator.Database.MsSql((_, name) =>
		{
			Assert.Equal("hosting", name);
			return new RecordingConnection(commands, sql => sql.Contains("FROM sys.schemas", StringComparison.Ordinal) ? [["dbo"], ["sales"]] : []);
		});
		await driver.GrantAsync(database, new(directory.Path, Path.Combine(directory.Path, "state")), TestContext.Current.CancellationToken);

		Assert.Contains("GRANT ALTER, VIEW DEFINITION ON SCHEMA::[dbo] TO [application]", commands);
		Assert.Contains("GRANT ALTER, VIEW DEFINITION ON SCHEMA::[sales] TO [application]", commands);
		if(creation != null)
			Assert.Contains("GRANT " + creation + " ON DATABASE::[hosting] TO [application]", commands);
		Assert.DoesNotContain(commands, sql => sql.Contains("SERVER", StringComparison.Ordinal) || sql.Contains("db_owner", StringComparison.Ordinal));
	}

	[Theory]
	[InlineData("CreateTable")]
	[InlineData("CreateIndex")]
	public async Task Postgres_CreateTableOrIndex_IncludesSchemaAndSequenceCreationAsync(string privilege)
	{
		using var directory = new MigrationTestDirectory();
		var database = Database("postgres");
		database.Users[0].Permission = "none";
		database.Users[0].Privileges = [privilege];
		var commands = new List<string>();
		var driver = new Migrator.Database.Postgres((_, _) => new RecordingConnection(commands, sql => sql.StartsWith("SELECT nspname", StringComparison.Ordinal) ? [["public"]] : []));

		await driver.GrantAsync(database, new(directory.Path, Path.Combine(directory.Path, "state")), TestContext.Current.CancellationToken);

		Assert.Contains("GRANT USAGE, CREATE ON SCHEMA \"public\" TO \"application\"", commands);
		Assert.Contains("GRANT USAGE, SELECT ON ALL SEQUENCES IN SCHEMA \"public\" TO \"application\"", commands);
		Assert.DoesNotContain(commands, sql => sql.Contains("OWNER TO", StringComparison.Ordinal) || sql.Contains("SUPERUSER", StringComparison.Ordinal));
	}

	[Theory]
	[InlineData("CreateIndex")]
	[InlineData("AlterTable")]
	[InlineData("AlterIndex")]
	[InlineData("AlterView")]
	[InlineData("AlterProcedure")]
	[InlineData("AlterFunction")]
	[InlineData("DropTable")]
	[InlineData("DropIndex")]
	[InlineData("DropView")]
	[InlineData("DropProcedure")]
	[InlineData("DropFunction")]
	public async Task Postgres_MissingObjectOwnership_ReportsUnsupportedWithoutEscalationAsync(string privilege)
	{
		using var directory = new MigrationTestDirectory();
		var database = Database("postgres");
		database.Users[0].Permission = "none";
		database.Users[0].Privileges = [privilege];
		var commands = new List<string>();
		var driver = new Migrator.Database.Postgres((_, _) => new RecordingConnection(commands, sql => sql.Contains("NOT pg_has_role", StringComparison.Ordinal) ? [["42"]] : []));

		var error = await Assert.ThrowsAsync<MigrationPrivilegeException>(() => driver.GrantAsync(database, new(directory.Path, Path.Combine(directory.Path, "state")), TestContext.Current.CancellationToken));

		Assert.Equal(privilege, error.Privilege);
		Assert.Equal("ObjectOwnershipRequired", error.Reason);
		Assert.DoesNotContain(commands, sql => sql.StartsWith("GRANT", StringComparison.Ordinal) || sql.StartsWith("ALTER", StringComparison.Ordinal));
	}

	[Theory]
	[InlineData("mysql")]
	[InlineData("postgres")]
	public async Task Roles_CrossDatabasePermissions_RejectsBeforeGrantingMembershipAsync(string provider)
	{
		using var directory = new MigrationTestDirectory();
		var database = Database(provider);
		database.Users[0].Roles = ["outside"];
		var commands = new List<string>();
		Migrator.Database driver = provider == "mysql" ? new Migrator.Database.MySql((_, _) => new RecordingConnection(commands, sql => sql.StartsWith("SHOW GRANTS", StringComparison.Ordinal) ? [["GRANT SELECT ON `other`.* TO 'outside'@'%'"]] : [])) :
			new Migrator.Database.Postgres((_, _) => new RecordingConnection(commands, sql => sql.StartsWith("WITH RECURSIVE", StringComparison.Ordinal) ? [["unsafe"]] : []));

		var error = await Assert.ThrowsAsync<MigrationPrivilegeException>(() => driver.GrantAsync(database, new(directory.Path, Path.Combine(directory.Path, "state")), TestContext.Current.CancellationToken));

		Assert.Equal("Roles", error.Privilege);
		Assert.Equal("UnverifiedRoleScope", error.Reason);
		Assert.DoesNotContain(commands, sql => sql.StartsWith("GRANT", StringComparison.Ordinal));
	}

	[Fact]
	public async Task Postgres_ScopedOwnerRole_EnablesDdlWithoutOwnershipTransferAsync()
	{
		using var directory = new MigrationTestDirectory();
		var database = Database("postgres");
		database.Users[0].Permission = "admin";
		database.Users[0].Roles = ["hosting_owner"];
		var commands = new List<string>();
		var joined = false;
		var driver = new Migrator.Database.Postgres((_, _) => new RecordingConnection(commands, sql =>
		{
			if(sql.StartsWith("WITH RECURSIVE", StringComparison.Ordinal))
			{
				Assert.Contains("pg_shdepend", sql);
				Assert.Contains("current_database()", sql);
				return [["safe"]];
			}

			if(sql.Contains("NOT pg_has_role", StringComparison.Ordinal))
				return joined ? [] : [["42"]];
			return sql.StartsWith("SELECT nspname", StringComparison.Ordinal) ? [["public"]] : [];
		}, sql => joined |= sql == "GRANT \"hosting_owner\" TO \"application\""));

		await driver.GrantAsync(database, new(directory.Path, Path.Combine(directory.Path, "state")), TestContext.Current.CancellationToken);

		Assert.True(joined);
		Assert.Contains("GRANT EXECUTE ON ALL ROUTINES IN SCHEMA \"public\" TO \"application\"", commands);
		Assert.Contains("ALTER DEFAULT PRIVILEGES FOR ROLE \"postgres\" IN SCHEMA \"public\" GRANT EXECUTE ON FUNCTIONS TO \"application\"", commands);
		Assert.DoesNotContain(commands, sql => sql.Contains("OWNER TO", StringComparison.Ordinal) || sql.Contains("ALL PRIVILEGES", StringComparison.Ordinal));
	}

	[Theory]
	[InlineData("GRANT SELECT ON `tenant\\_one`.* TO 'scoped'@'%'", true)]
	[InlineData("GRANT SELECT ON `tenant_one`.* TO 'scoped'@'%'", false)]
	[InlineData("GRANT SELECT ON `tenant_one`.`records` TO 'scoped'@'%'", true)]
	[InlineData("GRANT EXECUTE ON PROCEDURE `tenant_one`.`answer` TO 'scoped'@'%'", true)]
	[InlineData("GRANT SELECT ON *.* TO 'scoped'@'%'", false)]
	[InlineData("GRANT 'nested'@'%' TO 'scoped'@'%'", false)]
	public async Task MySql_RoleScope_DistinguishesLiteralObjectsFromDatabaseWildcardsAsync(string grant, bool supported)
	{
		using var directory = new MigrationTestDirectory();
		var database = Database("mysql");
		database.Name = "tenant_one";
		database.Users[0].Permission = "none";
		database.Users[0].Roles = ["scoped"];
		var commands = new List<string>();
		var driver = new Migrator.Database.MySql((_, _) => new RecordingConnection(commands, sql => sql.StartsWith("SHOW GRANTS", StringComparison.Ordinal) ? [[grant]] : []));
		var operation = driver.GrantAsync(database, new(directory.Path, Path.Combine(directory.Path, "state")), TestContext.Current.CancellationToken);

		if(supported)
		{
			await operation;
			Assert.Contains("GRANT 'scoped'@'%' TO 'application'@'%'", commands);
		}
		else
		{
			await Assert.ThrowsAsync<MigrationPrivilegeException>(() => operation);
			Assert.DoesNotContain(commands, sql => sql.StartsWith("GRANT", StringComparison.Ordinal));
		}
	}

	[Fact]
	public async Task Apply_UnsupportedOwnership_ClearsReadyAndReportsSafeCapabilityContextAsync()
	{
		using var directory = new MigrationTestDirectory();
		var database = Database("postgres");
		database.Users[0].Privileges = ["AlterTable"];
		var plan = new MigrationPlan { Name = "test", Version = "1.0.0", Runtime = "linux-x64", Databases = [database], Steps = [new() { Provider = "postgres", DatabaseIndex = 0 }] };
		var commands = new List<string>();
		var driver = new Migrator.Database.Postgres((_, _) => new RecordingConnection(commands, sql =>
			sql.Contains("pg_database", StringComparison.Ordinal) ? [[database.Name]] : sql == "SELECT rolname FROM pg_roles" ? [["application"]] : sql.Contains("NOT pg_has_role", StringComparison.Ordinal) ? [["42"]] : []));
		var context = new MigrationContext(directory.Path, Path.Combine(directory.Path, "state"));
		directory.Write("state/ready", "previous");

		var error = await Assert.ThrowsAsync<MigrationException>(() => new MigrationExecutor(_ => driver).ApplyAsync(plan, context, TestContext.Current.CancellationToken));

		Assert.False(File.Exists(Path.Combine(context.StateDirectory, "ready")));
		using var status = JsonDocument.Parse(File.ReadAllText(Path.Combine(context.StateDirectory, "status.json")));
		Assert.Equal("permissions", status.RootElement.GetProperty("phase").GetString());
		Assert.Contains("AlterTable", error.Message);
		Assert.Contains("ObjectOwnershipRequired", status.RootElement.GetProperty("error").GetString());
		Assert.DoesNotContain("administrator-secret", error.Message + status.RootElement.GetRawText());
		Assert.DoesNotContain("initial-password", error.Message + status.RootElement.GetRawText());
	}
}
