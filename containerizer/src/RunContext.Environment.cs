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
using System.Globalization;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Collections.Generic;

using Zongsoft.Tools.Containerizer.Protocol;

namespace Zongsoft.Tools.Containerizer;

internal sealed partial class RunContext
{
	#region 私有方法
	private async Task CheckArchitectureAsync(CancellationToken cancellation)
	{
		var format = _engine.Executable == ContainerEngine.PODMAN ? "{{.Host.OS}}/{{.Host.Arch}}" : "{{.OSType}}/{{.Architecture}}";
		var platform = (await _engine.RunAsync(["info", "--format", format], null, cancellation, 30)).Trim();
		var expected = _bundle.Plan.Architecture == "arm64" ? "linux/arm64" : "linux/amd64";

		if(platform.Replace("x86_64", "amd64", StringComparison.Ordinal).Replace("aarch64", "arm64", StringComparison.Ordinal) != expected)
			throw new ContainerizationException(3, string.Format(Properties.Resources.Run_Architecture_Message, expected, platform));
	}

	private async Task<string> PrepareBaseAsync(CancellationToken cancellation)
	{
		var recipe = Recipe(_bundle.Plan.Distribution);
		var key = Files.HashText($"{recipe}\n{_bundle.Plan.Architecture}");
		var image = $"localhost/containerizer-run-base:{key[..24]}";

		using var imageLock = BuildStorage.Lock(Path.Combine(BuildStorage.CacheRoot, "run-base", key));
		var cached = await _engine.RunAsync(["image", "ls", "--filter", $"reference={image}", "--format", "{{.ID}}"], null, cancellation);

		if(!string.IsNullOrWhiteSpace(cached))
		{
			using var json = JsonDocument.Parse(await _engine.InspectAsync(image, cancellation));
			if(json.RootElement[0].GetProperty("Config").GetProperty("Labels").TryGetProperty(OWNER_LABEL, out var label) && label.GetString() == key)
				return image;

			throw new ContainerizationException(4, string.Format(Properties.Resources.Run_BaseConflict_Message, image));
		}

		_output(Output.Message(Properties.Resources.Run_Prepare, _bundle.Plan.Distribution, _bundle.Plan.Architecture));

		if(!_engine.Mirrors.IsEmpty)
		{
			var baseline = await _engine.ResolveAsync(BootstrapPackageBuilder.BaseImage(_bundle.Plan.Distribution), null, null, _bundle.Plan.Architecture, cancellation);
			recipe = Recipe(_bundle.Plan.Distribution, _engine.BuildReference(baseline));
		}

		Files.Write(Path.Combine(_workspace, "Dockerfile"), $"{recipe}\nLABEL {OWNER_LABEL}={key}\n", true);
		await _engine.BuildAsync(_workspace, _bundle.Plan.Architecture == "arm64" ? "linux/arm64" : "linux/amd64", image, cancellation);

		return image;
	}

	internal static string Recipe(string distribution, string baseline = null)
	{
		var packages = distribution switch
		{
			"ubuntu@22.04" => "libicu70",
			"debian@12" => "libicu72",
			"debian@13" => "libicu76",
			_ => "libicu",
		};

		var install = Distribution.IsDebian(distribution) ?
			$"apt-get update && DEBIAN_FRONTEND=noninteractive apt-get install -y --no-install-recommends systemd systemd-sysv dbus iproute2 procps socat ca-certificates curl {packages} && apt-get clean && rm -rf /var/lib/apt/lists/*" :
			$"dnf install -y systemd dbus iproute procps-ng socat ca-certificates curl-minimal {packages} && dnf clean all";

		return $$$"""
			FROM {{{baseline ?? BootstrapPackageBuilder.BaseImage(distribution)}}}
			ENV container=docker
			RUN {{{install}}}
			RUN mkdir -p /etc/docker && printf '%s\n' '{"storage-driver":"overlay2","features":{"containerd-snapshotter":false}}' > /etc/docker/daemon.json
			STOPSIGNAL SIGRTMIN+3
			CMD ["/sbin/init"]
			""";
	}

	private async Task StartAsync(string image, CancellationToken cancellation)
	{
		for(var attempt = 0; attempt < 2; attempt++)
		{
			List<string> arguments =
			[
				"create", "--name", this.Name, "--label", $"{OWNER_LABEL}={_identity}", "--label", $"{APPLICATION_LABEL}={_bundle.Plan.Name}",
				"--privileged", "--cgroupns=private", "--tmpfs", "/run", "--tmpfs", "/run/lock", "--tmpfs", "/tmp",
				"--volume", $"{_cache.Name}:/var/lib/docker", "--volume", "/var/lib/containerd",
			];

			foreach(var endpoint in _endpoints)
			{
				var port = attempt == 0 ? Endpoint.AvailablePort(endpoint.Port.Host, endpoint.IsWeb) : 0;
				arguments.AddRange(["--publish", $"{endpoint.BindAddress}:{(port == 0 ? "" : port.ToString(System.Globalization.CultureInfo.InvariantCulture))}:{endpoint.Relay}/tcp"]);
			}

			arguments.Add(image);
			await _engine.RunAsync(arguments, null, cancellation);
			_created = true;

			try
			{
				await _engine.RunAsync(["start", this.Name], null, cancellation);
				await _engine.RunAsync(["exec", this.Name, "rm", "-f", ImageCache.MARKER], null, cancellation);
				return;
			}
			catch(ContainerizationException exception) when(attempt == 0 && IsPortConflict(exception.Message))
			{
				using var cleanup = new CancellationTokenSource(TimeSpan.FromSeconds(60));
				await this.RemoveContainersAsync(cleanup.Token);
			}
		}
	}

	internal static bool IsPortConflict(string message) =>
		message.Contains("address already in use", StringComparison.OrdinalIgnoreCase) ||
		message.Contains("port is already allocated", StringComparison.OrdinalIgnoreCase) ||
		message.Contains("ports are not available", StringComparison.OrdinalIgnoreCase);

	private async Task InstallAsync(CancellationToken cancellation)
	{
		using(Output.Measure(_output, Properties.Resources.Run_StageCopy))
		{
			await _engine.RunAsync(["cp", _bundle.Directory, $"{this.Name}:/delivery"], null, cancellation);

			if(!_engine.Mirrors.IsEmpty)
			{
				var path = Path.Combine(_workspace, "mirrors.json");
				Files.Save(path, _engine.Mirrors, ProtocolJson.Default.RegistryMirrors);
				await _engine.RunAsync(["cp", path, $"{this.Name}:/run/containerizer-mirrors.json"], null, cancellation);
			}
		}

		await _engine.RunAsync(["exec", this.Name, "chmod", "+x", "/delivery/containerizer"], null, cancellation);

		_output(Output.Message(Properties.Resources.Run_Install, _bundle.Plan.Name));
		var culture = CultureInfo.CurrentUICulture.Name;
		var locale = string.IsNullOrEmpty(culture) ? "C.UTF-8" : $"{culture.Replace('-', '_')}.UTF-8";
		List<string> arguments = ["exec", "--env", $"LANG={locale}"];

		if(!_engine.Mirrors.IsEmpty)
			arguments.AddRange(["--env", $"{RegistryMirrors.ENVIRONMENT}=/run/containerizer-mirrors.json"]);

		arguments.AddRange(["--workdir", "/delivery", this.Name, "sh", "/delivery/install.sh"]);
		var result = await _stream(_engine.Executable, arguments, null, cancellation);

		if(result != 0)
			throw new ContainerizationException(4, string.Format(Properties.Resources.Run_InstallFailed_Message, result));
	}

	private async Task ForwardAsync(CancellationToken cancellation)
	{
		using var json = JsonDocument.Parse(await _engine.RunAsync(["inspect", this.Name], null, cancellation));
		var ports = json.RootElement[0].GetProperty("NetworkSettings").GetProperty("Ports");
		var infrastructure = _endpoints.Where(endpoint => endpoint.Service.Kind == "infrastructure").ToArray();
		var hosts = _bundle.Plan.Services.Where(service => service.Kind != "infrastructure").ToArray();
		var forwarded = new HashSet<Endpoint>();

		_output(Output.Message(string.Empty));

		foreach(var endpoint in infrastructure)
		{
			if(await this.ForwardEndpointAsync(endpoint, ports, cancellation))
				this.ReportEndpoint(endpoint, endpoint.Service.Id);
		}

		if(infrastructure.Length > 0 && hosts.Length > 0)
			_output(Output.Message(string.Empty));

		foreach(var service in hosts)
		{
			foreach(var endpoint in _endpoints.Where(endpoint => endpoint.IsWeb ?
				endpoint.Service.Web.Any(site => site.Application == service.Id && site.Bindings.Any(binding => binding.Publication == endpoint.Port.Name)) :
				endpoint.Service == service))
			{
				if(forwarded.Add(endpoint) && !await this.ForwardEndpointAsync(endpoint, ports, cancellation))
					continue;

				this.ReportEndpoint(endpoint, service.Id);
			}

			if(service.Kind == "application" && service.Ports.Count == 0 && !_bundle.Plan.Services.Any(ingress => ingress.Web.Any(site => site.Application == service.Id)))
				_output(Output.Message(Properties.Resources.Run_Daemon, service.Id));
		}

		var unknown = _bundle.Plan.Services.Where(service => service.Kind == "application" && service.Ports.Count > 0 && service.Web.Count == 0).ToArray();

		if(unknown.Length != 0)
			throw new ContainerizationException(4, string.Format(Properties.Resources.Run_WebMetadata_Message, string.Join(", ", unknown.Select(service => service.Id))));
	}

	private async Task<bool> ForwardEndpointAsync(Endpoint endpoint, JsonElement ports, CancellationToken cancellation)
	{
		try
		{
			endpoint.Bind(ports);

			await _engine.RunAsync(["exec", this.Name, "systemd-run", "--unit", $"containerizer-forward-{endpoint.Relay}",
				"--property", "Restart=on-failure", "/usr/bin/socat", $"TCP4-LISTEN:{endpoint.Relay},fork,reuseaddr", endpoint.Target], null, cancellation);

			await endpoint.ProbeAsync(cancellation);

			return true;
		}
		catch(Exception exception) when(!cancellation.IsCancellationRequested)
		{
			if(endpoint.IsWeb)
				throw new ContainerizationException(4, string.Format(Properties.Resources.Run_WebFailed_Message, endpoint.Address, exception.Message), exception);

			_output(Output.Message(Properties.Resources.Run_ForwardFailed, Zongsoft.Components.CommandOutletColor.Magenta, endpoint.Service.Id, exception.Message));
			return false;
		}
	}

	private void ReportEndpoint(Endpoint endpoint, string application)
	{
		if(endpoint.IsWeb)
		{
			foreach(var result in endpoint.Results.Where(result => result.Application == application))
			{
				_output(Output.Message("  {0}: {1}", result.Site, result.Address));
				if(result.Redirect != null)
					_output(Output.Message(Properties.Resources.Run_Redirect_Message, result.Redirect));
			}

			_output(Output.Message(Properties.Resources.Run_WebBrowser, endpoint.Host.ToString(CultureInfo.InvariantCulture)));
			var addresses = endpoint.GetLocalAddresses();
			if(addresses.Length > 0)
				_output(Output.Message(Properties.Resources.Run_WebInterfaces, string.Join(", ", addresses)));
		}
		else
			_output(Output.Message("  {0}: {1}", endpoint.Service.Id, endpoint.Address));

		if(endpoint.Service.Kind == "ingress" && !endpoint.IsWeb)
			_output(Output.Message($"  {Properties.Resources.Run_UnknownEntry}", Zongsoft.Components.CommandOutletColor.Magenta, endpoint.Service.Id));
	}
	#endregion
}
