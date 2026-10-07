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
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Runtime.InteropServices;

using Zongsoft.Tools.Containerizer.Protocol;

namespace Zongsoft.Tools.Containerizer.Execution;

partial class DockerHost
{
	#region 公共方法
	public async Task PrepareImagesAsync(DeliveryBundle bundle, CancellationToken cancellation)
	{
		var sources = mirrors ?? ReadRuntimeMirrors();
		foreach(var service in bundle.Plan.Services)
		{
			var image = service.Image;
			var timer = Stopwatch.StartNew();
			var imageInspection = await runner.RunAsync(ENGINE, ["image", "inspect", image.Id], null, cancellation, 30);

			if(imageInspection.ExitCode == 0)
			{
				VerifyImageIdentity(imageInspection.Output, image);
				Console.WriteLine(string.Format(Properties.Resources.Run_ImageCached, service.Id));
			}
			else if(image.Mode == "offline")
				await this.RunCommandAsync(ENGINE, ["image", "load", "--input", Files.ResolveRelativePath(bundle.Directory, image.Archive)], null, cancellation);
			else
			{
				await sources.ExecuteAsync(image.Repository, async repository =>
				{
					var reference = $"{repository}@{image.Digest}";
					await this.RunCommandAsync(ENGINE, ["pull", "--platform", image.Platform, reference], null, cancellation);
					VerifyImageIdentity(await this.RunCommandAsync(ENGINE, ["image", "inspect", reference], null, cancellation), image, repository);
					return true;
				}, cancellation, (repository, exception) => Console.Error.WriteLine(string.Format(Properties.Resources.Mirrors_SourceFailed, repository, exception.Message)));
			}

			if(imageInspection.ExitCode != 0)
			{
				VerifyImageIdentity(await this.RunCommandAsync(ENGINE, ["image", "inspect", image.Id], null, cancellation), image);
				Console.WriteLine(string.Format(Properties.Resources.Run_ImagePrepared, service.Id, timer.Elapsed.TotalSeconds.ToString("0.0", System.Globalization.CultureInfo.CurrentCulture)));
			}

			await this.RunCommandAsync(ENGINE, ["tag", image.Id, image.Reference], null, cancellation);
		}
	}

	private static void VerifyImageIdentity(string metadata, ImagePlan image, string repository = null)
	{
		using var json = JsonDocument.Parse(metadata);
		var actual = json.RootElement[0];
		if(actual.GetProperty("Id").GetString() != image.Id || $"{actual.GetProperty("Os").GetString()}/{actual.GetProperty("Architecture").GetString()}" != image.Platform)
			throw new ContainerizationException(4, Properties.Resources.DockerHost_14_Message);

		if(repository != null && (!actual.TryGetProperty("RepoDigests", out var digests) || digests.ValueKind != JsonValueKind.Array || !digests.EnumerateArray().Any(value => value.GetString() == $"{repository}@{image.Digest}")))
			throw new ContainerizationException(4, Properties.Resources.DockerHost_14_Message);
	}

	public async Task StartServiceAsync(DeliveryBundle bundle, ServicePlan service, CancellationToken cancellation)
	{
		var arguments = new List<string> { "up", "-d", "--no-deps", "--pull", "never", "--no-build" };

		if(service.Kind == "infrastructure")
			arguments.Add("--no-recreate");

		var temporaryVolumes = new Dictionary<string, string[]>(StringComparer.Ordinal);

		if(service.Kind != "infrastructure" && service.Mounts.Any(mount => mount.Temporary))
		{
			arguments.Add("--renew-anon-volumes");

			foreach(var id in await this.GetContainerIdsAsync(bundle.Plan.Name, service.Id, cancellation))
			{
				using var json = JsonDocument.Parse(await this.RunCommandAsync(ENGINE, ["inspect", id], null, cancellation));

				temporaryVolumes[id] = json.RootElement[0].GetProperty("Mounts").EnumerateArray()
					.Where(mount => mount.GetProperty("Type").GetString() == "volume" && service.Mounts.Any(item => item.Temporary && item.Target == mount.GetProperty("Destination").GetString()))
					.Select(mount => mount.GetProperty("Name").GetString())
					.Where(name => name?.Length == 64 && name.All(char.IsAsciiHexDigit)).Distinct(StringComparer.Ordinal).ToArray();
			}
		}

		arguments.Add(service.Id);
		await this.RunComposeAsync(bundle, arguments, cancellation);
		var containerIds = await this.GetContainerIdsAsync(bundle.Plan.Name, service.Id, cancellation);

		// Compose may leave replaced anonymous volumes behind; remove only captured, unused volumes.
		foreach(var volume in temporaryVolumes.Where(pair => !containerIds.Contains(pair.Key, StringComparer.Ordinal)).SelectMany(pair => pair.Value).Distinct(StringComparer.Ordinal))
		{
			var volumeInspection = await runner.RunAsync(ENGINE, ["volume", "inspect", volume], null, cancellation);

			if(volumeInspection.ExitCode == 0)
				await this.RunCommandAsync(ENGINE, ["volume", "rm", volume], null, cancellation);
		}

		if(containerIds.Length != 1)
			throw new ContainerizationException(6, Properties.Resources.DockerHost_18_Message);

		await this.RunCommandAsync(ENGINE, ["update", $"--restart={(service.Kind == "infrastructure" ? service.Restart : "no")}", containerIds[0]], null, cancellation);
	}

	public async Task WaitForHealthAsync(DeliveryBundle bundle, ServicePlan service, CancellationToken cancellation)
	{
		var deadline = DateTime.UtcNow.AddSeconds(service.Health.StartSeconds + service.Health.Retries * service.Health.IntervalSeconds + service.Health.TimeoutSeconds);

		while(true)
		{
			var containerIds = await this.GetContainerIdsAsync(bundle.Plan.Name, service.Id, cancellation);
			if(containerIds.Length != 1)
				throw new ContainerizationException(6, string.Format(Properties.Resources.DockerHost_19_Message, service.Id));

			using var json = JsonDocument.Parse(await this.RunCommandAsync(ENGINE, ["inspect", containerIds[0]], null, cancellation));
			var state = json.RootElement[0].GetProperty("State");

			if(!state.TryGetProperty("Health", out var health))
				throw new ContainerizationException(6, string.Format(Properties.Resources.DockerHost_20_Message, service.Id));
			if(state.GetProperty("Running").GetBoolean() && health.GetProperty("Status").GetString() == "healthy")
				return;
			if(DateTime.UtcNow >= deadline)
				throw new ContainerizationException(6, string.Format(Properties.Resources.DockerHost_21_Message, service.Id, containerIds[0]));

			await Task.Delay(TimeSpan.FromSeconds(Math.Max(1, service.Health.IntervalSeconds)), cancellation);
		}
	}

	public async Task StopApplicationsAsync(Installation installation, CancellationToken cancellation)
	{
		var serviceIds = this.ReadReleasePlans(installation).SelectMany(plan => plan.Services)
			.Where(service => service.Kind is "application" or "ingress")
			.Select(service => service.Id).ToHashSet(StringComparer.Ordinal);

		foreach(var id in await this.GetContainerIdsAsync(installation.Name, null, cancellation))
		{
			using var json = JsonDocument.Parse(await this.RunCommandAsync(ENGINE, ["inspect", id], null, cancellation));
			var item = json.RootElement[0];
			var service = item.GetProperty("Config").GetProperty("Labels").GetProperty(SERVICE_LABEL).GetString();

			if(!serviceIds.Contains(service))
				continue;

			var policy = item.GetProperty("HostConfig").GetProperty("RestartPolicy");
			if(!installation.RestartPolicies.ContainsKey(service))
			{
				var name = policy.GetProperty("Name").GetString();
				var retries = policy.GetProperty("MaximumRetryCount").GetInt32();

				installation.RestartPolicies[service] = name == "on-failure" && retries > 0 ? $"{name}:{retries}" : name;
				store.Save(installation);
			}

			await this.RunCommandAsync(ENGINE, ["update", "--restart=no", id], null, cancellation);
			await this.RunCommandAsync(ENGINE, ["stop", id], null, cancellation);
		}
	}

	public async Task RestoreRestartPoliciesAsync(DeliveryBundle bundle, CancellationToken cancellation)
	{
		var installation = store.Load(bundle.Plan.Name);

		foreach(var service in bundle.Plan.Services)
		{
			foreach(var id in await this.GetContainerIdsAsync(bundle.Plan.Name, service.Id, cancellation))
				await this.RunCommandAsync(ENGINE, ["update", $"--restart={installation.RestartPolicies.GetValueOrDefault(service.Id, service.Restart)}", id], null, cancellation);
		}
	}

	public async Task RestartServiceAsync(DeliveryBundle bundle, ServicePlan service, CancellationToken cancellation)
	{
		var containerIds = await this.GetContainerIdsAsync(bundle.Plan.Name, service.Id, cancellation);
		if(containerIds.Length != 1)
			throw new ContainerizationException(6, string.Format(Properties.Resources.DockerHost_19_Message, service.Id));

		await this.RunCommandAsync(ENGINE, ["restart", containerIds[0]], null, cancellation);
		await this.WaitForHealthAsync(bundle, service, cancellation);
	}

	public async Task StreamLogsAsync(Installation installation, ExecutorArguments arguments, CancellationToken cancellation)
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
			arguments.TailLines.ToString(System.Globalization.CultureInfo.InvariantCulture)
		};

		if(arguments.Follow)
			command.Add("--follow");
		if(arguments.Component != null)
			command.Add(arguments.Component);

		var code = await runner.StreamAsync(ENGINE, GetComposeArguments(bundle, command), bundle.Directory, cancellation);

		if(code != 0)
			throw new ContainerizationException(4, string.Format(Properties.Resources.DockerHost_27_Message, ENGINE, "compose logs", code));
	}
	#endregion

	#region 私有方法
	private static RegistryMirrors ReadRuntimeMirrors()
	{
		var path = Environment.GetEnvironmentVariable(RegistryMirrors.ENVIRONMENT_VARIABLE);
		if(string.IsNullOrEmpty(path))
			return new();

		var sources = Files.Load(path, ProtocolJson.Default.RegistryMirrors);
		if(sources == null)
			throw new ContainerizationException(2, Properties.Resources.Mirrors_Invalid_Message);
		sources.Validate();
		return sources;
	}

	private async Task CheckPortAvailabilityAsync(DeliveryBundle bundle, Installation installation, CancellationToken cancellation)
	{
		var ownedPorts = new List<PortPlan>();

		if(installation?.Current != null)
		{
			foreach(var id in await this.GetContainerIdsAsync(installation.Name, null, cancellation))
			{
				using var json = JsonDocument.Parse(await this.RunCommandAsync(ENGINE, ["inspect", id], null, cancellation));
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
						ownedPorts.Add(new()
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
			if(ownedPorts.Any(item => item.Protocol == port.Protocol && item.Host == port.Host && (item.Address == port.Address || item.Address is "" or "0.0.0.0" or "::")))
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

	private async Task<string[]> GetContainerIdsAsync(string name, string service, CancellationToken cancellation)
	{
		var arguments = new List<string> { "ps", "-aq", "--filter", $"label={OWNER_LABEL}={name}" };

		if(service != null)
			arguments.AddRange(["--filter", $"label={SERVICE_LABEL}={service}"]);

		return SplitLines(await this.RunCommandAsync(ENGINE, arguments, null, cancellation));
	}

	private static string[] GetComposeArguments(DeliveryBundle bundle, IReadOnlyList<string> command) => ["compose", "--project-name", bundle.Plan.Project, "--project-directory", bundle.Directory, "-f", Path.Combine(bundle.Directory, "compose.yaml"), .. command];

	private Task<string> RunComposeAsync(DeliveryBundle bundle, IReadOnlyList<string> command, CancellationToken cancellation) => this.RunCommandAsync(ENGINE, GetComposeArguments(bundle, command), bundle.Directory, cancellation);
	#endregion
}
