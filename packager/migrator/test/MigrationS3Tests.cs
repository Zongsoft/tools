using System;
using System.IO;
using System.Net;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Collections.Generic;

using Amazon.Runtime;
using Amazon.S3;
using Amazon.S3.Model;

using Xunit;

namespace Zongsoft.Tools.Packager.Migration.Tests;

public sealed class MigrationS3Tests
{
	#region 测试方法
	[Theory]
	[InlineData(false)]
	[InlineData(true)]
	public async Task S3_ExistingBucket_NeverChangesAccessPolicy(bool isPublic)
	{
		using var directory = new MigrationTestDirectory();
		var client = new RecordingS3Client { Exists = true };
		var context = new MigrationContext(directory.Path, Path.Combine(directory.Path, "state"));

		await new Migrator.AmazonS3(_ => client).MigrateAsync(TaskFor(isPublic), context, TestContext.Current.CancellationToken);

		Assert.Equal(new[] { "HEAD attachments" }, client.Calls);
		Assert.Null(client.Policy);
		Assert.Empty(Directory.GetFiles(context.StateDirectory, "*.pending"));
	}

	[Theory]
	[InlineData(false)]
	[InlineData(true)]
	public async Task S3_MissingBucket_CreatesBucketAndOnlyRequestedPublicReadPolicy(bool isPublic)
	{
		using var directory = new MigrationTestDirectory();
		var client = new RecordingS3Client();
		var context = new MigrationContext(directory.Path, Path.Combine(directory.Path, "state"));

		await new Migrator.AmazonS3(_ => client).MigrateAsync(TaskFor(isPublic), context, TestContext.Current.CancellationToken);

		Assert.Equal(isPublic ? new[] { "HEAD attachments", "CREATE attachments", "POLICY attachments" } : new[] { "HEAD attachments", "CREATE attachments" }, client.Calls);
		Assert.True(client.Exists);
		Assert.Empty(Directory.GetFiles(context.StateDirectory, "*.pending"));
		if(isPublic)
		{
			using var policy = JsonDocument.Parse(client.Policy);
			var statement = Assert.Single(policy.RootElement.GetProperty("Statement").EnumerateArray());
			Assert.Equal("s3:GetObject", statement.GetProperty("Action").GetString());
			Assert.Equal("*", statement.GetProperty("Principal").GetString());
			Assert.Equal("arn:aws:s3:::attachments/*", statement.GetProperty("Resource").GetString());
			Assert.Equal("Allow", statement.GetProperty("Effect").GetString());
		}
		else Assert.Null(client.Policy);
	}

	[Theory]
	[InlineData(HttpStatusCode.Forbidden)]
	[InlineData(HttpStatusCode.BadRequest)]
	public async Task S3_HeadFailure_DoesNotTreatFailureAsMissingBucket(HttpStatusCode status)
	{
		using var directory = new MigrationTestDirectory();
		var client = new RecordingS3Client { HeadFailure = status };

		var error = await Assert.ThrowsAsync<AmazonS3Exception>(() => new Migrator.AmazonS3(_ => client).MigrateAsync(TaskFor(true), new(directory.Path, Path.Combine(directory.Path, "state")), TestContext.Current.CancellationToken));

		Assert.Equal(status, error.StatusCode);
		Assert.Equal(new[] { "HEAD attachments" }, client.Calls);
		Assert.False(client.Exists);
	}

	[Fact]
	public async Task S3_PublicPolicyFailsAfterCreation_RetryResumesPolicyWithoutRecreatingBucket()
	{
		using var directory = new MigrationTestDirectory();
		var context = new MigrationContext(directory.Path, Path.Combine(directory.Path, "state"));
		var first = new RecordingS3Client { PolicyFailure = true };

		await Assert.ThrowsAsync<AmazonS3Exception>(() => new Migrator.AmazonS3(_ => first).MigrateAsync(TaskFor(true), context, TestContext.Current.CancellationToken));

		Assert.True(first.Exists);
		Assert.Single(Directory.GetFiles(context.StateDirectory, "*.pending"));
		var retry = new RecordingS3Client { Exists = true };
		await new Migrator.AmazonS3(_ => retry).MigrateAsync(TaskFor(true), context, TestContext.Current.CancellationToken);
		Assert.Equal(new[] { "HEAD attachments", "POLICY attachments" }, retry.Calls);
		Assert.NotNull(retry.Policy);
		Assert.Empty(Directory.GetFiles(context.StateDirectory, "*.pending"));
	}

	[Fact]
	public async Task S3_CreateDenied_DoesNotWritePolicyOrLeavePendingOwnership()
	{
		using var directory = new MigrationTestDirectory();
		var context = new MigrationContext(directory.Path, Path.Combine(directory.Path, "state"));
		var client = new RecordingS3Client { CreateFailure = HttpStatusCode.Forbidden };

		await Assert.ThrowsAsync<AmazonS3Exception>(() => new Migrator.AmazonS3(_ => client).MigrateAsync(TaskFor(true), context, TestContext.Current.CancellationToken));

		Assert.Equal(new[] { "HEAD attachments", "CREATE attachments" }, client.Calls);
		Assert.Null(client.Policy);
		Assert.False(client.Exists);
		Assert.Empty(Directory.GetFiles(context.StateDirectory, "*.pending"));
	}
	#endregion

	#region 辅助方法
	private static MigrationPlan.Step TaskFor(bool isPublic) => new()
	{
		Id = "0001-amazon.s3", Provider = "amazon.s3",
		Parameters = new(StringComparer.OrdinalIgnoreCase) { ["Server"] = "http://localhost:9000", ["Region"] = "us-east-1", ["AccessKey"] = "test", ["SecretKey"] = "test" },
		Buckets = [new() { Name = "attachments", Public = isPublic }],
	};
	#endregion

	#region 嵌套类型
	private sealed class RecordingS3Client() : AmazonS3Client(new BasicAWSCredentials("test", "test"), new AmazonS3Config { ServiceURL = "http://localhost:9000" })
	{
		#region 属性定义
		public List<string> Calls { get; } = [];
		public bool Exists { get; set; }
		public HttpStatusCode? HeadFailure { get; set; }
		public bool PolicyFailure { get; set; }
		public HttpStatusCode? CreateFailure { get; set; }
		public string Policy { get; private set; }
		#endregion

		#region 模拟请求
		public override Task<HeadBucketResponse> HeadBucketAsync(HeadBucketRequest request, CancellationToken cancellationToken = default)
		{
			this.Calls.Add("HEAD " + request.BucketName);
			if(this.HeadFailure.HasValue || !this.Exists)
				return Task.FromException<HeadBucketResponse>(new AmazonS3Exception("fake HEAD failure") { StatusCode = this.HeadFailure ?? HttpStatusCode.NotFound });
			return Task.FromResult(new HeadBucketResponse());
		}

		public override Task<PutBucketResponse> PutBucketAsync(PutBucketRequest request, CancellationToken cancellationToken = default)
		{
			this.Calls.Add("CREATE " + request.BucketName);
			if(this.CreateFailure.HasValue) return Task.FromException<PutBucketResponse>(new AmazonS3Exception("fake create denied") { StatusCode = this.CreateFailure.Value });
			this.Exists = true;
			return Task.FromResult(new PutBucketResponse());
		}

		public override Task<PutBucketPolicyResponse> PutBucketPolicyAsync(PutBucketPolicyRequest request, CancellationToken cancellationToken = default)
		{
			this.Calls.Add("POLICY " + request.BucketName);
			if(this.PolicyFailure) return Task.FromException<PutBucketPolicyResponse>(new AmazonS3Exception("fake policy denied") { StatusCode = HttpStatusCode.Forbidden });
			this.Policy = request.Policy;
			return Task.FromResult(new PutBucketPolicyResponse());
		}
		#endregion
	}
	#endregion
}

public sealed class MigrationExecutorTests
{
	#region 测试方法
	[Fact]
	public async Task Apply_SuccessAndRepeat_ExecutesEveryTaskInOrderAndRecordsCurrentFingerprint()
	{
		using var directory = new MigrationTestDirectory();
		var plan = Plan();
		var context = new MigrationContext(directory.Path, Path.Combine(directory.Path, "state"));
		var calls = new List<string>();
		var executor = new MigrationExecutor(_ => new ActionMigrator(task => calls.Add(task.Id)));

		await executor.ApplyAsync(plan, context, TestContext.Current.CancellationToken);
		await executor.ApplyAsync(plan, context, TestContext.Current.CancellationToken);

		Assert.Equal(new[] { "one", "two", "one", "two" }, calls);
		Assert.True(MigrationExecutor.IsReady(plan, context.StateDirectory));
		using var status = JsonDocument.Parse(File.ReadAllText(Path.Combine(context.StateDirectory, "status.json")));
		Assert.Equal("complete", status.RootElement.GetProperty("status").GetString());
		plan.Version = "1.2.0";
		Assert.False(MigrationExecutor.IsReady(plan, context.StateDirectory));
	}

	[Fact]
	public async Task Apply_RetryFails_ClearsOldReadinessStopsRemainingTasksAndRedactsError()
	{
		using var directory = new MigrationTestDirectory();
		var plan = Plan();
		var context = new MigrationContext(directory.Path, Path.Combine(directory.Path, "state"));
		await new MigrationExecutor(_ => new ActionMigrator(_ => { })).ApplyAsync(plan, context, TestContext.Current.CancellationToken);
		var calls = new List<string>();
		var executor = new MigrationExecutor(_ => new ActionMigrator(task => { calls.Add(task.Id); throw new InvalidOperationException("secret;password=value"); }));

		var error = await Assert.ThrowsAsync<MigrationException>(() => executor.ApplyAsync(plan, context, TestContext.Current.CancellationToken));

		Assert.Equal(new[] { "one" }, calls);
		Assert.False(MigrationExecutor.IsReady(plan, context.StateDirectory));
		var status = File.ReadAllText(Path.Combine(context.StateDirectory, "status.json"));
		Assert.DoesNotContain("password", status + error.Message);
		using var document = JsonDocument.Parse(status);
		Assert.Equal("failed", document.RootElement.GetProperty("status").GetString());
		Assert.Equal("one", document.RootElement.GetProperty("task").GetString());
	}

	[Fact]
	public async Task Apply_ConcurrentOwner_HoldsExclusiveLockAndRejectsSecondExecution()
	{
		using var directory = new MigrationTestDirectory();
		var state = Path.Combine(directory.Path, "state");
		Directory.CreateDirectory(state);
		using var held = new FileStream(Path.Combine(state, "migration.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
		var calls = new List<string>();

		await Assert.ThrowsAsync<IOException>(() => new MigrationExecutor(_ => new ActionMigrator(task => calls.Add(task.Id))).ApplyAsync(Plan(), new(directory.Path, state), TestContext.Current.CancellationToken));

		Assert.Empty(calls);
		Assert.False(File.Exists(Path.Combine(state, "ready")));
	}

	[Fact]
	public async Task Apply_LaterTaskHasInvalidSqlChecksum_PrevalidatesBeforeAnyMigratorRuns()
	{
		using var directory = new MigrationTestDirectory();
		var plan = Plan();
		var script = directory.Script(".migration/.artifacts/later.sql", "SELECT 1;");
		plan.Tasks[1].Scripts.Add(script);
		directory.Write(".migration/.artifacts/later.sql", "SELECT 2;");
		var calls = new List<string>();
		var context = new MigrationContext(directory.Path, Path.Combine(directory.Path, "state"));

		await Assert.ThrowsAsync<MigrationException>(() => new MigrationExecutor(_ => new ActionMigrator(task => calls.Add(task.Id))).ApplyAsync(plan, context, TestContext.Current.CancellationToken));

		Assert.Empty(calls);
		Assert.False(MigrationExecutor.IsReady(plan, context.StateDirectory));
		using var status = JsonDocument.Parse(File.ReadAllText(Path.Combine(context.StateDirectory, "status.json")));
		Assert.Equal("two", status.RootElement.GetProperty("task").GetString());
		Assert.Equal("failed", status.RootElement.GetProperty("status").GetString());
	}
	#endregion

	#region 辅助方法
	private static MigrationPlan Plan() => new()
	{
		Package = "zongsoft.web", Version = "1.1.0",
		Tasks = [new() { Id = "one", Provider = "sqlite", Parameters = new(StringComparer.OrdinalIgnoreCase) { ["Database"] = "/var/lib/zongsoft/web.db" } }, new() { Id = "two", Provider = "duckdb", Parameters = new(StringComparer.OrdinalIgnoreCase) { ["Database"] = "/var/lib/zongsoft/web.duckdb" } }],
	};
	#endregion

	#region 嵌套类型
	private sealed class ActionMigrator(Action<MigrationPlan.Step> action) : Migrator
	{
		#region 模拟方法
		public override Task MigrateAsync(MigrationPlan.Step task, MigrationContext context, CancellationToken cancellation = default)
		{
			action(task);
			return Task.CompletedTask;
		}
		#endregion
	}
	#endregion
}
