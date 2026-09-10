using System;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Net.WebSockets;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Collections.Generic;

using Xunit;

namespace Zongsoft.Tools.Packager.Migration.Tests;

public sealed class MigrationTDengineTests
{
	#region 测试方法
	[Theory]
	[InlineData(null)]
	[InlineData("bootstrap")]
	public async Task WebSocket_ConnectionDatabaseSqlAndResultRelease_PreserveOrderWithFragmentedResponses(string bootstrap)
	{
		using var directory = new MigrationTestDirectory();
		await using var server = new LoopbackServer(request => Reply(request, result: Sql(request).StartsWith("SELECT", StringComparison.Ordinal)));
		var step = Step(directory, server, "SELECT 'quoted;值'", "INSERT INTO readings VALUES (1)");
		if(bootstrap != null) step.Parameters["BootstrapDatabase"] = bootstrap;

		await Migrator.Create("tdengine").MigrateAsync(step, new(directory.Path, Path.Combine(directory.Path, "state")), TestContext.Current.CancellationToken).WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);

		Assert.Equal("/rest/ws", server.RequestPath);
		Assert.Equal(new[] { "conn", "query", "query", "query", "free_result", "query" }, server.Requests.Select(Action));
		var arguments = server.Requests[0].GetProperty("args");
		Assert.Equal("operator", arguments.GetProperty("user").GetString());
		Assert.Equal("secret;with=punctuation\"", arguments.GetProperty("password").GetString());
		Assert.Equal(bootstrap ?? "", arguments.GetProperty("db").GetString());
		Assert.Equal(new[] { "CREATE DATABASE IF NOT EXISTS `hosting`", "USE `hosting`", "SELECT 'quoted;值'", "INSERT INTO readings VALUES (1)" }, server.Requests.Where(request => Action(request) == "query").Select(Sql));
		Assert.Equal(42UL, server.Requests[4].GetProperty("args").GetProperty("id").GetUInt64());
		var ids = server.Requests.Select(request => request.GetProperty("args").GetProperty("req_id").GetUInt64()).ToArray();
		Assert.Equal(ids.Length, ids.Distinct().Count());
	}

	[Fact]
	public async Task WebSocket_QueryFailure_StopsLaterSqlClearsReadyAndDoesNotExposeServerSecret()
	{
		using var directory = new MigrationTestDirectory();
		await using var server = new LoopbackServer(request => Reply(request, error: Sql(request) == "SELECT fail"));
		var step = Step(directory, server, "SELECT fail", "SELECT never");
		var plan = new MigrationPlan { Package = "zongsoft.daemon", Version = "1.1.0", Tasks = [step] };
		var context = new MigrationContext(directory.Path, Path.Combine(directory.Path, "state"));
		directory.Write("state/ready", plan.Fingerprint());

		var error = await Assert.ThrowsAsync<MigrationException>(() => new MigrationExecutor().ApplyAsync(plan, context, TestContext.Current.CancellationToken));

		Assert.False(MigrationExecutor.IsReady(plan, context.StateDirectory));
		Assert.Equal(new[] { "CREATE DATABASE IF NOT EXISTS `hosting`", "USE `hosting`", "SELECT fail" }, server.Requests.Where(request => Action(request) == "query").Select(Sql));
		var status = File.ReadAllText(Path.Combine(context.StateDirectory, "status.json"));
		Assert.DoesNotContain("server-secret-value", status + error.ToString());
		using var document = JsonDocument.Parse(status);
		Assert.Equal("failed", document.RootElement.GetProperty("status").GetString());
		Assert.Equal(step.Id, document.RootElement.GetProperty("task").GetString());
	}

	[Fact]
	public async Task WebSocket_DirectServerError_ReportsNumericCodeWithoutEchoingServerMessage()
	{
		using var directory = new MigrationTestDirectory();
		await using var server = new LoopbackServer(request => Reply(request, error: Action(request) == "query"));

		var error = await Assert.ThrowsAsync<InvalidOperationException>(() => Migrator.Create("tdengine").MigrateAsync(Step(directory, server, "SELECT never;"), new(directory.Path, Path.Combine(directory.Path, "state")), TestContext.Current.CancellationToken));

		Assert.Contains("123", error.Message);
		Assert.DoesNotContain("server-secret-value", error.ToString());
		Assert.Equal(new[] { "conn", "query" }, server.Requests.Select(Action));
	}

	[Theory]
	[InlineData(false)]
	[InlineData(true)]
	public async Task WebSocket_MismatchedResponseIdentity_RejectsBeforeSendingFurtherQueries(bool mismatchAction)
	{
		using var directory = new MigrationTestDirectory();
		await using var server = new LoopbackServer(request => new { action = mismatchAction ? "unexpected" : Action(request), req_id = request.GetProperty("args").GetProperty("req_id").GetUInt64() + (mismatchAction ? 0UL : 100UL), code = 0 });
		var step = Step(directory, server, "SELECT never;");

		await Assert.ThrowsAsync<InvalidDataException>(() => Migrator.Create("tdengine").MigrateAsync(step, new(directory.Path, Path.Combine(directory.Path, "state")), TestContext.Current.CancellationToken));

		Assert.Equal("conn", Action(Assert.Single(server.Requests)));
	}

	[Theory]
	[InlineData(false)]
	[InlineData(true)]
	public async Task WebSocket_NoQueryResponse_CommandTimeoutOrCancellationStopsExecution(bool cancel)
	{
		using var directory = new MigrationTestDirectory();
		using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
		await using var server = new LoopbackServer(request =>
		{
			if(Action(request) == "conn") return Reply(request);
			if(cancel) cancellation.Cancel();
			return null;
		});
		var step = Step(directory, server, "SELECT never;");
		step.Parameters["CommandTimeout"] = cancel ? "30s" : "1s";

		await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Migrator.Create("tdengine").MigrateAsync(step, new(directory.Path, Path.Combine(directory.Path, "state")), cancellation.Token).WaitAsync(TimeSpan.FromSeconds(8), TestContext.Current.CancellationToken));

		Assert.Equal(new[] { "conn", "query" }, server.Requests.Select(Action));
	}
	#endregion

	#region 辅助方法
	private static MigrationPlan.Step Step(MigrationTestDirectory directory, LoopbackServer server, params string[] batches) => new()
	{
		Id = "0001-tdengine", Provider = "tdengine",
		Parameters = new(StringComparer.OrdinalIgnoreCase) { ["Server"] = "127.0.0.1", ["Port"] = server.Port.ToString(System.Globalization.CultureInfo.InvariantCulture), ["Database"] = "hosting", ["UserName"] = "operator", ["Password"] = "secret;with=punctuation\"", ["Timeout"] = "3s", ["CommandTimeout"] = "3s" },
		Scripts = batches.Select((sql, index) => directory.Script($".migration/.artifacts/0001-tdengine/{index + 1:D4}.sql", sql)).ToList(),
	};

	private static string Action(JsonElement request) => request.GetProperty("action").GetString();

	private static string Sql(JsonElement request) => request.GetProperty("args").TryGetProperty("sql", out var sql) ? sql.GetString().Trim().TrimEnd(';') : "";

	private static object Reply(JsonElement request, bool result = false, bool error = false)
	{
		if(Action(request) == "free_result") return null;
		return new { action = Action(request), req_id = request.GetProperty("args").GetProperty("req_id").GetUInt64(), code = error ? 123 : 0, message = error ? "server-secret-value" : "", is_update = !result, id = result ? 42 : 0, affected_rows = 1 };
	}
	#endregion

	#region 嵌套类型
	private sealed class LoopbackServer : IAsyncDisposable
	{
		#region 成员字段
		private readonly HttpListener _listener = new();
		private readonly CancellationTokenSource _stop = new();
		private readonly Task _pump;
		private WebSocket _socket;
		#endregion

		#region 构造函数
		public LoopbackServer(Func<JsonElement, object> reply)
		{
			using(var probe = new TcpListener(IPAddress.Loopback, 0))
			{
				probe.Start();
				this.Port = ((IPEndPoint)probe.LocalEndpoint).Port;
			}
			_listener.Prefixes.Add($"http://127.0.0.1:{this.Port}/");
			_listener.Start();
			_pump = Pump(reply);
		}
		#endregion

		#region 属性定义
		public int Port { get; }
		public string RequestPath { get; private set; }
		public List<JsonElement> Requests { get; } = [];
		#endregion

		#region 辅助方法
		private async Task Pump(Func<JsonElement, object> reply)
		{
			try
			{
				var context = await _listener.GetContextAsync().WaitAsync(_stop.Token);
				this.RequestPath = context.Request.Url.AbsolutePath;
				_socket = (await context.AcceptWebSocketAsync(null)).WebSocket;
				var buffer = new byte[1024];
				while(!_stop.IsCancellationRequested)
				{
					using var message = new MemoryStream();
					WebSocketReceiveResult received;
					do
					{
						received = await _socket.ReceiveAsync(new ArraySegment<byte>(buffer), _stop.Token);
						if(received.MessageType == WebSocketMessageType.Close) return;
						message.Write(buffer, 0, received.Count);
					} while(!received.EndOfMessage);
					using var document = JsonDocument.Parse(message.ToArray());
					var request = document.RootElement.Clone();
					this.Requests.Add(request);
					var response = reply(request);
					if(response == null) continue;
					var bytes = JsonSerializer.SerializeToUtf8Bytes(response);
					var split = bytes.Length / 2;
					await _socket.SendAsync(new ArraySegment<byte>(bytes, 0, split), WebSocketMessageType.Text, false, _stop.Token);
					await _socket.SendAsync(new ArraySegment<byte>(bytes, split, bytes.Length - split), WebSocketMessageType.Text, true, _stop.Token);
				}
			}
			catch(OperationCanceledException) when(_stop.IsCancellationRequested) { }
			catch(WebSocketException) { }
			catch(HttpListenerException) when(_stop.IsCancellationRequested) { }
		}

		public async ValueTask DisposeAsync()
		{
			_stop.Cancel();
			_socket?.Abort();
			_listener.Close();
			try { await _pump; }
			finally { _socket?.Dispose(); _stop.Dispose(); }
		}
		#endregion
	}
	#endregion
}
