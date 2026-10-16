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
using System.Net;
using System.Linq;
using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace Zongsoft.Tools.Containerizer.Protocol;

internal sealed partial class ContainerBundle : IDisposable
{
	#region 成员字段
	private readonly bool _temporary;
	#endregion

	#region 构造函数
	private ContainerBundle(string directory, bool temporary)
	{
		_temporary = temporary;
		this.Directory = directory;
		this.Plan = Files.Load(Path.Combine(directory, ContainerBundlePlan.FileName), ProtocolJson.Default.ContainerBundlePlan);
		this.Id = Files.Hash(Path.Combine(directory, ContainerBundlePlan.FileName));
		this.Verify();
	}
	#endregion

	#region 公共属性
	public string Id { get; }
	public string Directory { get; }
	public ContainerBundlePlan Plan { get; }
	#endregion

	#region 公共方法
	public static ContainerBundle Open(string path)
	{
		path = Path.GetFullPath(path);
		Files.EnsureNoLinks(path);

		if(System.IO.Directory.Exists(path))
			return new(path, false);

		if(!File.Exists(path) || !path.EndsWith(".tar.gz", StringComparison.OrdinalIgnoreCase))
			throw new ContainerizationException(2, Properties.Resources.Bundle_1_Message);

		var directory = Path.Combine(Path.GetTempPath(), $"containerizer-{Guid.NewGuid().ToString("N")}");
		Files.CreatePrivateDirectory(directory);

		try
		{
			Files.Extract(path, directory);
			return new(directory, true);
		}
		catch { System.IO.Directory.Delete(directory, true); throw; }
	}

	public static void ValidateIdentity(string name)
	{
		if(!GetIdentityRegex().IsMatch(name ?? "") || name.Contains("..", StringComparison.Ordinal))
			throw new ContainerizationException(2, Properties.Resources.Bundle_16_Message);
	}

	public static string ValidateBootstrapRepository(BootstrapPlan plan, string directory)
	{
		if(plan.Requirements == null || plan.Requirements.Count == 0 ||
			plan.Requirements.Distinct(StringComparer.Ordinal).Count() != plan.Requirements.Count ||
			plan.Requirements.Any(IsInvalidRequirement))
			throw new ContainerizationException(4, Properties.Resources.Bundle_3_Message);

		var repository = Files.ResolveRelativePath(directory, plan.Repository);
		if(!File.Exists(Path.Combine(repository, "repomd.xml")))
			throw new ContainerizationException(4, Properties.Resources.Bundle_3_Message);

		Files.ValidateTree(repository);
		return repository;

		bool IsInvalidRequirement(string name) =>
			string.IsNullOrEmpty(name) || !char.IsAsciiLetterOrDigit(name[0]) ||
			name.Any(character => !char.IsAsciiLetterOrDigit(character) && character is not ('.' or '_' or '+' or '-')) ||
			!plan.Packages.Any(package => package.Name == name);
	}

	public static void ValidateLinuxPath(string path)
	{
		if(!Files.IsLinuxPath(path))
			throw new ContainerizationException(4, Properties.Resources.Bundle_17_Message);
	}
	#endregion

	#region 私有方法
	private void Verify()
	{
		if(this.Plan.Schema != ContainerBundlePlan.ProtocolVersion)
			throw new ContainerizationException(3, Properties.Resources.Bundle_2_Message);

		ValidateIdentity(this.Plan.Name);

		if(this.Plan.Project != $"containerizer-{this.Plan.Name.ToLowerInvariant().Replace('.', '-').Replace('_', '-')}-{Files.HashText(this.Plan.Name)[..8]}")
			throw new ContainerizationException(4, Properties.Resources.Bundle_3_Message);

		if(this.Plan.Architecture is not ("x64" or "arm64") ||
		   this.Plan.Services.Count == 0 ||
		   this.Plan.DataRoot != Installation.Paths.GetDataPath(this.Plan.Name) ||
		   string.IsNullOrEmpty(this.Plan.Project) || !GetProjectRegex().IsMatch(this.Plan.Project))
			throw new ContainerizationException(4, Properties.Resources.Bundle_3_Message);

		var records = new Dictionary<string, ContainerBundlePlan.FileEntry>(StringComparer.OrdinalIgnoreCase);
		ValidateFiles();
		ValidateChecksums();
		ValidateServices();
		ValidatePorts();
		ValidateMigrations();

		void ValidateFiles()
		{
			foreach(var file in this.Plan.Files)
			{
				if(!records.TryAdd(file.Path, file) || !GetHashRegex().IsMatch(file.Hash ?? ""))
					throw new ContainerizationException(4, Properties.Resources.Bundle_4_Message);

				var path = Files.ResolveRelativePath(this.Directory, file.Path);
				if(!File.Exists(path) || new FileInfo(path).Length != file.Length || Files.Hash(path) != file.Hash)
					throw new ContainerizationException(4, string.Format(Properties.Resources.Bundle_5_Message, file.Path));
			}

			string[] requiredFiles = ["containerizer", "compose.yaml", "install.sh", "uninstall.sh"];
			foreach(var required in requiredFiles)
			{
				if(!records.ContainsKey(required))
					throw new ContainerizationException(4, string.Format(Properties.Resources.Bundle_6_Message, required));
			}
		}

		void ValidateChecksums()
		{
			var checksums = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
			foreach(var line in File.ReadLines(Path.Combine(this.Directory, "checksums.sha256")))
			{
				if(line.Length < 67 || line.Substring(64, 2) != "  " || !checksums.TryAdd(line[66..], line[..64]))
					throw new ContainerizationException(4, Properties.Resources.Bundle_7_Message);
			}

			if(checksums.Count != records.Count + 1 ||
				checksums.GetValueOrDefault(ContainerBundlePlan.FileName) != this.Id ||
				records.Any(pair => checksums.GetValueOrDefault(pair.Key) != pair.Value.Hash))
				throw new ContainerizationException(4, Properties.Resources.Bundle_8_Message);

			foreach(var file in Files.EnumerateFiles(this.Directory))
			{
				var relative = Path.GetRelativePath(this.Directory, file).Replace('\\', '/');
				if(relative is not (ContainerBundlePlan.FileName or "checksums.sha256") && !records.ContainsKey(relative))
					throw new ContainerizationException(4, Properties.Resources.Bundle_9_Message);
			}
		}

		void ValidateServices()
		{
			var services = new HashSet<string>(StringComparer.Ordinal);
			foreach(var service in this.Plan.Services)
				ValidateBindingStructure(service);

			foreach(var service in this.Plan.Services)
			{
				ValidateIdentity(service.Id);

				if(!services.Add(service.Id) || service.Kind is not ("application" or "infrastructure" or "ingress"))
					throw new ContainerizationException(4, Properties.Resources.Bundle_10_Message);
				if(service.Image.Platform != (this.Plan.Architecture == "arm64" ? "linux/arm64" : "linux/amd64") ||
					!GetDigestRegex().IsMatch(service.Image.Id ?? "") || service.Image.Mode is not ("online" or "offline"))
					throw new ContainerizationException(4, Properties.Resources.Bundle_11_Message);
				if(!service.Image.Reference.StartsWith($"containerizer/{this.Plan.Project}/{service.Id}:", StringComparison.Ordinal))
					throw new ContainerizationException(4, Properties.Resources.Bundle_11_Message);
				if(service.Image.Mode == "offline" && !records.ContainsKey(service.Image.Archive ?? ""))
					throw new ContainerizationException(4, Properties.Resources.Bundle_12_Message);
				if(service.Kind == "application" && service.Image.Mode != "offline")
					throw new ContainerizationException(4, Properties.Resources.Bundle_13_Message);

				foreach(var port in service.Ports)
				{
					if(port.Host is < 1 or > 65535 || port.Container is < 1 or > 65535 ||
					   !IPAddress.TryParse(port.Address, out _) || port.Protocol is not ("tcp" or "udp"))
						throw new ContainerizationException(4, Properties.Resources.Bundle_3_Message);
				}

				ValidateWeb(service, this.Plan.Services);
				ValidateBindings(service, this.Plan.Services);

				foreach(var mount in service.Mounts)
				{
					ValidateLinuxPath(mount.Target);

					if(mount.ReadOnly)
						Files.ResolveRelativePath(this.Directory, mount.Source);
					else
						ValidateLinuxPath(mount.Source);
				}
			}
		}

		void ValidatePorts()
		{
			var ports = this.Plan.Services.SelectMany(service => service.Ports).ToArray();
			for(int index = 0; index < ports.Length; index++)
			{
				if(ports.Skip(index + 1).Any(port => ServicePlan.PortMapping.Conflicts(ports[index], port)))
					throw new ContainerizationException(4, Properties.Resources.Bundle_3_Message);
			}
		}

		void ValidateMigrations()
		{
			var versions = new HashSet<Version>();
			foreach(var migration in this.Plan.Migrations)
			{
				if(!Version.TryParse(migration.Version, out var version) || !versions.Add(version) ||
				   !records.TryGetValue(migration.Archive, out var archive) ||
				   !records.TryGetValue(migration.Script, out var script))
					throw new ContainerizationException(4, Properties.Resources.Bundle_14_Message);
				if(Files.HashText($"{archive.Hash}{script.Hash}") != migration.Identity)
					throw new ContainerizationException(4, Properties.Resources.Bundle_15_Message);
			}
		}
	}

	private static void ValidateWeb(ServicePlan service, IReadOnlyList<ServicePlan> services)
	{
		var sites = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
		if(service.Ports.Where(port => port.Name != null).GroupBy(port => port.Name, StringComparer.Ordinal).Any(group => group.Count() > 1))
			throw new ContainerizationException(4, Properties.Resources.Bundle_3_Message);

		foreach(var site in service.Web)
		{
			if(!OwnsApplication(site) || string.IsNullOrWhiteSpace(site.Name) || !sites.Add(site.Application + "/" + site.Name))
				throw new ContainerizationException(4, Properties.Resources.Bundle_3_Message);
			if(!HasValidListeners(site))
				throw new ContainerizationException(4, Properties.Resources.Bundle_3_Message);

			ValidateListeners(site);
		}

		if(service.Kind == "ingress")
		{
			foreach(var group in service.Web.SelectMany(site => site.Bindings).GroupBy(binding => (binding.Protocol, binding.Address, binding.Port)))
			{
				if(group.Count(binding => binding.IsDefault) != 1 ||
				   group.Count(binding => binding.IsExplicitDefault) > 1 ||
				   group.Any(binding => binding.IsExplicitDefault && !binding.IsDefault) ||
				   group.Select(binding => binding.Scheme).Distinct().Count() != 1 ||
				   group.Select(binding => binding.Publication).Distinct().Count() != 1)
					throw new ContainerizationException(4, Properties.Resources.Bundle_3_Message);
			}
		}

		void ValidateListeners(ServicePlan.WebSite site)
		{
			foreach(var binding in site.Bindings)
			{
				if(IsInvalidAddress(binding))
					throw new ContainerizationException(4, Properties.Resources.Bundle_3_Message);

				var application = services.Single(item => item.Id == site.Application);
				var declared = application.Bindings.FirstOrDefault(item => item.Id == binding.Binding && item.Owner == "nginx");
				var endpoint = declared?.Endpoints.FirstOrDefault(item => item.Id == binding.Endpoint);

				if(IsEndpointMismatch(endpoint, binding))
					throw new ContainerizationException(4, Properties.Resources.Bundle_3_Message);

				if(binding.Publication == null)
					continue;

				if(IsInvalidPublication(site, binding))
					throw new ContainerizationException(4, Properties.Resources.Bundle_3_Message);

				if(IsUnpairedIPv6(site, binding))
					throw new ContainerizationException(4, Properties.Resources.Bundle_3_Message);
			}

			static bool IsInvalidAddress(ServicePlan.WebSite.BindingInfo binding)
			{
				return binding.Scheme is not ("http" or "https") || binding.Protocol is not ("tcp" or "udp") ||
					binding.Protocol == "udp" && binding.Scheme != "https" || !IPAddress.TryParse(binding.Address, out _) || binding.Port is < 1 or > 65535;
			}

			static bool IsEndpointMismatch(ServicePlan.Binding.Endpoint endpoint, ServicePlan.WebSite.BindingInfo binding)
			{
				return endpoint == null ||
					endpoint.Scheme != binding.Scheme ||
					endpoint.Host != binding.Address ||
					endpoint.Port != binding.Port ||
					!endpoint.Transports.Contains(binding.Protocol);
			}

			bool IsInvalidPublication(ServicePlan.WebSite site, ServicePlan.WebSite.BindingInfo binding)
			{
				return binding.Protocol == "tcp" && site.ProbeHosts.Count == 0 || binding.Address is not ("0.0.0.0" or "::") ||
					!service.Ports.Any(port =>
						port.Name == binding.Publication && port.Container == binding.Port && port.Protocol == binding.Protocol &&
							port.Bindings.Any(reference => reference.Application == site.Application && reference.Binding == binding.Binding && reference.Endpoint == binding.Endpoint));
			}

			static bool IsUnpairedIPv6(ServicePlan.WebSite site, ServicePlan.WebSite.BindingInfo binding)
			{
				return binding.Address == "::" && !site.Bindings.Any(ipv4 =>
					ipv4.Address == "0.0.0.0" && ipv4.Protocol == binding.Protocol && ipv4.Port == binding.Port && ipv4.Scheme == binding.Scheme &&
					ipv4.Publication == binding.Publication && ipv4.IsDefault == binding.IsDefault && ipv4.IsExplicitDefault == binding.IsExplicitDefault);
			}
		}

		bool OwnsApplication(ServicePlan.WebSite site)
		{
			var owns = service.Kind switch
			{
				"application" => site.Application == service.Id,
				"ingress" => service.Dependencies.Contains(site.Application),
				_ => false,
			};
			return owns && services.Any(application => application.Id == site.Application && application.Kind == "application");
		}

		static bool HasValidListeners(ServicePlan.WebSite site)
		{
			return site.Bindings.Count > 0 &&
				!site.ProbeHosts.Any(host => Uri.CheckHostName(host) == UriHostNameType.Unknown || host.Contains('*')) &&
				!site.Bindings.GroupBy(binding => (binding.Protocol, binding.Address, binding.Port)).Any(group => group.Count() > 1);
		}
	}

	private static void ValidateBindingStructure(ServicePlan service)
	{
		if(service == null || service.Bindings == null || service.Ports == null || service.Web == null ||
			service.Bindings.Any(IsInvalidBinding) || service.Ports.Any(IsInvalidPort) || service.Web.Any(IsInvalidSite))
			throw new ContainerizationException(4, Properties.Resources.Bundle_3_Message);

		static bool IsInvalidBinding(ServicePlan.Binding binding) =>
			binding == null || binding.Aliases == null || binding.Endpoints == null || binding.Connections == null ||
			binding.Endpoints.Any(endpoint => endpoint == null || endpoint.Protocols == null || endpoint.Transports == null) ||
			binding.Connections.Any(connection => connection == null);
		static bool IsInvalidPort(ServicePlan.PortMapping port) =>
			port == null || port.Bindings == null || port.Bindings.Any(reference => reference == null);
		static bool IsInvalidSite(ServicePlan.WebSite site) =>
			site == null || site.Hosts == null || site.ProbeHosts == null || site.Bindings == null || site.Bindings.Any(binding => binding == null);
	}

	private static void ValidateBindings(ServicePlan service, IReadOnlyList<ServicePlan> services)
	{
		if(service.Bindings == null || service.Kind != "application" && service.Bindings.Count > 0 ||
			service.Bindings.GroupBy(binding => binding.Id, StringComparer.Ordinal).Any(group => group.Count() != 1))
			throw new ContainerizationException(4, Properties.Resources.Bundle_3_Message);

		ValidateDeclarations();
		ValidatePublications();

		void ValidateDeclarations()
		{
			foreach(var binding in service.Bindings)
			{
				if(IsInvalidBinding(binding))
					throw new ContainerizationException(4, Properties.Resources.Bundle_3_Message);

				foreach(var endpoint in binding.Endpoints)
				{
					if(IsInvalidEndpoint(endpoint))
						throw new ContainerizationException(4, Properties.Resources.Bundle_3_Message);
				}

				foreach(var connection in binding.Connections)
				{
					var original = binding.Endpoints.FirstOrDefault(endpoint => endpoint.Id == connection.Endpoint);
					var selected = binding.Endpoints.FirstOrDefault(endpoint => endpoint.Id == connection.SelectedEndpoint);

					if(IsInvalidConnection(binding, connection, original, selected))
						throw new ContainerizationException(4, Properties.Resources.Bundle_3_Message);
				}

				var published = services.SelectMany(item => item.Ports).Where(port => port.Bindings.Any(reference => reference.Application == service.Id && reference.Binding == binding.Id)).ToArray();
				if(IsInvalidPublication(binding, published.Length))
					throw new ContainerizationException(4, Properties.Resources.Bundle_3_Message);
			}
		}

		void ValidatePublications()
		{
			foreach(var port in service.Ports)
			{
				if(port.Bindings == null || service.Kind is "application" or "ingress" && port.Bindings.Count == 0 ||
					port.Bindings.GroupBy(reference => (reference.Application, reference.Binding, reference.Endpoint)).Any(group => group.Count() != 1))
					throw new ContainerizationException(4, Properties.Resources.Bundle_3_Message);

				foreach(var reference in port.Bindings)
				{
					var application = services.FirstOrDefault(item => item.Id == reference.Application && item.Kind == "application");
					var binding = application?.Bindings.FirstOrDefault(item => item.Id == reference.Binding);
					var endpoint = binding?.Endpoints.FirstOrDefault(item => item.Id == reference.Endpoint);

					if(IsInvalidReference(application, binding, endpoint, port))
						throw new ContainerizationException(4, Properties.Resources.Bundle_3_Message);
				}
			}
		}

		static bool IsInvalidBinding(ServicePlan.Binding binding)
		{
			return !GetHashRegex().IsMatch(binding.Id ?? "") || binding.Owner is not ("application" or "nginx") ||
				binding.Publication is not ("auto" or "none" or "explicit") || binding.Aliases == null || binding.Endpoints == null || binding.Connections == null ||
				binding.Endpoints.Count == 0 || binding.Endpoints.GroupBy(endpoint => endpoint.Id, StringComparer.Ordinal).Any(group => group.Count() != 1) ||
				binding.IsProxied != (binding.Connections.Count > 0);
		}

		static bool IsInvalidEndpoint(ServicePlan.Binding.Endpoint endpoint)
		{
			return !GetHashRegex().IsMatch(endpoint.Id ?? "") || string.IsNullOrEmpty(endpoint.Scheme) ||
				Uri.CheckHostName(endpoint.Host) == UriHostNameType.Unknown || endpoint.Port is < 1 or > 65535 ||
				endpoint.Path == null || !endpoint.Path.StartsWith('/') || endpoint.Protocols == null || endpoint.Transports == null ||
				endpoint.Transports.Any(transport => transport is not ("tcp" or "udp"));
		}

		bool IsInvalidConnection(ServicePlan.Binding binding, ServicePlan.Binding.Connection connection, ServicePlan.Binding.Endpoint original, ServicePlan.Binding.Endpoint selected)
		{
			return binding.Owner != "application" || original == null || selected == null || selected.Host != "0.0.0.0" ||
				selected.Scheme != original.Scheme || selected.Path != original.Path || connection.Host != service.Id || connection.Port != selected.Port ||
				!selected.Transports.Contains("tcp") || !selected.Protocols.Contains(connection.Protocol);
		}

		static bool IsInvalidPublication(ServicePlan.Binding binding, int count)
		{
			return binding.Publication == "none" && count != 0 ||
				binding.Owner == "application" && binding.Publication == "auto" && binding.IsProxied && count != 0 ||
				(binding.Publication == "explicit" || binding.Publication == "auto" && !binding.IsProxied) && count == 0;
		}

		bool IsInvalidReference(ServicePlan application, ServicePlan.Binding binding, ServicePlan.Binding.Endpoint endpoint, ServicePlan.PortMapping port)
		{
			return endpoint == null || endpoint.Port != port.Container || !endpoint.Transports.Contains(port.Protocol) ||
				service.Kind == "application" && (application != service || binding.Owner != "application" || endpoint.Host != "0.0.0.0") ||
				service.Kind == "ingress" && (binding.Owner != "nginx" || !service.Dependencies.Contains(application.Id)) ||
				service.Kind == "infrastructure";
		}
	}

	[GeneratedRegex(@"^[a-z0-9][a-z0-9_-]*$")]
	private static partial Regex GetProjectRegex();

	[GeneratedRegex(@"^[a-f0-9]{64}$")]
	private static partial Regex GetHashRegex();

	[GeneratedRegex(@"^sha256:[a-f0-9]{64}$")]
	private static partial Regex GetDigestRegex();

	[GeneratedRegex(@"^[A-Za-z0-9][A-Za-z0-9_.-]*$")]
	private static partial Regex GetIdentityRegex();
	#endregion

	#region 释放资源
	public void Dispose()
	{
		if(_temporary && System.IO.Directory.Exists(this.Directory))
			System.IO.Directory.Delete(this.Directory, true);
	}
	#endregion
}
