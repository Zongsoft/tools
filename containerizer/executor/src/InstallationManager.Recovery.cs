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
	private async Task RecoverAsync(Installation installation, string retry, CancellationToken cancellation)
	{
		var transaction = installation.Pending;
		if(transaction == null || !transaction.Failed && installation.Status is "ReadyToStart" or "Prepared")
			throw new ContainerizationException(7, Properties.Resources.Lifecycle_9_Message);

		using var bundle = DeliveryBundle.Open(transaction.Assets);
		if(bundle.Id != transaction.Id)
			throw new ContainerizationException(4, Properties.Resources.Lifecycle_10_Message);

		if(retry != null)
		{
			var matches = bundle.Plan.Migrations.Where(item => item.Version == retry).ToArray();

			if(matches.Length != 1 ||
			   !installation.Migrations.TryGetValue(matches[0].Identity, out var result) ||
			   result.Status is not ("Started" or "Failed" or "Interrupted" or "Unknown"))
				throw new ContainerizationException(7, Properties.Resources.Lifecycle_11_Message);
		}

		Console.WriteLine(string.Format(Properties.Resources.Lifecycle_Recovery, installation.Name, transaction.Version, GetPhaseText(transaction.Phase)));

		await host.VerifyAsync(bundle, installation, cancellation);
		await host.StopApplicationsAsync(installation, cancellation);

		installation.Maintenance = true;
		transaction.Failed = false;
		store.Save(installation);

		await this.ContinueAsync(installation, bundle, false, true, retry, cancellation);
	}

	private async Task StartAsync(Installation installation, CancellationToken cancellation)
	{
		if(installation.Status == "Uninstalled")
			throw new ContainerizationException(7, Properties.Resources.Lifecycle_12_Message);

		if(installation.Pending != null)
		{
			if(installation.Pending.Failed || !installation.Pending.Completed.Contains("ReadyToStart"))
				throw new ContainerizationException(7, Properties.Resources.Lifecycle_13_Message);

			using var bundle = DeliveryBundle.Open(installation.Pending.Assets);
			await this.MigrationsAsync(installation, bundle, null, cancellation);
			await this.ActivateAsync(installation, bundle, cancellation);
		}
		else
		{
			using var bundle = this.Current(installation);

			try
			{
				await this.CheckInfrastructureAsync(bundle, cancellation);

				foreach(var service in Ordered(bundle.Plan).Where(service => service.Kind != "infrastructure"))
				{
					await host.StartAsync(bundle, service, cancellation);
					await host.HealthyAsync(bundle, service, cancellation);
				}

				await host.RestorePoliciesAsync(bundle, cancellation);
				installation.Maintenance = false;
				installation.Status = "Installed";

				store.Save(installation);
			}
			catch
			{
				await host.StopApplicationsAsync(installation, CancellationToken.None);
				throw;
			}
		}
	}

	private async Task ActivateAsync(Installation installation, DeliveryBundle bundle, CancellationToken cancellation)
	{
		try
		{
			await this.CheckInfrastructureAsync(bundle, cancellation);

			string[] kinds = ["application", "ingress"];
			foreach(var kind in kinds)
			{
				await this.PhaseAsync(installation, kind == "application" ? "StartApplications" : "StartIngress", async () =>
				{
					foreach(var service in Ordered(bundle.Plan).Where(service => service.Kind == kind))
					{
						await host.StartAsync(bundle, service, cancellation);
						await host.HealthyAsync(bundle, service, cancellation);
					}
				});
			}

			await this.PhaseAsync(installation, "CheckHealth", async () =>
			{
				foreach(var service in bundle.Plan.Services)
					await host.HealthyAsync(bundle, service, cancellation);
			});

			await host.RestorePoliciesAsync(bundle, cancellation);

			installation.Current = bundle.Id;
			installation.CurrentVersion = bundle.Plan.Version;
			installation.Status = "Installed";
			installation.Maintenance = false;

			var transaction = installation.Pending;
			transaction.Phase = "CommitRelease";
			transaction.Events.Add(new() { Phase = "CommitRelease", Result = "Succeeded" });

			Files.Save(Path.Combine(Path.GetDirectoryName(bundle.Directory), "history.json"), installation, ProtocolJson.Default.Installation);

			installation.Pending = null;
			installation.RestartPolicies.Clear();

			store.Save(installation);
		}
		catch(Exception exception)
		{
			await this.FailAsync(installation, exception);
			throw;
		}
	}

	private async Task CheckInfrastructureAsync(DeliveryBundle bundle, CancellationToken cancellation)
	{
		foreach(var service in Ordered(bundle.Plan).Where(service => service.Kind == "infrastructure"))
			await host.HealthyAsync(bundle, service, cancellation);
	}

	private async Task RestartAsync(Installation installation, string component, CancellationToken cancellation)
	{
		if(installation.Maintenance || installation.Pending != null || installation.Status != "Installed")
			throw new ContainerizationException(7, Properties.Resources.Lifecycle_14_Message);

		using var bundle = this.Current(installation);
		var services = Ordered(bundle.Plan).Where(service => component == null ? service.Kind != "infrastructure" : service.Id == component).ToArray();

		if(services.Length == 0)
			throw new ContainerizationException(2, Properties.Resources.Lifecycle_15_Message);

		foreach(var service in services)
			await host.RestartAsync(bundle, service, cancellation);
	}
	#endregion
}
