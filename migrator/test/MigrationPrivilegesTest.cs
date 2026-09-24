using System;
using System.IO;
using System.Linq;

using Xunit;

using Zongsoft.Tools.Migrator.Migration;

namespace Zongsoft.Tools.Migrator.Tests;

public sealed class MigrationPrivilegesTest
{
	[Theory]
	[InlineData("mysql")]
	[InlineData("mssql")]
	[InlineData("postgres")]
	public void Load_UnifiedPrivileges_NormalizesMergesPresetAndFingerprintsEffectiveCapabilities(string provider)
	{
		using var directory = new MigrationTestDirectory();
		directory.Write("main.migration", $"[{provider}]\n");
		var path = directory.Write("main.ini", $"[{provider}]\nServer=localhost\nDatabase=sample\nPassword=root\n[{provider} sample app]\nPassword=secret\nPermission=ReadWrite\nPrivileges=createTABLE|SELECT,createfunction,CreateProcedure,createtable\n");
		var plan = new MigrationLoader(null).Load("main.migration", directory.Path, "test", "1.0.0");
		var user = Assert.Single(Assert.Single(plan.Databases).Users);

		Assert.Equal(new[] { "Select", "Insert", "Update", "Delete", "Execute", "CreateTable", "CreateProcedure", "CreateFunction" }, user.Privileges);
		var fingerprint = plan.Fingerprint();
		plan.Validate();
		Assert.Equal(fingerprint, plan.Fingerprint());
		File.WriteAllText(path, File.ReadAllText(path).Replace("Permission=ReadWrite", "Permission=None", StringComparison.Ordinal));
		Assert.NotEqual(fingerprint, new MigrationLoader(null).Load("main.migration", directory.Path, "test", "1.0.0").Fingerprint());
	}

	[Theory]
	[InlineData("None", 0)]
	[InlineData("ReadOnly", 1)]
	[InlineData("ReadWrite", 5)]
	[InlineData("Admin", 20)]
	public void Prepare_PermissionPresets_ExpandExactlyTheUnifiedCatalog(string permission, int count)
	{
		var database = Database("mysql", permission);
		MigrationProvider.Get("mysql").Prepare(database);
		var privileges = Assert.Single(database.Users).Privileges;
		Assert.Equal(count, privileges.Length);
		Assert.Equal(count, privileges.Distinct(StringComparer.OrdinalIgnoreCase).Count());
		if(permission == "Admin")
			Assert.Equal(new[] { "Select", "Insert", "Update", "Delete", "Execute", "CreateTable", "CreateIndex", "CreateView", "CreateProcedure", "CreateFunction", "AlterTable", "AlterIndex", "AlterView", "AlterProcedure", "AlterFunction", "DropTable", "DropIndex", "DropView", "DropProcedure", "DropFunction" }, privileges);
	}

	[Theory]
	[InlineData("CREATE")]
	[InlineData("CREATE TABLE")]
	[InlineData("ALL PRIVILEGES")]
	[InlineData("SELECT; DROP USER app")]
	public void Prepare_NativeOrSqlPrivileges_RejectsWithoutCredentialLeak(string privilege)
	{
		var database = Database("mysql", "none");
		database.Users[0].Privileges = [privilege];
		var error = Assert.Throws<InvalidDataException>(() => MigrationProvider.Get("mysql").Prepare(database));
		Assert.DoesNotContain("administrator-secret", error.Message);
		Assert.DoesNotContain("user-secret", error.Message);
	}

	[Fact]
	public void Prepare_TDengineReadWrite_ReportsUnsupportedUnifiedOperation()
	{
		var database = Database("tdengine", "readwrite");
		var error = Assert.Throws<MigrationPrivilegeException>(() => MigrationProvider.Get("tdengine").Prepare(database));
		Assert.IsAssignableFrom<NotSupportedException>(error);
		Assert.Equal("Update", error.Privilege);
		Assert.Equal("tdengine", error.Provider);
		Assert.Equal("UnsupportedOperation", error.Reason);
	}

	[Fact]
	public void Load_NativePrivilegesParameter_IsNotExposed()
	{
		using var directory = new MigrationTestDirectory();
		directory.Write("main.migration", "[mysql]\n");
		directory.Write("main.ini", "[mysql]\nServer=localhost\nDatabase=sample\nPassword=root\n[mysql sample app]\nPassword=secret\nNativePrivileges=CREATE\n");
		Assert.Throws<InvalidDataException>(() => new MigrationLoader(null).Load("main.migration", directory.Path, "test", "1.0.0"));
	}

	[Theory]
	[InlineData("")]
	[InlineData("script.sql")]
	public void Load_UnsupportedOperation_WithOrWithoutScripts_PreservesNotSupportedException(string script)
	{
		using var directory = new MigrationTestDirectory();
		directory.Write("main.migration", "[tdengine]\n" + script + "\n");
		directory.Write("script.sql", "SELECT 1;");
		directory.Write("main.ini", "[tdengine]\nServer=localhost\nDatabase=sample\nPassword=root\n[tdengine sample app]\nPassword=secret\nPermission=ReadWrite\n");
		var error = Assert.Throws<MigrationPrivilegeException>(() => new MigrationLoader(null).Load("main.migration", directory.Path, "test", "1.0.0"));
		Assert.Equal("Update", error.Privilege);
		Assert.Equal("UnsupportedOperation", error.Reason);
	}

	private static MigrationPlan.Database Database(string provider, string permission) => new()
	{
		Provider = provider,
		Name = "sample",
		Settings = new() { ["Server"] = "localhost", ["Password"] = "administrator-secret" },
		Users = [new() { Name = "app", Password = "user-secret", Permission = permission }],
	};
}
