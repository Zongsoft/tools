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

using System.Text.Json;

namespace Zongsoft.Tools.Packager.Migration;

public sealed class MigrationExecutor(Func<string, Migrator> resolve = null)
{
	#region 成员字段
	private readonly Func<string, Migrator> _resolve = resolve ?? Migrator.Create;
	#endregion

	#region 公共方法
	public async Task ApplyAsync(MigrationPlan plan, MigrationContext context, CancellationToken cancellation = default)
	{
		Directory.CreateDirectory(context.StateDirectory);
		if(!OperatingSystem.IsWindows())
			File.SetUnixFileMode(context.StateDirectory, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute | UnixFileMode.GroupRead | UnixFileMode.GroupExecute | UnixFileMode.OtherRead | UnixFileMode.OtherExecute);

		using var exclusive = new FileStream(Path.Combine(context.StateDirectory, "migration.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
		var ready = Path.Combine(context.StateDirectory, "ready");
		File.Delete(ready);
		var current = "validation";

		try
		{
			plan.Validate();
			foreach(var task in plan.Tasks)
			{
				current = task.Id;
				MigrationProvider.Get(task.Provider).Validate(task.Parameters);
				foreach(var script in task.Scripts) context.GetScriptPath(script);
			}

			foreach(var task in plan.Tasks)
			{
				current = task.Id;
				context.Log(string.Format(Properties.Resources.MigrationStarting, task.Id));
				await _resolve(task.Provider).MigrateAsync(task, context, cancellation);
			}

			await WriteStatus("complete", null);
			await File.WriteAllTextAsync(ready + ".tmp", plan.Fingerprint(), cancellation);
			if(!OperatingSystem.IsWindows()) File.SetUnixFileMode(ready + ".tmp", UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.GroupRead | UnixFileMode.OtherRead);
			File.Move(ready + ".tmp", ready, true);
		}
		catch(Exception ex)
		{
			// Driver error messages can contain SQL, connection strings or credentials.
			await WriteStatus("failed", ex.GetType().Name);
			throw new MigrationException(string.Format(Properties.Resources.MigrationFailed, current, ex.GetType().Name));
		}

		async Task WriteStatus(string status, string error)
		{
			await using var stream = File.Create(Path.Combine(context.StateDirectory, "status.json"));
			await using var writer = new Utf8JsonWriter(stream, new() { Indented = true });
			writer.WriteStartObject();
			writer.WriteString("package", plan.Package);
			writer.WriteString("version", plan.Version);
			writer.WriteString("status", status);
			writer.WriteString("task", current);
			writer.WriteString("error", error);
			writer.WriteString("time", DateTimeOffset.UtcNow);
			writer.WriteEndObject();
			await writer.FlushAsync();
		}
	}

	public static bool IsReady(MigrationPlan plan, string stateDirectory)
	{
		var path = Path.Combine(stateDirectory, "ready");
		return File.Exists(path) && File.ReadAllText(path) == plan.Fingerprint();
	}
	#endregion
}

public sealed class MigrationException(string message) : Exception(message);
