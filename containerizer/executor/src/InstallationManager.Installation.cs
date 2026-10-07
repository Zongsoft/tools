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
	private async Task InstallAsync(DeliveryBundle input, ExecutorArguments arguments, CancellationToken cancellation)
	{
		var installation = store.Load(input.Plan.Name, false);

		if(arguments.Command == "upgrade" && installation == null)
			throw new ContainerizationException(2, Properties.Resources.Lifecycle_3_Message);
		if(installation?.Pending != null && installation.Pending.Id != input.Id)
			throw new ContainerizationException(7, Properties.Resources.Lifecycle_4_Message);
		if(installation?.Pending?.Failed == true)
			throw new ContainerizationException(7, Properties.Resources.Lifecycle_5_Message);

		await host.VerifyTargetAsync(input, installation, cancellation);

		if(installation?.Current != null)
		{
			using var current = this.OpenCurrentBundle(installation);
			ValidateUpgrade(current.Plan, input.Plan);

			if(installation.Current == input.Id && installation.Pending == null && installation.Status == "Installed" && !installation.IsInMaintenance)
			{
				await host.PrepareImagesAsync(current, cancellation);

				foreach(var service in current.Plan.Services)
					await host.WaitForHealthAsync(current, service, cancellation);

				return;
			}
		}

		var wasInMaintenance = installation?.IsInMaintenance == true;
		await host.InstallExecutorAsync(input, cancellation);

		using(var registrationLock = store.AcquireHostLock())
		{
			installation ??= new() { Name = input.Plan.Name, DataRoot = input.Plan.DataRoot };
			var assets = store.StageBundle(input);

			if(!installation.Releases.Contains(input.Id))
				installation.Releases.Add(input.Id);

			installation.Pending ??= new()
			{
				Id = input.Id,
				Version = input.Plan.Version,
				Assets = assets,
				IsReinstall = installation.Status == "Uninstalled"
			};

			installation.PurgeResourcesCompleted = false;
			store.Save(installation);
		}

		using var bundle = DeliveryBundle.Open(installation.Pending.Assets);
		await this.ContinueAsync(installation, bundle, arguments.Command == "prepare", arguments.NoStart || wasInMaintenance, null, cancellation);
	}

	private async Task ContinueAsync(Installation installation, DeliveryBundle bundle, bool prepareOnly, bool suppressStart, string retryMigrationVersion, CancellationToken cancellation)
	{
		var requiresMaintenance = false;

		try
		{
			await this.ExecutePhaseAsync(installation, "PrepareBootstrap", () => host.PrepareBootstrapAsync(bundle, installation, cancellation));
			await this.ExecutePhaseAsync(installation, "PrepareImages", () => host.PrepareImagesAsync(bundle, cancellation));

			if(prepareOnly)
			{
				installation.Status = "Prepared";
				store.Save(installation);
				return;
			}

			await host.PrepareDirectoriesAsync(bundle, installation, cancellation);

			// The persistent maintenance barrier precedes every stop or migration.
			requiresMaintenance = true;

			await this.EnterMaintenanceAsync(installation, cancellation);

			await this.ExecutePhaseAsync(installation, "StartInfrastructure", async () =>
			{
				foreach(var service in GetServicesInDependencyOrder(bundle.Plan).Where(service => service.Kind == "infrastructure"))
				{
					if(installation.Current == null || installation.Pending.IsReinstall)
						await host.StartServiceAsync(bundle, service, cancellation);
				}
			});

			await this.ExecutePhaseAsync(installation, "CheckLocalInfrastructure", async () =>
			{
				foreach(var service in GetServicesInDependencyOrder(bundle.Plan).Where(service => service.Kind == "infrastructure"))
					await host.WaitForHealthAsync(bundle, service, cancellation);
			});

			await this.ExecutePhaseAsync(installation, "ApplyMigration", () => this.ApplyMigrationsAsync(installation, bundle, retryMigrationVersion, cancellation));
			await this.ExecutePhaseAsync(installation, "ReadyToStart", () => Task.CompletedTask);

			installation.Status = "ReadyToStart";
			store.Save(installation);

			if(!suppressStart)
				await this.ActivateAsync(installation, bundle, cancellation);
		}
		catch(Exception exception)
		{
			await this.FailAsync(installation, exception, requiresMaintenance);
			throw;
		}
	}
	#endregion
}
