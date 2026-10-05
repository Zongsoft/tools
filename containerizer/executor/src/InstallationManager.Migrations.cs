/*
 *   _____                                ______
 *  /_   /  ____  ____  ____  _________  / __/ /_
 *    / /  / __ \/ __ \/ __ \/ ___/ __ \/ /_/ __/
 *   / /__/ /_/ / / / /_/ /\_ \/ /_/ / __/ /_
 *  /____/\____/_/ /_/\__  /____/\____/_/  \__/
 *                   /____/
 *
 * Authors:
 *   钟峰(Popeye Zhong) <zongsoft@gmail.com>
 *
 * The MIT License (MIT)
 *
 * Copyright (C) 2026 Zongsoft Corporation <http://www.zongsoft.com>
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
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Collections.Generic;

using Zongsoft.Tools.Containerizer.Protocol;

namespace Zongsoft.Tools.Containerizer.Execution;

partial class InstallationManager
{
	#region 私有方法
	private async Task MigrationsAsync(Installation installation, DeliveryBundle bundle, string retry, CancellationToken cancellation)
	{
		foreach(var migration in bundle.Plan.Migrations)
		{
			var directory = store.GetMigrationPath(installation.Name, migration.Version);
			var result = installation.Migrations.GetValueOrDefault(migration.Identity);
			var unresolved = result?.Status is "Started" or "Failed" or "Interrupted" or "Unknown";

			if(unresolved && retry != migration.Version)
				throw new ContainerizationException(7, string.Format(Properties.Resources.Lifecycle_6_Message, migration.Version));

			if(result?.Status == "Succeeded" || result == null && installation.Migrations.Values.Any(item => item.Version == migration.Version && item.Status == "Succeeded"))
			{
				if(await host.MigrateAsync(bundle, migration, directory, "check", null, cancellation) != 0)
					throw new ContainerizationException(7, Properties.Resources.Lifecycle_7_Message);

				if(result == null)
				{
					installation.Migrations[migration.Identity] = new() { Version = migration.Version, Identity = migration.Identity, Status = "Succeeded" };
					store.Save(installation);
				}

				continue;
			}

			result ??= new() { Version = migration.Version, Identity = migration.Identity };
			installation.Migrations[migration.Identity] = result;
			result.Status = "Started";

			var attempt = new Installation.MigrationAttempt
			{
				Log = Path.Combine(
					store.GetLogPath(installation.Name),
					$"migration-{migration.Version}-{Guid.NewGuid():N}.log")
			};

			result.Attempts.Add(attempt);
			store.Save(installation);

			try
			{
				var code = await host.MigrateAsync(bundle, migration, directory, "apply", attempt.Log, cancellation);
				attempt.ExitCode = code;

				if(code != 0 || await host.MigrateAsync(bundle, migration, directory, "check", null, cancellation) != 0)
					throw new ContainerizationException(7, string.Format(Properties.Resources.Lifecycle_8_Message, migration.Version));

				result.Status = "Succeeded";
			}
			catch(OperationCanceledException) { result.Status = "Interrupted"; throw; }
			catch { result.Status = "Failed"; throw; }
			finally
			{
				attempt.Finished = DateTimeOffset.UtcNow;
				store.Save(installation);
			}
		}
	}
	#endregion
}
