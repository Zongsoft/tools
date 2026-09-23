/*
 *   _____                                ______
 *  /_   /  ____  ____  ____  _________  / __/ /_
 *    / /  / __ \/ __ \/ __ \/ ___/ __ \/ /_/ __/
 *   / /__/ /_/ / / / / /_/ /\_ \/ /_/ / __/ /_
 *  /____/\____/_/ /_/\__  /____/\____/_/  \__/
 *                   /____/
 *
 * Authors:
 *   钟峰(Popeye Zhong) <zongsoft@gmail.com>
 *
 * The MIT License (MIT)
 *
 * Copyright (C) 2020-2026 Zongsoft Corporation <http://www.zongsoft.com>
 *
 * Permission is hereby granted, free of charge, to any person obtaining a copy
 * of this software and associated documentation files (the "Software"), to deal
 * in the Software without restriction, including without limitation the rights
 * to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
 * copies of the Software, and to permit persons to whom the Software is
 * furnished to do so, subject to the following conditions:
 *
 * The above copyright notice and this permission notice shall be included in all
 * copies or substantial portions of the Software.
 *
 * THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
 * IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
 * FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
 * AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
 * LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
 * OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE SOFTWARE.
 */

using System;
using System.Text.Json;
using System.Net.WebSockets;

namespace Zongsoft.Tools.Migrator.Migration;

partial class Migrator
{
	partial class Database
	{
		public sealed class TDengine : Database
		{
			#region 升迁方法
			public override async Task InitializeAsync(MigrationPlan.Database database, MigrationContext context, CancellationToken cancellation = default)
			{
				using var client = await OpenAsync(database, database.Settings.Get("Bootstrap", ""), cancellation);
				var timeout = database.Options.Seconds("CommandTimeout", 300);
				var query = "SELECT name FROM information_schema.ins_databases WHERE name = " + Text(database.Name);

				if(await client.ExistsAsync(query, timeout, cancellation))
					return;

				var options = database.Options;

				try
				{
					await client.ExecuteAsync("CREATE DATABASE " + Quote(database.Name) + " PRECISION " + Text(options.Get("Precision")) +
						" KEEP " + options.Get("Keep") + " DURATION " + options.Get("Duration") + " REPLICA " + options.Get("Replica"), timeout, cancellation);
				}
				catch(InvalidOperationException)
				{
					if(!await client.ExistsAsync(query, timeout, cancellation))
						throw;
				}
			}

			public override async Task CreateUsersAsync(MigrationPlan.Database database, MigrationContext context, CancellationToken cancellation = default)
			{
				if(database.Users.Count == 0)
					return;

				using var client = await OpenAsync(database, database.Settings.Get("Bootstrap", ""), cancellation);
				var timeout = database.Options.Seconds("CommandTimeout", 300);

				foreach(var user in database.Users)
				{
					var query = "SELECT name FROM information_schema.ins_users WHERE name = " + Text(user.Name);
					if(await client.ExistsAsync(query, timeout, cancellation))
						continue;

					try
					{
						await client.ExecuteAsync("CREATE USER " + Quote(user.Name) + " PASS " + Text(user.Password), timeout, cancellation);
					}
					catch(InvalidOperationException)
					{
						if(!await client.ExistsAsync(query, timeout, cancellation))
							throw;
					}
				}
			}

			public override async Task MigrateAsync(MigrationPlan.Step step, MigrationContext context, CancellationToken cancellation = default)
			{
				var database = context.GetDatabase(step.DatabaseIndex);
				var timeout = database.Options.Seconds("CommandTimeout", 300);
				using var client = await OpenAsync(database, database.Name, cancellation);

				foreach(var script in step.Scripts)
				{
					context.Log(string.Format(Properties.Resources.ScriptExecuting, context.StepNumber, script.Path));
					await client.ExecuteAsync(await File.ReadAllTextAsync(context.GetScriptPath(script), cancellation), timeout, cancellation);
				}
			}

			public override async Task GrantAsync(MigrationPlan.Database database, MigrationContext context, CancellationToken cancellation = default)
			{
				if(database.Users.Count == 0)
					return;

				var timeout = database.Options.Seconds("CommandTimeout", 300);
				using var client = await OpenAsync(database, database.Name, cancellation);

				foreach(var user in database.Users)
				{
					MigrationPrivileges.Validate(database, user);
					var privileges = user.Privileges.Select(value => value == "Select" ? "READ" : "WRITE");

					foreach(var privilege in privileges.Distinct(StringComparer.OrdinalIgnoreCase))
						await client.ExecuteAsync("GRANT " + privilege + " ON " + Quote(database.Name) + ".* TO " + Quote(user.Name), timeout, cancellation);
				}
			}
			#endregion

			#region 私有方法
			private static string Quote(string name) => "`" + name.Replace("`", "``") + "`";
			private static string Text(string value) => "'" + value.Replace("\\", "\\\\").Replace("'", "\\'") + "'";

			private static async Task<Session> OpenAsync(MigrationPlan.Database database, string name, CancellationToken cancellation)
			{
				var settings = database.Settings;
				var endpoint = new UriBuilder(bool.Parse(settings.Get("Secured", "false")) ? "wss" : "ws",
					settings.Get("Server"), int.Parse(settings.Get("Port", "6041"), System.Globalization.CultureInfo.InvariantCulture), "/rest/ws").Uri;

				var client = new Session();

				try
				{
					using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
					timeout.CancelAfter(TimeSpan.FromSeconds(settings.Seconds("Timeout", 30)));
					await client.ConnectAsync(endpoint, timeout.Token);

					using var response = await client.RequestAsync("conn", writer =>
					{
						writer.WriteString("user", settings.Get("UserName"));
						writer.WriteString("password", settings["Password"]);
						writer.WriteString("db", name);
					}, timeout.Token);

					return client;
				}
				catch
				{
					client.Dispose();
					throw;
				}
			}
			#endregion

			#region 嵌套子类
			private sealed class Session : IDisposable
			{
				#region 成员字段
				private ulong _sequence;
				private readonly ClientWebSocket _socket = new();
				#endregion

				#region 连接方法
				public Task ConnectAsync(Uri endpoint, CancellationToken cancellation) => _socket.ConnectAsync(endpoint, cancellation);
				#endregion

				#region 协议方法
				public async Task ExecuteAsync(string sql, int seconds, CancellationToken cancellation)
				{
					using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
					timeout.CancelAfter(TimeSpan.FromSeconds(seconds));
					using var response = await this.RequestAsync("query", writer => writer.WriteString("sql", sql), timeout.Token);

					if(!response.RootElement.GetProperty("is_update").GetBoolean())
					{
						var id = response.RootElement.GetProperty("id").GetUInt64();
						// SQL migration ignores result rows. taosAdapter does not acknowledge free_result.
						await this.SendAsync("free_result", writer => writer.WriteNumber("id", id), timeout.Token);
					}
				}

				public async Task<bool> ExistsAsync(string sql, int seconds, CancellationToken cancellation)
				{
					using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
					timeout.CancelAfter(TimeSpan.FromSeconds(seconds));

					using var query = await this.RequestAsync("query", writer => writer.WriteString("sql", sql), timeout.Token);

					if(query.RootElement.GetProperty("is_update").GetBoolean())
						throw new InvalidDataException(Properties.Resources.TDengineResponseInvalid_Message);

					var id = query.RootElement.GetProperty("id").GetUInt64();

					try
					{
						using var rows = await this.RequestAsync("fetch", writer => writer.WriteNumber("id", id), timeout.Token);
						return rows.RootElement.GetProperty("rows").GetInt32() > 0;
					}
					finally
					{
						if(!timeout.IsCancellationRequested)
							await this.SendAsync("free_result", writer => writer.WriteNumber("id", id), timeout.Token);
					}
				}

				public async Task<JsonDocument> RequestAsync(string action, Action<Utf8JsonWriter> arguments, CancellationToken cancellation)
				{
					await this.SendAsync(action, arguments, cancellation);

					using var message = new MemoryStream();
					var buffer = new byte[4096];
					ValueWebSocketReceiveResult received;

					do
					{
						received = await _socket.ReceiveAsync(buffer.AsMemory(), cancellation);

						if(received.MessageType != WebSocketMessageType.Text)
							throw new InvalidDataException(Properties.Resources.TDengineResponseInvalid_Message);
						if(message.Length + received.Count > 1024 * 1024)
							throw new InvalidDataException(Properties.Resources.TDengineResponseTooLarge_Message);

						message.Write(buffer, 0, received.Count);
					} while(!received.EndOfMessage);

					var response = JsonDocument.Parse(message.GetBuffer().AsMemory(0, (int)message.Length));

					try
					{
						var root = response.RootElement;

						if(root.GetProperty("action").GetString() != action || root.GetProperty("req_id").GetUInt64() != _sequence)
							throw new InvalidDataException(Properties.Resources.TDengineResponseInvalid_Message);

						// Server messages can echo SQL and credentials; expose only the numeric error code.
						var code = root.GetProperty("code").GetInt32();
						if(code != 0)
							throw new InvalidOperationException(string.Format(Properties.Resources.TDengineRequestFailed_Message, action, code));

						return response;
					}
					catch
					{
						response.Dispose();
						throw;
					}
				}

				private ValueTask SendAsync(string action, Action<Utf8JsonWriter> arguments, CancellationToken cancellation)
				{
					var buffer = new System.Buffers.ArrayBufferWriter<byte>();
					using(var writer = new Utf8JsonWriter(buffer))
					{
						writer.WriteStartObject();
						writer.WriteString("action", action);
						writer.WriteStartObject("args");
						writer.WriteNumber("req_id", ++_sequence);
						arguments(writer);
						writer.WriteEndObject();
						writer.WriteEndObject();
					}

					return _socket.SendAsync(buffer.WrittenMemory, WebSocketMessageType.Text, true, cancellation);
				}
				#endregion

				#region 释放资源
				public void Dispose() => _socket.Dispose();
				#endregion
			}
			#endregion
		}
	}
}
