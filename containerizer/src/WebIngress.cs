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
using System.Linq;
using System.Globalization;
using System.Collections.Generic;

using Zongsoft.Tools.Containerizer.Protocol;

namespace Zongsoft.Tools.Containerizer;

internal static partial class WebIngress
{
	internal static void Plan(ContainerManifest manifest, IReadOnlyList<ServiceBuildContext> sources)
	{
		foreach(var source in sources.Where(source => source.Component.IsApplication))
		{
			var component = source.Component;

			if(source.Package.Web == null && ((component["dependences"] ?? "").Split(';', StringSplitOptions.TrimEntries).Contains("nginx") || component.Values.Keys.Any(key => key.StartsWith("probe-host!", StringComparison.OrdinalIgnoreCase))))
				throw WebPackage.CreateException(component.Name, ".web/nginx/.bindings");
		}

		var applications = sources.Where(source => source.Package?.Web != null).OrderBy(source => source.Plan.Id, StringComparer.Ordinal).ToArray();

		foreach(var application in applications)
		{
			var ingress = sources.Single(source => source.Plan.Id == application.Package.Web.Hoster);
			ingress.Plan.Dependencies.Add(application.Plan.Id);

			foreach(var site in application.Package.Web.Sites)
			{
				ingress.Plan.Web.Add(new()
				{
					Application = application.Plan.Id,
					Name = site.Name,
					Hosts = [.. site.Hosts],
					Bindings = site.Bindings
						.Select(binding => new WebBindingPlan
						{
							Address = binding.Address,
							Port = binding.Port,
							Scheme = binding.Scheme,
							IsExplicitDefault = binding.IsExplicitDefault
						})
						.ToList(),
				});
			}
		}

		foreach(var ingress in sources.Where(source => source.Plan.Template == "nginx"))
		{
			if(ingress.Plan.Web.Count == 0)
				throw WebPackage.CreateException(ingress.Plan.Id, ".web/nginx/.bindings");

			Configure(ingress, sources);
			ValidateFiles(manifest, ingress, applications);
		}
	}

	private static void Configure(ServiceBuildContext source, IReadOnlyList<ServiceBuildContext> sources)
	{
		var sites = source.Plan.Web;
		var records = sites.SelectMany(site => site.Bindings.Select(binding => (Site: site, Binding: binding))).ToArray();
		var mapping = ParsePorts(source.Settings.GetValueOrDefault("port"), records.Select(record => record.Binding.Port).ToHashSet(), source.Plan.Id);

		foreach(var endpoint in records.GroupBy(record => (record.Binding.Address, record.Binding.Port)))
		{
			if(endpoint.Select(record => record.Binding.Scheme).Distinct().Count() != 1 || endpoint.Count(record => record.Binding.IsExplicitDefault) > 1)
				throw WebPackage.CreateException(source.Plan.Id, $"listen {endpoint.Key}");

			var first = endpoint.FirstOrDefault(record => record.Binding.IsExplicitDefault);
			if(first.Site == null)
				first = endpoint.First();

			first.Binding.IsDefault = true;
			var hosts = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

			foreach(var record in endpoint)
			{
				foreach(var host in record.Site.Hosts)
				{
					// '.example' also owns both the exact name and the leading wildcard.
					var names = host.StartsWith('.') ? new[] { host[1..], "*" + host } : [host];

					if(names.Any(name => !hosts.Add(name)))
						throw WebPackage.CreateException(source.Plan.Id, "host=" + host);
				}
			}
		}

		foreach(var group in records.GroupBy(record => record.Binding.Port).OrderBy(group => group.Key))
		{
			var preferred = mapping.GetValueOrDefault(group.Key, group.Key);
			if(preferred == 0)
				continue;

			var ipv4 = group.Where(record => record.Binding.Address == "0.0.0.0").ToArray();
			var ipv6 = group.Where(record => record.Binding.Address == "::").ToArray();

			if(ipv4.Length == 0 || ipv4.Length + ipv6.Length != group.Count() ||
			   group.Select(record => record.Binding.Scheme).Distinct().Count() != 1 ||
			   ipv6.Length > 0 && !ipv4.Select(GetBindingSignature).Order(StringComparer.Ordinal).SequenceEqual(ipv6.Select(GetBindingSignature).Order(StringComparer.Ordinal)))
				throw WebPackage.CreateException(source.Plan.Id, $"listen :{group.Key}");

			var name = "web-" + group.Key.ToString(CultureInfo.InvariantCulture);
			source.Plan.Ports.Add(new() { Name = name, Container = group.Key, Host = preferred, Address = "127.0.0.1" });

			foreach(var record in group)
				record.Binding.Publication = name;
		}

		foreach(var site in sites)
		{
			var component = sources.Single(source => source.Plan.Id == site.Application).Component;
			var custom = component["probe-host!" + site.Name];
			var published = site.Bindings.Where(binding => binding.Publication != null && binding.Address == "0.0.0.0").ToArray();

			if(custom != null && (!WebPackage.IsConcreteHost(custom) || !site.Hosts.Any(host => WebPackage.GetHostMatchScore(host, custom) > 0)))
				throw WebPackage.CreateException(site.Application, "probe-host!" + site.Name);

			site.ProbeHosts.AddRange(site.Hosts.Where(WebPackage.IsConcreteHost));
			if(custom != null)
				site.ProbeHosts.Add(custom);
			if(published.Length == 0)
				continue;
			if(site.Hosts.Count > 0 && site.ProbeHosts.Count == 0)
				throw WebPackage.CreateException(site.Application, "probe-host!" + site.Name);
			if(site.Hosts.Count == 0)
				site.ProbeHosts.Add("127.0.0.1");

			foreach(var binding in published)
			{
				var peers = records.Where(record => record.Binding.Port == binding.Port && record.Binding.Address == binding.Address).ToArray();

				foreach(var host in site.ProbeHosts)
				{
					var candidates = peers.Select(record => (record.Site, Score: record.Site.Hosts.Select(pattern => WebPackage.GetHostMatchScore(pattern, host)).DefaultIfEmpty().Max())).OrderByDescending(item => item.Score).ToArray();
					var winner = candidates[0].Score > 0 ? candidates[0].Site : peers.Single(record => record.Binding.IsDefault).Site;

					if(winner != site || candidates.Length > 1 && candidates[0].Score > 0 && candidates[0].Score == candidates[1].Score)
						throw WebPackage.CreateException(site.Application, $"{site.Name}: {host}:{binding.Port}");
				}
			}
		}

		foreach(var component in sources.Select(source => source.Component).Where(component => component.IsApplication))
		{
			foreach(var key in component.Values.Keys.Where(key => key.StartsWith("probe-host!", StringComparison.OrdinalIgnoreCase)))
			{
				if(!sites.Any(site => site.Application.Equals(component.Name, StringComparison.OrdinalIgnoreCase) && site.Name.Equals(key[11..], StringComparison.OrdinalIgnoreCase)))
					throw WebPackage.CreateException(component.Name, key);
			}
		}
	}

	private static Dictionary<int, int> ParsePorts(string text, HashSet<int> targets, string source)
	{
		var result = new Dictionary<int, int>();
		if(text == null)
			return result;

		foreach(var item in text.Split(',', StringSplitOptions.TrimEntries))
		{
			var parts = item.Split(':', StringSplitOptions.TrimEntries);
			if(parts.Length != 2 || !int.TryParse(parts[0], NumberStyles.None, CultureInfo.InvariantCulture, out var target) || !targets.Contains(target))
				throw WebPackage.CreateException(source, "port=" + text);

			var host = 0;
			if(parts[1] != "none" && (!int.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out host) || host is < 1 or > 65535) || !result.TryAdd(target, host))
				throw WebPackage.CreateException(source, "port=" + text);
		}

		if(targets.Select(target => result.GetValueOrDefault(target, target)).Where(port => port > 0).GroupBy(port => port).Any(group => group.Count() > 1))
			throw WebPackage.CreateException(source, "port=" + text);

		return result;
	}

	private static string GetBindingSignature((WebSitePlan Site, WebBindingPlan Binding) record) => $"{record.Site.Application}/{record.Site.Name}/{record.Binding.Scheme}/{record.Binding.IsDefault}/{record.Binding.IsExplicitDefault}";
}
