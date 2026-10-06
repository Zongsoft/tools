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
using System.Net;
using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace Zongsoft.Tools.Containerizer.Protocol;

internal sealed partial class DeliveryBundle : IDisposable
{
	#region 成员字段
	private readonly bool _temporary;
	#endregion

	#region 构造函数
	private DeliveryBundle(string directory, bool temporary)
	{
		_temporary = temporary;
		this.Directory = directory;
		this.Plan = Files.Load(Path.Combine(directory, DeliveryPlan.FileName), ProtocolJson.Default.DeliveryPlan);
		this.Id = Files.Hash(Path.Combine(directory, DeliveryPlan.FileName));
		this.Verify();
	}
	#endregion

	#region 公共属性
	public string Id { get; }
	public string Directory { get; }
	public DeliveryPlan Plan { get; }
	#endregion

	#region 公共方法
	public static DeliveryBundle Open(string path)
	{
		path = Path.GetFullPath(path);
		Files.NoLinks(path);

		if(System.IO.Directory.Exists(path))
			return new(path, false);

		if(!File.Exists(path) || !path.EndsWith(".tar.gz", StringComparison.OrdinalIgnoreCase))
			throw new ContainerizationException(2, Properties.Resources.Bundle_1_Message);

		var directory = Path.Combine(Path.GetTempPath(), $"containerizer-{Guid.NewGuid().ToString("N")}");
		Files.PrivateDirectory(directory);

		try
		{
			Files.Extract(path, directory);
			return new(directory, true);
		}
		catch { System.IO.Directory.Delete(directory, true); throw; }
	}

	public void Verify()
	{
		if(this.Plan.Schema != DeliveryPlan.ProtocolVersion)
			throw new ContainerizationException(3, Properties.Resources.Bundle_2_Message);

		Identity(this.Plan.Name);

		if(this.Plan.Project != $"containerizer-{this.Plan.Name.ToLowerInvariant().Replace('.', '-').Replace('_', '-')}-{Files.HashText(this.Plan.Name)[..8]}")
			throw new ContainerizationException(4, Properties.Resources.Bundle_3_Message);

		if(this.Plan.Architecture is not ("x64" or "arm64") ||
		   this.Plan.Services.Count == 0 ||
		   this.Plan.DataRoot != Installation.Paths.GetDataPath(this.Plan.Name) ||
		   string.IsNullOrEmpty(this.Plan.Project) || !ProjectRegex().IsMatch(this.Plan.Project))
			throw new ContainerizationException(4, Properties.Resources.Bundle_3_Message);

		var records = new Dictionary<string, FileRecord>(StringComparer.OrdinalIgnoreCase);
		foreach(var file in this.Plan.Files)
		{
			if(!records.TryAdd(file.Path, file) || !HashRegex().IsMatch(file.Hash ?? ""))
				throw new ContainerizationException(4, Properties.Resources.Bundle_4_Message);

			var path = Files.Below(this.Directory, file.Path);
			if(!File.Exists(path) || new FileInfo(path).Length != file.Length || Files.Hash(path) != file.Hash)
				throw new ContainerizationException(4, string.Format(Properties.Resources.Bundle_5_Message, file.Path));
		}

		string[] requiredFiles = ["containerizer", "compose.yaml", "install.sh", "uninstall.sh"];
		foreach(var required in requiredFiles)
		{
			if(!records.ContainsKey(required))
				throw new ContainerizationException(4, string.Format(Properties.Resources.Bundle_6_Message, required));
		}

		var checksums = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
		foreach(var line in File.ReadLines(Path.Combine(this.Directory, "checksums.sha256")))
		{
			if(line.Length < 67 || line.Substring(64, 2) != "  " || !checksums.TryAdd(line[66..], line[..64]))
				throw new ContainerizationException(4, Properties.Resources.Bundle_7_Message);
		}

		if(checksums.Count != records.Count + 1 ||
			checksums.GetValueOrDefault(DeliveryPlan.FileName) != this.Id ||
			records.Any(pair => checksums.GetValueOrDefault(pair.Key) != pair.Value.Hash))
			throw new ContainerizationException(4, Properties.Resources.Bundle_8_Message);

		foreach(var file in Files.Enumerate(this.Directory))
		{
			var relative = Path.GetRelativePath(this.Directory, file).Replace('\\', '/');
			if(relative is not (DeliveryPlan.FileName or "checksums.sha256") && !records.ContainsKey(relative))
				throw new ContainerizationException(4, Properties.Resources.Bundle_9_Message);
		}

		var services = new HashSet<string>(StringComparer.Ordinal);
		foreach(var service in this.Plan.Services)
		{
			Identity(service.Id);

			if(!services.Add(service.Id) || service.Kind is not ("application" or "infrastructure" or "ingress"))
				throw new ContainerizationException(4, Properties.Resources.Bundle_10_Message);
			if(service.Image.Platform != (this.Plan.Architecture == "arm64" ? "linux/arm64" : "linux/amd64") ||
				!DigestRegex().IsMatch(service.Image.Id ?? "") || service.Image.Mode is not ("online" or "offline"))
				throw new ContainerizationException(4, Properties.Resources.Bundle_11_Message);
			if(!service.Image.Tag.StartsWith($"containerizer/{this.Plan.Project}/{service.Id}:", StringComparison.Ordinal))
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

			foreach(var mount in service.Mounts)
			{
				LinuxPath(mount.Target);

				if(mount.ReadOnly)
					Files.Below(this.Directory, mount.Source);
				else
					LinuxPath(mount.Source);
			}
		}

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

	public static void Identity(string name)
	{
		if(!IdentityRegex().IsMatch(name ?? "") || name.Contains("..", StringComparison.Ordinal))
			throw new ContainerizationException(2, Properties.Resources.Bundle_16_Message);
	}

	public static void LinuxPath(string path)
	{
		if(!Files.IsLinuxPath(path))
			throw new ContainerizationException(4, Properties.Resources.Bundle_17_Message);
	}
	#endregion

	#region 私有方法
	internal static void ValidateWeb(ServicePlan service, IReadOnlyList<ServicePlan> services)
	{
		var sites = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
		if(service.Ports.Where(port => port.Name != null).GroupBy(port => port.Name, StringComparer.Ordinal).Any(group => group.Count() > 1))
			throw new ContainerizationException(4, Properties.Resources.Bundle_3_Message);

		foreach(var site in service.Web)
		{
			var ownsApplication = service.Kind switch
			{
				"application" => site.Application == service.Id,
				"ingress" => service.Dependencies.Contains(site.Application),
				_ => false,
			};
			if(!ownsApplication || !services.Any(application => application.Id == site.Application && application.Kind == "application") ||
				string.IsNullOrWhiteSpace(site.Name) || !sites.Add(site.Application + "/" + site.Name) || site.Bindings.Count == 0 ||
				site.ProbeHosts.Any(host => Uri.CheckHostName(host) == UriHostNameType.Unknown || host.Contains('*')) ||
				site.Bindings.GroupBy(binding => (binding.Address, binding.Port)).Any(group => group.Count() > 1))
				throw new ContainerizationException(4, Properties.Resources.Bundle_3_Message);

			foreach(var binding in site.Bindings)
			{
				if(binding.Scheme is not ("http" or "https") || !IPAddress.TryParse(binding.Address, out _) || binding.Port is < 1 or > 65535)
					throw new ContainerizationException(4, Properties.Resources.Bundle_3_Message);
				if(binding.Publication == null)
					continue;
				if(site.ProbeHosts.Count == 0 || binding.Address is not ("0.0.0.0" or "::") ||
					!service.Ports.Any(port => port.Name == binding.Publication && port.Container == binding.Port && port.Protocol == "tcp"))
					throw new ContainerizationException(4, Properties.Resources.Bundle_3_Message);
				if(binding.Address == "::" && !site.Bindings.Any(ipv4 =>
					ipv4.Address == "0.0.0.0" && ipv4.Port == binding.Port && ipv4.Scheme == binding.Scheme &&
					ipv4.Publication == binding.Publication && ipv4.IsDefault == binding.IsDefault && ipv4.ExplicitDefault == binding.ExplicitDefault))
					throw new ContainerizationException(4, Properties.Resources.Bundle_3_Message);
			}
		}

		if(service.Kind == "ingress")
		{
			foreach(var group in service.Web.SelectMany(site => site.Bindings).GroupBy(binding => (binding.Address, binding.Port)))
			{
				if(group.Count(binding => binding.IsDefault) != 1 || group.Count(binding => binding.ExplicitDefault) > 1 ||
					group.Any(binding => binding.ExplicitDefault && !binding.IsDefault) ||
					group.Select(binding => binding.Scheme).Distinct().Count() != 1 || group.Select(binding => binding.Publication).Distinct().Count() != 1)
					throw new ContainerizationException(4, Properties.Resources.Bundle_3_Message);
			}
		}
	}

	[GeneratedRegex(@"^[a-z0-9][a-z0-9_-]*$")]
	private static partial Regex ProjectRegex();

	[GeneratedRegex(@"^[a-f0-9]{64}$")]
	private static partial Regex HashRegex();

	[GeneratedRegex(@"^sha256:[a-f0-9]{64}$")]
	private static partial Regex DigestRegex();

	[GeneratedRegex(@"^[A-Za-z0-9][A-Za-z0-9_.-]*$")]
	private static partial Regex IdentityRegex();
	#endregion

	#region 释放资源
	public void Dispose()
	{
		if(_temporary && System.IO.Directory.Exists(this.Directory))
			System.IO.Directory.Delete(this.Directory, true);
	}
	#endregion
}
