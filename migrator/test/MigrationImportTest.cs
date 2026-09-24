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
		directory.Write("main.ini", "[sqlite]\nDatabase=/data/root.db\n");
		directory.Write("child/part.ini", "[sqlite]\nDatabase=/data/child.db\n");
		directory.Write("child/deep/leaf.ini", "[sqlite]\nDatabase=/data/leaf.db\n");
		var root = directory.Write("root.sql", "SELECT 'root';");
		var child = directory.Write("child/child.sql", "SELECT 'child';");
		var leaf = directory.Write("child/deep/leaf.sql", "SELECT 'leaf';");

		var plan = Loader().Load("main.migration", directory.Path, "test", "1.0.0");

		Assert.Equal(new[] { "/data/leaf.db", "/data/child.db", "/data/root.db" }, plan.Steps.Select(task => plan.Databases[task.DatabaseIndex.Value].Name));
		Assert.Equal(new[] { leaf, child, root }, plan.Steps.Select(task => Assert.Single(task.Scripts).Source));

		Assert.Equal(new[] { ".migration/.artifacts/sqlite/1.sql", ".migration/.artifacts/sqlite/2.sql", ".migration/.artifacts/sqlite/3.sql" }, plan.Steps.SelectMany(task => task.Scripts).Select(script => script.Path));
	}

	[Fact]
	public void Load_InterleavedOrigins_PreservesEffectiveEntryOrder()
	{
		using var directory = new MigrationTestDirectory();
		directory.Write("main.migration", "[sqlite]\n./first*.sql\n./shared.sql\n./first.sql\n./last.sql\n#@import child/part.migration\n");
		directory.Write("child/part.migration", "[sqlite]\n./shared.sql\n");
		directory.Write("main.ini", "[sqlite]\nDatabase=/data/root.db\n");
		directory.Write("child/part.ini", "[sqlite]\nDatabase=/data/child.db\n");
		var first = directory.Write("first.sql", "SELECT 'first';");
		var second = directory.Write("first2.sql", "SELECT 'second';");
		var shared = directory.Write("child/shared.sql", "SELECT 'child';");
		var last = directory.Write("last.sql", "SELECT 'last';");

		var plan = Loader().Load("main.migration", directory.Path, "test", "1.0.0");

		Assert.Equal(new[] { "/data/root.db", "/data/child.db", "/data/root.db" }, plan.Steps.Select(task => plan.Databases[task.DatabaseIndex.Value].Name));
		Assert.Equal(new[] { first, second, shared, last }, plan.Steps.SelectMany(task => task.Scripts).Select(script => script.Source));
		Assert.Equal(new[] { 2, 1, 1 }, plan.Steps.Select(task => task.Scripts.Count));
		Assert.Equal(new[] { ".migration/.artifacts/sqlite/1.sql", ".migration/.artifacts/sqlite/2.sql", ".migration/.artifacts/sqlite/3.sql", ".migration/.artifacts/sqlite/4.sql" }, plan.Steps.SelectMany(task => task.Scripts).Select(script => script.Path));
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
		directory.Write("main.ini", "[sqlite]\nDatabase=/data/root.db\n");
		directory.Write("child/part.ini", "[sqlite]\nDatabase=/data/child.db\n");
		var selected = directory.Write(importFirst ? "schema.sql" : "child/schema.sql", "SELECT 'winner';");

		var plan = Loader().Load("main.migration", directory.Path, "test", "1.0.0");
		var task = Assert.Single(plan.Steps);

		Assert.Equal(importFirst ? "/data/root.db" : "/data/child.db", plan.Databases[task.DatabaseIndex.Value].Name);
		Assert.Equal(selected, Assert.Single(task.Scripts).Source);
		Assert.Equal("SELECT 'winner';", task.Scripts[0].Content);
	}

	[Fact]
	public void Load_ParameterImports_ComposeSelectedCandidateWithoutOtherCandidates()
	{
		using var directory = new MigrationTestDirectory();
		directory.Write("main.migration", "[postgres]\n./schema.sql\n");
		directory.Write("main.ini", "#@import settings/defaults.ini\n[postgres]\nDatabase=selected\n");
		directory.Write("settings/defaults.ini", "#@import deep/base.ini\n[postgres]\nDatabase=original\nUserName=operator\n");
		directory.Write("settings/deep/base.ini", "[postgres]\nServer=nested-host\nPassword=secret=punctuation\n");
		directory.Write("postgres.ini", "[postgres]\nServer=wrong-host\nDatabase=wrong\nUserName=wrong\nCommandTimeout=9\n");
		directory.Write("schema.sql", "SELECT 1;");

		var plan = Loader().Load("main.migration", directory.Path, "test", "1.0.0");
		var task = Assert.Single(plan.Steps);

		Assert.Equal("nested-host", plan.Databases[task.DatabaseIndex.Value].Settings["Server"]);
		Assert.Equal("selected", plan.Databases[task.DatabaseIndex.Value].Name);
		Assert.Equal("operator", plan.Databases[task.DatabaseIndex.Value].Settings["UserName"]);
		Assert.Equal("secret=punctuation", plan.Databases[task.DatabaseIndex.Value].Settings["Password"]);
		Assert.Equal("300s", Assert.Single(plan.Databases).Settings["CommandTimeout"]);
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
		directory.Write("sqlite.ini", "[sqlite]\nDatabase=/data/test.db\n");
		directory.Write("local.sql", "SELECT 1;");

		var error = Assert.Throws<InvalidDataException>(() => Loader().Load("main.migration", directory.Path, "test", "1.0.0"));

		Assert.Contains(child, error.Message);
	}

	[Fact]
	public void Load_DuplicateImportedParameterKey_ReportsChildWithoutValues()
	{
		using var directory = new MigrationTestDirectory();
		directory.Write("main.migration", "[sqlite]\n./schema.sql\n");
		directory.Write("sqlite.ini", "#@import settings/duplicate.ini\n[sqlite]\nDatabase=/data/local.db\n");
		var child = directory.Write("settings/duplicate.ini", "[sqlite]\nDatabase=private-first\nDATABASE=private-second\n");
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
		directory.Write("original/sqlite.ini", "[sqlite]\nDatabase=/data/target.db\n");
		directory.Write("original/schema.sql", "SELECT 'target';");
		directory.Write("sqlite.ini", "[sqlite]\nDatabase=/data/logical.db\n");
		directory.Write("schema.sql", "SELECT 'logical';");
		var link = Path.Combine(directory.Path, directoryLink ? "linked" : "linked.migration");
		if(directoryLink)
			Directory.CreateSymbolicLink(link, Path.GetDirectoryName(source));
		else
			File.CreateSymbolicLink(link, source);
		directory.Write("main.migration", "#@import " + (directoryLink ? "linked/part.migration" : "linked.migration") + "\n");

		var plan = Loader().Load("main.migration", directory.Path, "test", "1.0.0");
		var task = Assert.Single(plan.Steps);

		Assert.Equal(directoryLink ? "/data/target.db" : "/data/logical.db", plan.Databases[task.DatabaseIndex.Value].Name);
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
		directory.Write("physical/main.ini", "[sqlite]\nDatabase=/data/wrong.db\n");
		directory.Write("logical/extra.migration", "[sqlite]\n./extra.sql\n");
		directory.Write("logical/extra.ini", "[sqlite]\nDatabase=/data/import.db\n");
		directory.Write("logical/main.ini", "[sqlite]\nDatabase=/data/main.db\n");
		directory.Write("logical/extra.sql", "SELECT 'import';");
		var sql = directory.Write("physical/statement.data", "SELECT 'linked';");
		File.CreateSymbolicLink(Path.Combine(directory.Path, "logical/main.migration"), ini);
		var script = Path.GetFullPath(Path.Combine(directory.Path, "logical/script.sql"));
		File.CreateSymbolicLink(script, sql);

		var plan = Loader().Load("logical/main.migration", directory.Path, "test", "1.0.0");
		var tasks = plan.Steps;

		Assert.Equal(new[] { "/data/import.db", "/data/main.db" }, tasks.Select(task => plan.Databases[task.DatabaseIndex.Value].Name));
		Assert.Equal(new[] { "SELECT 'import';", "SELECT 'linked';" }, tasks.SelectMany(task => task.Scripts).Select(item => item.Content));
		Assert.Equal(script, Assert.Single(tasks[1].Scripts).Source);
	}

	[Fact]
	public void Load_ImportedSqlFailure_ReportsChildEntry()
	{
		using var directory = new MigrationTestDirectory();
		directory.Write("main.migration", "#@import child/part.migration\n");
		var child = directory.Write("child/part.migration", "# child\n[sqlite]\n./missing.sql\n");
		directory.Write("child/sqlite.ini", "[sqlite]\nDatabase=/data/child.db\n");

		var error = Assert.Throws<InvalidDataException>(() => Loader().Load("main.migration", directory.Path, "test", "1.0.0"));

		Assert.Contains(child + ":3", error.Message);
		Assert.Contains("sqlite", error.Message);
	}

	[Fact]
	public void Load_ImportedParameterExpansionFailure_ReportsSourceAndNoSecret()
	{
		using var directory = new MigrationTestDirectory();
		directory.Write("main.migration", "[postgres]\n./schema.sql\n");
		directory.Write("postgres.ini", "[postgres]\nServer=localhost\nDatabase=test\nUserName=operator\n#@import settings/secret.ini\n");
		var child = directory.Write("settings/secret.ini", "[postgres]\nPassword=private-prefix-$(missing)\n");
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
		directory.Write("child/sqlite.ini", "[sqlite]\nDatabase=/data/child.db\n");
		var sql = directory.Write("child/schema.sql", "SELECT 'retry';");
		var task = Assert.Single(loader.Load("main.migration", directory.Path, "test", "1.0.0").Steps);
		Assert.Equal(sql, Assert.Single(task.Scripts).Source);
		Assert.Equal(".migration/.artifacts/sqlite/1.sql", task.Scripts[0].Path);
	}

	[Fact]
	public void Load_ParameterImportCycle_ReportsParameterSource()
	{
		using var directory = new MigrationTestDirectory();
		directory.Write("main.migration", "[sqlite]\n./schema.sql\n");
		directory.Write("sqlite.ini", "#@import settings/child.ini\n[sqlite]\nDatabase=/data/test.db\n");
		var child = directory.Write("settings/child.ini", "#@import ../sqlite.ini\n");
		directory.Write("schema.sql", "SELECT 1;");

		var error = Assert.Throws<InvalidDataException>(() => Loader().Load("main.migration", directory.Path, "test", "1.0.0"));

		Assert.Contains(child, error.Message);
	}

	[Fact]
	public void Load_MissingOptionalImport_LeavesLocalTask()
	{
		using var directory = new MigrationTestDirectory();
		directory.Write("main.migration", "#@import missing/child.migration\n[sqlite]\n./schema.sql\n");
		directory.Write("sqlite.ini", "#@import absent.ini\n[sqlite]\nDatabase=/data/local.db\n");
		var sql = directory.Write("schema.sql", "SELECT 1;");
		var warnings = new List<string>();

		var plan = Loader(warnings.Add).Load("main.migration", directory.Path, "test", "1.0.0");
		var task = Assert.Single(plan.Steps);

		Assert.Equal(sql, Assert.Single(task.Scripts).Source);
		Assert.Equal("/data/local.db", plan.Databases[task.DatabaseIndex.Value].Name);
		Assert.Empty(warnings);
	}

	[Fact]
	public void Load_ImportedBuckets_UseSourceSpecificParameters()
	{
		using var directory = new MigrationTestDirectory();
		directory.Write("main.migration", "#@import child/buckets.migration\n[amazon.s3]\nroot-bucket=private\n");
		directory.Write("child/buckets.migration", "[amazon.s3]\nchild-bucket=public\n");
		directory.Write("main.ini", "[amazon.s3]\nServer=http://root.invalid:9000\nRegion=us-east-1\nAccessKey=root\nSecretKey=sample\n");
		directory.Write("child/buckets.ini", "[amazon.s3]\nServer=http://child.invalid:9000\nRegion=us-east-1\nAccessKey=child\nSecretKey=sample\n");

		var plan = Loader().Load("main.migration", directory.Path, "test", "1.0.0");

		Assert.Equal(new[] { "http://child.invalid:9000", "http://root.invalid:9000" }, plan.Steps.Select(task => task.Settings["Server"]));
		Assert.Equal(new[] { "child-bucket", "root-bucket" }, plan.Steps.Select(task => Assert.Single(task.Buckets).Name));
		Assert.True(plan.Steps[0].Buckets[0].Public);
		Assert.False(plan.Steps[1].Buckets[0].Public);
		Assert.All(plan.Steps, task => Assert.Empty(task.Scripts));
	}

	[Theory]
	[InlineData(64)]
	[InlineData(65)]
	public void Load_DefaultImportDepth_Enforces64Files(int depth)
	{
		using var directory = new MigrationTestDirectory();
		for(var index = 0; index < depth; index++)
			directory.Write($"part-{index}.migration", index + 1 < depth ? $"#@import part-{index + 1}.migration\n" : "[sqlite]\n./schema.sql\n");
		directory.Write("sqlite.ini", "[sqlite]\nDatabase=/data/test.db\n");
		var sql = directory.Write("schema.sql", "SELECT 'leaf';");

		if(depth == 64)
		{
			var plan = Loader().Load("part-0.migration", directory.Path, "test", "1.0.0");
			var task = Assert.Single(plan.Steps);
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
