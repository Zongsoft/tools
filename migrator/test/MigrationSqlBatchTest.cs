using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Collections.Generic;

using Xunit;

using Zongsoft.Tools.Migrator.Migration;

namespace Zongsoft.Tools.Migrator.Tests;

public sealed class MigrationSqlBatchTest
{
	#region 测试方法
	[Theory]
	[InlineData("postgres")]
	[InlineData("duckdb")]
	[InlineData("sqlite")]
	[InlineData("mysql")]
	public void Read_OrdinaryScript_PreservesEntireDriverBatchIncludingComments(string provider)
	{
		const string SQL = "  SELECT 'first;part', '--literal';\r\n/* comments retained */\r\nSELECT 2; -- final comment\r\n";

		var batches = Load(SQL, provider);

		Assert.Equal(SQL, Assert.Single(batches));
	}

	[Theory]
	[InlineData("postgres", "SELECT $tag$unfinished")]
	[InlineData("duckdb", "SELECT 'unterminated;")]
	[InlineData("sqlite", "/* unfinished comment")]
	[InlineData("mysql", "SELECT 'unterminated;")]
	public void Read_DatabaseOwnedSyntaxValidation_DoesNotRejectIncompleteSql(string provider, string sql)
	{
		Assert.Equal(sql, Assert.Single(Load(sql, provider)));
	}

	[Fact]
	public void SqlServer_GoInsideQuotesAndComments_DoesNotBreakBatches()
	{
		var batches = Load("SELECT 'first;\nGO\nstill literal';\n/* GO */\nGO -- real batch\nSELECT [semi;colon], 'it''s ready';", "mssql");

		Assert.Equal(2, batches.Count);
		Assert.Contains("GO\nstill literal", batches[0]);
		Assert.Contains("/* GO */", batches[0]);
		Assert.Equal("SELECT [semi;colon], 'it''s ready';", batches[1]);
		Assert.DoesNotContain("real batch", string.Join("\n", batches));
	}

	[Fact]
	public void SqlServer_GoRepeatCount_RejectsUnsupportedClientRepetition()
	{
		Assert.Throws<InvalidDataException>(() => Load("SELECT 1;\nGO 2\n", "mssql"));
	}

	[Fact]
	public void MySql_DelimiterProcedureAndUserVariables_AreAdaptedIntoOneDriverBatch()
	{
		const string SQL = "SET @value = 1;\nDELIMITER $$\nCREATE PROCEDURE p() BEGIN SELECT 'a;'; SELECT 'it\\'s;ready'; END$$\nDELIMITER ;\nCALL p();";
		var batch = Assert.Single(Load(SQL, "mysql"));

		Assert.StartsWith("SET @value = 1;", batch);
		Assert.Contains("CREATE PROCEDURE p() BEGIN SELECT 'a;'; SELECT 'it\\'s;ready'; END;", batch);
		Assert.EndsWith("CALL p();", batch);
		Assert.DoesNotContain("DELIMITER", batch);
		Assert.DoesNotContain("END$$", batch);
	}

	[Fact]
	public void MySql_DelimiterLookingTextInsideQuotesAndComments_DoesNotChangeClientDelimiter()
	{
		const string PROCEDURE = "CREATE PROCEDURE p() BEGIN\nSELECT 'first\nDELIMITER //\nsecond $$';\n/*\nDELIMITER %%\n*/\n-- DELIMITER !!\nSELECT 2;\nEND";
		var batch = Assert.Single(Load("DELIMITER $$\n" + PROCEDURE + "$$\nDELIMITER ;\nCALL p();", "mysql"));

		Assert.Contains(PROCEDURE + ";", batch);
		Assert.Contains("second $$'", batch);
		Assert.EndsWith("CALL p();", batch);
		Assert.DoesNotContain("END$$", batch);
	}

	[Fact]
	public void TDengine_SemicolonsInQuotedValuesAndComments_DoNotSplitStatements()
	{
		var batches = Load("INSERT INTO readings VALUES (NOW, 'first;part'); /* separator;comment */ SELECT 'it''s;ready';", "tdengine");

		Assert.Equal(2, batches.Count);
		Assert.Contains("'first;part'", batches[0]);
		Assert.Contains("separator;comment", batches[1]);
		Assert.Contains("'it''s;ready'", batches[1]);
	}
	#endregion

	#region 辅助方法
	private static IReadOnlyList<string> Load(string sql, string provider)
	{
		using var directory = new MigrationTestDirectory();
		directory.Write("db.migration", "[" + provider + "]\n./schema.sql\n");
		directory.Write(provider + ".ini", provider is "sqlite" or "duckdb" ? "[" + provider + "]\nDatabase=/var/lib/zongsoft/hosting.db\n" : "[" + provider + "]\nServer=localhost\nDatabase=hosting\nUserName=operator\nPassword=\n");
		var source = directory.Write("schema.sql", "");
		File.WriteAllText(source, sql, new UTF8Encoding(false));
		var plan = new MigrationLoader(null).Load("db.migration", directory.Path, "zongsoft.daemon", "1.1.0");
		return Assert.Single(plan.Steps).Scripts.Select(script => script.Content).ToList();
	}
	#endregion
}
