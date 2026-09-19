using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;

using Xunit;

using Zongsoft.Tools.Migrator.Migration;

namespace Zongsoft.Tools.Migrator.Tests;

public sealed class MigrationImportTest
{
	#region 测试方法
	[Fact]
	public void Load_NestedImports_UsesEachSqlOriginAndItsOwnParameters()
	{
		using var directory = new MigrationTestDirectory();
		directory.Write("main.migration", "#@import child/part.migration\n[sqlite]\n./root.sql\n");
		directory.Write("child/part.migration", "#@import deep/leaf.migration\n[sqlite]\n./child.sql\n");
		directory.Write("child/deep/leaf.migration", "[sqlite]\n./leaf.sql\n");
		directory.Write("main.env", "[sqlite]\nDatabase=/data/root.db\n");
		directory.Write("child/part.env", "[sqlite]\nDatabase=/data/child.db\n");
		directory.Write("child/deep/leaf.env", "[sqlite]\nDatabase=/data/leaf.db\n");
		var root = directory.Write("root.sql", "SELECT 'root';");
		var child = directory.Write("child/child.sql", "SELECT 'child';");
		var leaf = directory.Write("child/deep/leaf.sql", "SELECT 'leaf';");

		var plan = Loader().Load("main.migration", directory.Path, "test", "1.0.0");

		Assert.Equal(new[] { "/data/leaf.db", "/data/child.db", "/data/root.db" }, plan.Tasks.Select(task => task.Parameters["Database"]));
		Assert.Equal(new[] { leaf, child, root }, plan.Tasks.Select(task => Assert.Single(task.Scripts).Source));
		Assert.Equal(new[] { "0001-sqlite", "0002-sqlite", "0003-sqlite" }, plan.Tasks.Select(task => task.Id));
		Assert.Equal(new[] { ".migration/.artifacts/sqlite/0001.sql", ".migration/.artifacts/sqlite/0002.sql", ".migration/.artifacts/sqlite/0003.sql" }, plan.Tasks.SelectMany(task => task.Scripts).Select(script => script.Path));
	}

	[Fact]
	public void Load_InterleavedOrigins_PreservesEffectiveEntryOrder()
	{
		using var directory = new MigrationTestDirectory();
		directory.Write("main.migration", "[sqlite]\n./first*.sql\n./shared.sql\n./first.sql\n./last.sql\n#@import child/part.migration\n");
		directory.Write("child/part.migration", "[sqlite]\n./shared.sql\n");
		directory.Write("main.env", "[sqlite]\nDatabase=/data/root.db\n");
		directory.Write("child/part.env", "[sqlite]\nDatabase=/data/child.db\n");
		var first = directory.Write("first.sql", "SELECT 'first';");
		var second = directory.Write("first2.sql", "SELECT 'second';");
		var shared = directory.Write("child/shared.sql", "SELECT 'child';");
		var last = directory.Write("last.sql", "SELECT 'last';");

		var plan = Loader().Load("main.migration", directory.Path, "test", "1.0.0");

		Assert.Equal(new[] { "/data/root.db", "/data/child.db", "/data/root.db" }, plan.Tasks.Select(task => task.Parameters["Database"]));
		Assert.Equal(new[] { first, second, shared, last }, plan.Tasks.SelectMany(task => task.Scripts).Select(script => script.Source));
		Assert.Equal(new[] { 2, 1, 1 }, plan.Tasks.Select(task => task.Scripts.Count));
		Assert.Equal(new[] { ".migration/.artifacts/sqlite/0001.sql", ".migration/.artifacts/sqlite/0002.sql", ".migration/.artifacts/sqlite/0003.sql", ".migration/.artifacts/sqlite/0004.sql" }, plan.Tasks.SelectMany(task => task.Scripts).Select(script => script.Path));
	}

	[Theory]
	[InlineData(true)]
	[InlineData(false)]
	public void Load_ImportedAndLocalSameKey_UsesLastDeclarationSource(bool importFirst)
	{
		using var directory = new MigrationTestDirectory();
		var directive = "#@import child/part.migration\n";
		var declaration = "[sqlite]\n./schema.sql\n";
		directory.Write("main.migration", importFirst ? directive + declaration : declaration + directive);
		directory.Write("child/part.migration", declaration);
		directory.Write("main.env", "[sqlite]\nDatabase=/data/root.db\n");
		directory.Write("child/part.env", "[sqlite]\nDatabase=/data/child.db\n");
		var selected = directory.Write(importFirst ? "schema.sql" : "child/schema.sql", "SELECT 'winner';");

		var task = Assert.Single(Loader().Load("main.migration", directory.Path, "test", "1.0.0").Tasks);

		Assert.Equal(importFirst ? "/data/root.db" : "/data/child.db", task.Parameters["Database"]);
		Assert.Equal(selected, Assert.Single(task.Scripts).Source);
		Assert.Equal("SELECT 'winner';", task.Scripts[0].Content);
	}

	[Fact]
	public void Load_ParameterImports_ComposeSelectedCandidateWithoutOtherCandidates()
	{
		using var directory = new MigrationTestDirectory();
		directory.Write("main.migration", "[postgres]\n./schema.sql\n");
		directory.Write("main.env", "#@import settings/defaults.env\n[postgres]\nDatabase=selected\n");
		directory.Write("settings/defaults.env", "#@import deep/base.env\n[postgres]\nDatabase=original\nUserName=operator\n");
		directory.Write("settings/deep/base.env", "[postgres]\nServer=nested-host\nPassword=secret=punctuation\n");
		directory.Write("postgres.env", "Server=wrong-host\nDatabase=wrong\nUserName=wrong\nCommandTimeout=9\n");
		directory.Write("schema.sql", "SELECT 1;");

		var task = Assert.Single(Loader().Load("main.migration", directory.Path, "test", "1.0.0").Tasks);

		Assert.Equal("nested-host", task.Parameters["Server"]);
		Assert.Equal("selected", task.Parameters["Database"]);
		Assert.Equal("operator", task.Parameters["UserName"]);
		Assert.Equal("secret=punctuation", task.Parameters["Password"]);
		Assert.False(task.Parameters.ContainsKey("CommandTimeout"));
		Assert.Single(task.Scripts);
	}

	[Theory]
	[InlineData("[sqlite]\n[SQLITE]\n")]
	[InlineData("[invalid section]\n")]
	[InlineData("[sqlite]\n./same.sql\n./SAME.SQL\n")]
	[InlineData("[unknown]\n")]
	[InlineData("unscoped.sql\n")]
	public void Load_InvalidImportedProfile_ReportsChildPath(string content)
	{
		using var directory = new MigrationTestDirectory();
		directory.Write("main.migration", "#@import child/invalid.migration\n[sqlite]\n./local.sql\n");
		var child = directory.Write("child/invalid.migration", content);
		directory.Write("sqlite.env", "Database=/data/test.db\n");
		directory.Write("local.sql", "SELECT 1;");

		var error = Assert.Throws<InvalidDataException>(() => Loader().Load("main.migration", directory.Path, "test", "1.0.0"));

		Assert.Contains(child, error.Message);
	}

	[Fact]
	public void Load_DuplicateImportedParameterKey_ReportsChildWithoutValues()
	{
		using var directory = new MigrationTestDirectory();
		directory.Write("main.migration", "[sqlite]\n./schema.sql\n");
		directory.Write("sqlite.env", "#@import settings/duplicate.env\nDatabase=/data/local.db\n");
		var child = directory.Write("settings/duplicate.env", "Database=private-first\nDATABASE=private-second\n");
		directory.Write("schema.sql", "SELECT 1;");

		var error = Assert.Throws<InvalidDataException>(() => Loader().Load("main.migration", directory.Path, "test", "1.0.0"));

		Assert.Contains(child, error.Message);
		Assert.DoesNotContain("private-first", error.ToString());
		Assert.DoesNotContain("private-second", error.ToString());
	}

	[Fact]
	public void Load_ImportedProviderAliases_RemainAmbiguous()
	{
		using var directory = new MigrationTestDirectory();
		var main = directory.Write("main.migration", "#@import child/part.migration\n[postgres]\n./root.sql\n");
		directory.Write("child/part.migration", "[postgresql]\n./child.sql\n");

		var error = Assert.Throws<InvalidDataException>(() => Loader().Load("main.migration", directory.Path, "test", "1.0.0"));

		Assert.Contains(main, error.Message);
		Assert.Contains("postgres", error.Message);
	}

	[Theory]
	[InlineData(false)]
	[InlineData(true)]
	public void Load_ImportedLink_UsesLogicalSqlAndParameterPaths(bool directoryLink)
	{
		using var directory = new MigrationTestDirectory();
		var source = directory.Write("original/part.migration", "[sqlite]\n./schema.sql\n");
		directory.Write("original/sqlite.env", "Database=/data/target.db\n");
		directory.Write("original/schema.sql", "SELECT 'target';");
		directory.Write("sqlite.env", "Database=/data/logical.db\n");
		directory.Write("schema.sql", "SELECT 'logical';");
		var link = Path.Combine(directory.Path, directoryLink ? "linked" : "linked.migration");
		if(directoryLink)
			Directory.CreateSymbolicLink(link, Path.GetDirectoryName(source));
		else
			File.CreateSymbolicLink(link, source);
		directory.Write("main.migration", "#@import " + (directoryLink ? "linked/part.migration" : "linked.migration") + "\n");

		var task = Assert.Single(Loader().Load("main.migration", directory.Path, "test", "1.0.0").Tasks);

		Assert.Equal(directoryLink ? "/data/target.db" : "/data/logical.db", task.Parameters["Database"]);
		var script = Assert.Single(task.Scripts);
		Assert.Equal(Path.GetFullPath(Path.Combine(directory.Path, directoryLink ? "linked/schema.sql" : "schema.sql")), script.Source);
		Assert.Equal(directoryLink ? "SELECT 'target';" : "SELECT 'logical';", script.Content);
		Assert.Equal("[sqlite]\r\n./schema.sql\r\n", File.ReadAllText(source));
	}

	[Fact]
	public void Load_LinkedRootIniAndSql_UseLogicalNamesAndSiblingParameters()
	{
		using var directory = new MigrationTestDirectory();
		var ini = directory.Write("physical/config.data", "#@import extra.migration\n[sqlite]\n./script.sql\n");
		directory.Write("physical/extra.migration", "[unknown]\n");
		directory.Write("physical/main.env", "[sqlite]\nDatabase=/data/wrong.db\n");
		directory.Write("logical/extra.migration", "[sqlite]\n./extra.sql\n");
		directory.Write("logical/extra.env", "[sqlite]\nDatabase=/data/import.db\n");
		directory.Write("logical/main.env", "[sqlite]\nDatabase=/data/main.db\n");
		directory.Write("logical/extra.sql", "SELECT 'import';");
		var sql = directory.Write("physical/statement.data", "SELECT 'linked';");
		File.CreateSymbolicLink(Path.Combine(directory.Path, "logical/main.migration"), ini);
		var script = Path.GetFullPath(Path.Combine(directory.Path, "logical/script.sql"));
		File.CreateSymbolicLink(script, sql);

		var tasks = Loader().Load("logical/main.migration", directory.Path, "test", "1.0.0").Tasks;

		Assert.Equal(new[] { "/data/import.db", "/data/main.db" }, tasks.Select(task => task.Parameters["Database"]));
		Assert.Equal(new[] { "SELECT 'import';", "SELECT 'linked';" }, tasks.SelectMany(task => task.Scripts).Select(item => item.Content));
		Assert.Equal(script, Assert.Single(tasks[1].Scripts).Source);
	}

	[Fact]
	public void Load_ImportedSqlFailure_ReportsChildEntry()
	{
		using var directory = new MigrationTestDirectory();
		directory.Write("main.migration", "#@import child/part.migration\n");
		var child = directory.Write("child/part.migration", "# child\n[sqlite]\n./missing.sql\n");
		directory.Write("child/sqlite.env", "Database=/data/child.db\n");

		var error = Assert.Throws<InvalidDataException>(() => Loader().Load("main.migration", directory.Path, "test", "1.0.0"));

		Assert.Contains(child + ":3", error.Message);
		Assert.Contains("sqlite", error.Message);
	}

	[Fact]
	public void Load_ImportedParameterExpansionFailure_ReportsSourceAndNoSecret()
	{
		using var directory = new MigrationTestDirectory();
		directory.Write("main.migration", "[postgres]\n./schema.sql\n");
		directory.Write("postgres.env", "Server=localhost\nDatabase=test\nUserName=operator\n#@import settings/secret.env\n");
		var child = directory.Write("settings/secret.env", "# secret source\nPassword=private-prefix-$(missing)\n");
		directory.Write("schema.sql", "SELECT 1;");

		var error = Assert.Throws<InvalidDataException>(() => Loader().Load("main.migration", directory.Path, "test", "1.0.0"));

		Assert.Contains(child + ":2", error.Message);
		Assert.Contains("Password", error.Message);
		Assert.DoesNotContain("private-prefix", error.ToString());
	}

	[Fact]
	public void Load_ImportCycle_FailsAndSameLoaderCanRetry()
	{
		using var directory = new MigrationTestDirectory();
		directory.Write("main.migration", "#@import child/part.migration\n");
		var child = directory.Write("child/part.migration", "#@import ../main.migration\n");
		var loader = Loader();

		var error = Assert.Throws<InvalidDataException>(() => loader.Load("main.migration", directory.Path, "test", "1.0.0"));

		Assert.Contains(child, error.Message);
		directory.Write("child/part.migration", "[sqlite]\n./schema.sql\n");
		directory.Write("child/sqlite.env", "Database=/data/child.db\n");
		var sql = directory.Write("child/schema.sql", "SELECT 'retry';");
		var task = Assert.Single(loader.Load("main.migration", directory.Path, "test", "1.0.0").Tasks);
		Assert.Equal(sql, Assert.Single(task.Scripts).Source);
		Assert.Equal(".migration/.artifacts/sqlite/0001.sql", task.Scripts[0].Path);
	}

	[Fact]
	public void Load_ParameterImportCycle_ReportsParameterSource()
	{
		using var directory = new MigrationTestDirectory();
		directory.Write("main.migration", "[sqlite]\n./schema.sql\n");
		directory.Write("sqlite.env", "#@import settings/child.env\nDatabase=/data/test.db\n");
		var child = directory.Write("settings/child.env", "#@import ../sqlite.env\n");
		directory.Write("schema.sql", "SELECT 1;");

		var error = Assert.Throws<InvalidDataException>(() => Loader().Load("main.migration", directory.Path, "test", "1.0.0"));

		Assert.Contains(child, error.Message);
	}

	[Fact]
	public void Load_MissingOptionalImport_LeavesLocalTask()
	{
		using var directory = new MigrationTestDirectory();
		directory.Write("main.migration", "#@import missing/child.migration\n[sqlite]\n./schema.sql\n");
		directory.Write("sqlite.env", "#@import absent.env\nDatabase=/data/local.db\n");
		var sql = directory.Write("schema.sql", "SELECT 1;");
		var warnings = new List<string>();

		var task = Assert.Single(Loader(warnings.Add).Load("main.migration", directory.Path, "test", "1.0.0").Tasks);

		Assert.Equal(sql, Assert.Single(task.Scripts).Source);
		Assert.Equal("/data/local.db", task.Parameters["Database"]);
		Assert.Empty(warnings);
	}

	[Fact]
	public void Load_ImportedBuckets_UseSourceSpecificParameters()
	{
		using var directory = new MigrationTestDirectory();
		directory.Write("main.migration", "#@import child/buckets.migration\n[amazon.s3]\nroot-bucket=private\n");
		directory.Write("child/buckets.migration", "[amazon.s3]\nchild-bucket=public\n");
		directory.Write("main.env", "[amazon.s3]\nServer=http://root.invalid:9000\nRegion=us-east-1\nAccessKey=root\nSecretKey=sample\n");
		directory.Write("child/buckets.env", "[amazon.s3]\nServer=http://child.invalid:9000\nRegion=us-east-1\nAccessKey=child\nSecretKey=sample\n");

		var plan = Loader().Load("main.migration", directory.Path, "test", "1.0.0");

		Assert.Equal(new[] { "http://child.invalid:9000", "http://root.invalid:9000" }, plan.Tasks.Select(task => task.Parameters["Server"]));
		Assert.Equal(new[] { "child-bucket", "root-bucket" }, plan.Tasks.Select(task => Assert.Single(task.Buckets).Name));
		Assert.True(plan.Tasks[0].Buckets[0].Public);
		Assert.False(plan.Tasks[1].Buckets[0].Public);
		Assert.All(plan.Tasks, task => Assert.Empty(task.Scripts));
	}

	[Theory]
	[InlineData(64)]
	[InlineData(65)]
	public void Load_DefaultImportDepth_Enforces64Files(int depth)
	{
		using var directory = new MigrationTestDirectory();
		for(var index = 0; index < depth; index++)
			directory.Write($"part-{index}.migration", index + 1 < depth ? $"#@import part-{index + 1}.migration\n" : "[sqlite]\n./schema.sql\n");
		directory.Write("sqlite.env", "Database=/data/test.db\n");
		var sql = directory.Write("schema.sql", "SELECT 'leaf';");

		if(depth == 64)
		{
			var task = Assert.Single(Loader().Load("part-0.migration", directory.Path, "test", "1.0.0").Tasks);
			Assert.Equal(sql, Assert.Single(task.Scripts).Source);
			Assert.Equal("SELECT 'leaf';", task.Scripts[0].Content);
		}
		else
		{
			var error = Assert.Throws<InvalidDataException>(() => Loader().Load("part-0.migration", directory.Path, "test", "1.0.0"));
			Assert.Contains(Path.Combine(directory.Path, "part-63.migration"), error.Message);
		}
	}
	#endregion

	#region 辅助方法
	private static MigrationLoader Loader(Action<string> warning = null) => new(value =>
	{
		var result = Normalizer.Normalize(value, new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase));
		if(!result.Succeed)
			throw new InvalidDataException("Undefined variable: " + result.Value);

		return result.Value;
	}, warning);
	#endregion
}
