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
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using System.Runtime.InteropServices;

using Zongsoft.Tools.Containerizer.Protocol;

namespace Zongsoft.Tools.Containerizer.Execution;

partial class DockerHost
{
	#region 公共方法
	public async Task ImagesAsync(DeliveryBundle bundle, CancellationToken cancellation)
	{
		foreach(var service in bundle.Plan.Services)
		{
			var image = service.Image;

			if(image.Mode == "offline")
				await this.RunAsync("docker", ["image", "load", "--input", Files.Below(bundle.Directory, image.Archive)], null, cancellation);
			else
			{
				await this.RunAsync("docker", ["pull", "--platform", image.Platform, $"{image.Repository}@{image.Digest}"], null, cancellation);
				await this.RunAsync("docker", ["tag", $"{image.Repository}@{image.Digest}", image.Tag], null, cancellation);
			}

			using var json = JsonDocument.Parse(await this.RunAsync("docker", ["image", "inspect", image.Mode == "offline" ? image.Id : image.Tag], null, cancellation));
			var actual = json.RootElement[0];

			if(actual.GetProperty("Id").GetString() != image.Id || $"{actual.GetProperty("Os").GetString()}/{actual.GetProperty("Architecture").GetString()}" != image.Platform)
				throw new ContainerizationException(4, Properties.Resources.DockerHost_14_Message);

			if(image.Mode == "offline")
				await this.RunAsync("docker", ["tag", image.Id, image.Tag], null, cancellation);
		}
	}

	public async Task StartAsync(DeliveryBundle bundle, ServicePlan service, CancellationToken cancellation)
	{
		var arguments = new List<string> { "up", "-d", "--no-deps", "--pull", "never", "--no-build" };

		if(service.Kind == "infrastructure")
			arguments.Add("--no-recreate");

		var temporary = new Dictionary<string, string[]>(StringComparer.Ordinal);

		if(service.Kind != "infrastructure" && service.Mounts.Any(mount => mount.Temporary))
		{
			arguments.Add("--renew-anon-volumes");

			foreach(var id in await this.ContainersAsync(bundle.Plan.Name, service.Id, cancellation))
			{
				using var json = JsonDocument.Parse(await this.RunAsync("docker", ["inspect", id], null, cancellation));

				temporary[id] = json.RootElement[0].GetProperty("Mounts").EnumerateArray()
					.Where(mount => mount.GetProperty("Type").GetString() == "volume" && service.Mounts.Any(item => item.Temporary && item.Target == mount.GetProperty("Destination").GetString()))
					.Select(mount => mount.GetProperty("Name").GetString())
					.Where(name => name?.Length == 64 && name.All(char.IsAsciiHexDigit)).Distinct(StringComparer.Ordinal).ToArray();
			}
		}

		arguments.Add(service.Id);
		await this.ComposeAsync(bundle, arguments, cancellation);
		var containers = await this.ContainersAsync(bundle.Plan.Name, service.Id, cancellation);

		// Compose may leave replaced anonymous volumes behind; remove only captured, unused volumes.
		foreach(var volume in temporary.Where(pair => !containers.Contains(pair.Key, StringComparer.Ordinal)).SelectMany(pair => pair.Value).Distinct(StringComparer.Ordinal))
		{
			var exists = await runner.RunAsync("docker", ["volume", "inspect", volume], null, cancellation);

			if(exists.ExitCode == 0)
				await this.RunAsync("docker", ["volume", "rm", volume], null, cancellation);
		}

		if(containers.Length != 1)
			throw new ContainerizationException(6, Properties.Resources.DockerHost_18_Message);

		await this.RunAsync("docker", ["update", $"--restart={(service.Kind == "infrastructure" ? service.Restart : "no")}", containers[0]], null, cancellation);
	}

	public async Task HealthyAsync(DeliveryBundle bundle, ServicePlan service, CancellationToken cancellation)
	{
		var deadline = DateTime.UtcNow.AddSeconds(service.Health.StartSeconds + service.Health.Retries * service.Health.IntervalSeconds + service.Health.TimeoutSeconds);

		while(true)
		{
			var ids = await this.ContainersAsync(bundle.Plan.Name, service.Id, cancellation);
			if(ids.Length != 1)
				throw new ContainerizationException(6, Properties.Resources.DockerHost_19_Message);

			using var json = JsonDocument.Parse(await this.RunAsync("docker", ["inspect", ids[0]], null, cancellation));
			var state = json.RootElement[0].GetProperty("State");

			if(!state.TryGetProperty("Health", out var health))
				throw new ContainerizationException(6, Properties.Resources.DockerHost_20_Message);
			if(state.GetProperty("Running").GetBoolean() && health.GetProperty("Status").GetString() == "healthy")
				return;
			if(DateTime.UtcNow >= deadline)
				throw new ContainerizationException(6, Properties.Resources.DockerHost_21_Message);

			await Task.Delay(TimeSpan.FromSeconds(Math.Max(1, service.Health.IntervalSeconds)), cancellation);
		}
	}

	public async Task StopApplicationsAsync(Installation installation, CancellationToken cancellation)
	{
		installation.Maintenance = true;
		store.Save(installation);

		foreach(var id in await this.ContainersAsync(installation.Name, null, cancellation))
		{
			using var json = JsonDocument.Parse(await this.RunAsync("docker", ["inspect", id], null, cancellation));
			var item = json.RootElement[0];
			var service = item.GetProperty("Config").GetProperty("Labels").GetProperty(SERVICE).GetString();
			var plans = this.Plans(installation).ToArray();

			if(!plans.SelectMany(plan => plan.Services).Any(plan => plan.Id == service && plan.Kind is "application" or "ingress"))
				continue;

			var policy = item.GetProperty("HostConfig").GetProperty("RestartPolicy");
			if(!installation.RestartPolicies.ContainsKey(service))
			{
				var name = policy.GetProperty("Name").GetString();
				var retries = policy.GetProperty("MaximumRetryCount").GetInt32();

				installation.RestartPolicies[service] = name == "on-failure" && retries > 0 ? $"{name}:{retries}" : name;
				store.Save(installation);
			}

			await this.RunAsync("docker", ["update", "--restart=no", id], null, cancellation);
			await this.RunAsync("docker", ["stop", id], null, cancellation);
		}
	}

	public async Task RestorePoliciesAsync(DeliveryBundle bundle, CancellationToken cancellation)
	{
		var installation = store.Load(bundle.Plan.Name);

		foreach(var service in bundle.Plan.Services)
		{
			foreach(var id in await this.ContainersAsync(bundle.Plan.Name, service.Id, cancellation))
				await this.RunAsync("docker", ["update", $"--restart={installation.RestartPolicies.GetValueOrDefault(service.Id, service.Restart)}", id], null, cancellation);
		}
	}

	public async Task RestartAsync(DeliveryBundle bundle, ServicePlan service, CancellationToken cancellation)
	{
		var ids = await this.ContainersAsync(bundle.Plan.Name, service.Id, cancellation);
		if(ids.Length != 1)
			throw new ContainerizationException(6, Properties.Resources.DockerHost_19_Message);

		await this.RunAsync("docker", ["restart", ids[0]], null, cancellation);
		await this.HealthyAsync(bundle, service, cancellation);
	}

	public async Task LogsAsync(Installation installation, ExecutorArguments arguments, CancellationToken cancellation)
	{
		var assets = installation.Current != null ? Path.Combine(store.GetApplicationPath(installation.Name), "releases", installation.Current, "assets") : installation.Pending?.Assets;

		if(assets == null)
			throw new ContainerizationException(2, Properties.Resources.DockerHost_24_Message);

		using var bundle = DeliveryBundle.Open(assets);
		if(arguments.Component != null && !bundle.Plan.Services.Any(service => service.Id == arguments.Component))
			throw new ContainerizationException(2, Properties.Resources.DockerHost_25_Message);

		var command = new List<string>
		{
			"logs",
			"--no-color",
			"--tail",
			arguments.Tail.ToString(System.Globalization.CultureInfo.InvariantCulture)
		};

		if(arguments.Follow)
			command.Add("--follow");
		if(arguments.Component != null)
			command.Add(arguments.Component);

		if(runner is ProcessRunner process)
		{
			var code = await ProcessRunner.StreamAsync("docker", ComposeArguments(bundle, command), bundle.Directory, cancellation);

			if(code != 0)
				throw new ContainerizationException(4, string.Format(Properties.Resources.DockerHost_27_Message, "docker", "compose logs", code));
		}
		else
			Console.Write(await this.ComposeAsync(bundle, command, cancellation));
	}
	#endregion

	#region 私有方法
	private async Task CheckPortsAsync(DeliveryBundle bundle, Installation installation, CancellationToken cancellation)
	{
		var owned = new List<PortPlan>();

		if(installation?.Current != null)
		{
			foreach(var id in await this.ContainersAsync(installation.Name, null, cancellation))
			{
				using var json = JsonDocument.Parse(await this.RunAsync("docker", ["inspect", id], null, cancellation));
				var item = json.RootElement[0];

				if(!item.GetProperty("State").GetProperty("Running").GetBoolean())
					continue;
				if(!item.GetProperty("HostConfig").TryGetProperty("PortBindings", out var bindings) || bindings.ValueKind != JsonValueKind.Object)
					continue;

				foreach(var port in bindings.EnumerateObject())
				{
					if(port.Value.ValueKind != JsonValueKind.Array)
						continue;

					foreach(var binding in port.Value.EnumerateArray())
						owned.Add(new()
						{
							Protocol = port.Name.Split('/')[1],
							Address = binding.GetProperty("HostIp").GetString(),
							Host = int.Parse(binding.GetProperty("HostPort").GetString(),
							System.Globalization.CultureInfo.InvariantCulture)
						});
				}
			}
		}

		foreach(var port in bundle.Plan.Services.SelectMany(service => service.Ports).Where(port => port.Host > 0))
		{
			if(owned.Any(item => item.Protocol == port.Protocol && item.Host == port.Host && (item.Address == port.Address || item.Address is "" or "0.0.0.0" or "::")))
				continue;

			try
			{
				var address = System.Net.IPAddress.Parse(port.Address);

				using var socket = new System.Net.Sockets.Socket(
					address.AddressFamily,
					port.Protocol == "udp" ? System.Net.Sockets.SocketType.Dgram : System.Net.Sockets.SocketType.Stream,
					port.Protocol == "udp" ? System.Net.Sockets.ProtocolType.Udp : System.Net.Sockets.ProtocolType.Tcp);

				socket.ExclusiveAddressUse = true;
				socket.Bind(new System.Net.IPEndPoint(address, port.Host));
			}
			catch(System.Net.Sockets.SocketException exception)
			{
				throw new ContainerizationException(3, Properties.Resources.DockerHost_31_Message, exception);
			}
		}
	}

	private async Task<string[]> ContainersAsync(string name, string service, CancellationToken cancellation)
	{
		var arguments = new List<string> { "ps", "-aq", "--filter", $"label={OWNER}={name}" };

		if(service != null)
			arguments.AddRange(["--filter", $"label={SERVICE}={service}"]);

		return Lines(await this.RunAsync("docker", arguments, null, cancellation));
	}

	private static string[] ComposeArguments(DeliveryBundle bundle, IReadOnlyList<string> command) => ["compose", "--project-name", bundle.Plan.Project, "--project-directory", bundle.Directory, "-f", Path.Combine(bundle.Directory, "compose.yaml"), .. command];

	private Task<string> ComposeAsync(DeliveryBundle bundle, IReadOnlyList<string> command, CancellationToken cancellation) => this.RunAsync("docker", ComposeArguments(bundle, command), bundle.Directory, cancellation);
	#endregion
}
