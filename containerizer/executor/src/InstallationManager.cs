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

internal sealed partial class InstallationManager(InstallationStore store, IInstallationHost host)
{
	#region 公共方法
	public async Task ExecuteAsync(ExecutorArguments arguments, CancellationToken cancellation)
	{
		if(arguments.Command == "list")
		{
			foreach(var item in store.List())
			{
				Console.WriteLine(string.Join('\t',
					item.Name,
					item.CurrentVersion ?? "-",
					item.Status,
					item.Pending?.Version ?? "-",
					item.IsInMaintenance ? "maintenance" : string.Empty));
			}

			return;
		}

		if(arguments.Command is "install" or "prepare" or "upgrade")
		{
			using var bundle = DeliveryBundle.Open(arguments.BundlePath);
			if(arguments.Name != null && arguments.Name != bundle.Plan.Name)
				throw new ContainerizationException(2, Properties.Resources.Lifecycle_1_Message);

			using var operationLock = store.AcquireApplicationLock(bundle.Plan.Name);
			await this.InstallAsync(bundle, arguments, cancellation);
			return;
		}

		var name = arguments.Name;
		if(arguments.SourceBundlePath != null)
		{
			using var source = DeliveryBundle.Open(arguments.SourceBundlePath);
			if(name != null && name != source.Plan.Name)
				throw new ContainerizationException(2, Properties.Resources.Lifecycle_2_Message);

			name = source.Plan.Name;
		}

		if(arguments.Command == "status")
		{
			var state = store.Load(name);
			Console.WriteLine(JsonSerializer.Serialize(state, ProtocolJson.Default.Installation));
			return;
		}

		if(arguments.Command == "logs")
		{
			await host.StreamLogsAsync(store.Load(name), arguments, cancellation);
			return;
		}

		using var held = store.AcquireApplicationLock(name);
		var installation = store.Load(name);

		switch(arguments.Command)
		{
			case "stop":
				await this.EnterMaintenanceAsync(installation, cancellation);
				installation.Status = "Maintenance";
				store.Save(installation);
				break;
			case "start":
				await this.StartAsync(installation, cancellation);
				break;
			case "recover":
				await this.RecoverAsync(installation, arguments.RetryMigrationVersion, cancellation);
				break;
			case "restart":
				await this.RestartAsync(installation, arguments.Component, cancellation);
				break;
			case "uninstall":
				await this.UninstallAsync(installation, arguments.Purge, cancellation);
				break;
		}
	}
	#endregion

	#region 私有方法
	private static void ValidateUpgrade(DeliveryPlan previous, DeliveryPlan next)
	{
		if(previous.Name != next.Name ||
		   previous.Distribution != next.Distribution ||
		   previous.Architecture != next.Architecture ||
		   previous.Project != next.Project ||
		   previous.DataRoot != next.DataRoot)
			throw new ContainerizationException(3, Properties.Resources.Lifecycle_18_Message);

		var before = previous.Services
			.Where(service => service.Kind == "infrastructure")
			.OrderBy(service => service.Id)
			.ToArray();

		var after = next.Services
			.Where(service => service.Kind == "infrastructure")
			.OrderBy(service => service.Id)
			.ToArray();

		if(before.Length != after.Length ||
		   before.Where((service, index) =>
			service.Id != after[index].Id ||
			service.Image.Id != after[index].Image.Id ||
			service.Image.Digest != after[index].Image.Digest ||
			service.ConfigurationHash != after[index].ConfigurationHash).Any())
			throw new ContainerizationException(3, Properties.Resources.Lifecycle_19_Message);

		var left = previous.Bootstrap.Packages
			.OrderBy(package => package.Name)
			.Select(package => $"{package.Name}/{package.Version}/{package.Hash}");
		var right = next.Bootstrap.Packages
			.OrderBy(package => package.Name)
			.Select(package => $"{package.Name}/{package.Version}/{package.Hash}");

		if(!left.SequenceEqual(right))
			throw new ContainerizationException(3, Properties.Resources.Lifecycle_20_Message);
	}

	private static IEnumerable<ServicePlan> GetServicesInDependencyOrder(DeliveryPlan plan)
	{
		var result = new List<ServicePlan>();
		var visiting = new HashSet<string>(StringComparer.Ordinal);
		var visited = new HashSet<string>(StringComparer.Ordinal);
		var services = plan.Services.ToDictionary(service => service.Id, StringComparer.Ordinal);

		foreach(var service in plan.Services)
			Visit(service.Id);

		return result;

		void Visit(string id)
		{
			if(visited.Contains(id))
				return;
			if(!services.TryGetValue(id, out var service) || !visiting.Add(id))
				throw new ContainerizationException(4, Properties.Resources.Lifecycle_21_Message);

			foreach(var dependency in service.Dependencies)
				Visit(dependency);

			visiting.Remove(id);
			visited.Add(id);
			result.Add(service);
		}
	}
	private DeliveryBundle OpenCurrentBundle(Installation installation) => installation.Current == null ?
		throw new ContainerizationException(7, Properties.Resources.Lifecycle_17_Message) :
		DeliveryBundle.Open(Path.Combine(store.GetApplicationPath(installation.Name), "releases", installation.Current, "assets"));
	#endregion
}
