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
using System.Net;
using System.Globalization;
using System.Collections.Generic;

using Zongsoft.Communication;
using Zongsoft.Tools.Containerizer.Protocol;

namespace Zongsoft.Tools.Containerizer;

internal static class ApplicationBindings
{
	internal static void Configure(ServiceBuildContext source, ContainerManifest manifest)
	{
		var document = source.Package.Bindings;
		var overrides = new Dictionary<string, string>(StringComparer.Ordinal);

		ReadOverrides();

		foreach(var binding in document.Bindings)
		{
			var routes = document.Sites.SelectMany(site => site.Routes).Where(route => route.Target.Kind == "binding" && route.Target.Binding == binding.Id).ToArray();
			var value = overrides.GetValueOrDefault(binding.Id, "auto");
			var plan = new ServicePlan.Binding
			{
				Id = binding.Id,
				Owner = binding.Owner,
				Aliases = [.. binding.Aliases],
				IsProxied = routes.Length > 0,
				Publication = value == null ? "pending" : value is "auto" or "none" ? value : "explicit",
				Endpoints = binding.Endpoints.Select(endpoint => new ServicePlan.Binding.Endpoint
				{
					Id = endpoint.Id,
					Scheme = endpoint.Scheme,
					Host = endpoint.Host,
					Port = endpoint.Port,
					Path = endpoint.Path,
					Protocols = [.. endpoint.Protocols],
					Transports = [.. endpoint.Transports],
				}).ToList(),
			};
			source.Plan.Bindings.Add(plan);

			ConfigureConnections(binding, plan, routes);

			if(binding.Owner != "application" || value == null || value == "none" || value == "auto" && plan.IsProxied)
				continue;

			ConfigurePublication(binding, value);
		}

		void ReadOverrides()
		{
			foreach(var pair in source.Component.Values.Where(pair => pair.Key.StartsWith("publish!", StringComparison.OrdinalIgnoreCase)).ToArray())
			{
				var selector = pair.Key[8..];
				var matches = document.Bindings.Where(binding => binding.Owner == "application" &&
					(binding.Id.Equals(selector, StringComparison.OrdinalIgnoreCase) || binding.Aliases.Contains(selector, StringComparer.OrdinalIgnoreCase))).ToArray();
				var value = manifest.ResolveValue(pair.Value);

				if(manifest.IsPlanning && ContainerManifest.HasVariables(value) && !manifest.Evaluator.TryEvaluate(value, out value, out _))
				{
					source.MissingSettings.Add(pair.Key);
					value = null;
				}

				if(matches.Length != 1 || !overrides.TryAdd(matches[0].Id, value))
					throw CreateException(source, pair.Key);

				if(!manifest.IsPlanning)
					source.Component[pair.Key] = overrides[matches[0].Id];
			}
		}

		void ConfigureConnections(BindingDocument.Binding binding, ServicePlan.Binding plan, BindingDocument.SiteRoute[] routes)
		{
			foreach(var group in routes.GroupBy(route => route.Target.Endpoint))
			{
				var declared = binding.Endpoints.Single(endpoint => endpoint.Id == group.Key);
				var selected = binding.Endpoints
					.Where(endpoint => endpoint.Host == "0.0.0.0" && endpoint.Scheme == declared.Scheme && endpoint.Path == declared.Path &&
						group.All(route => Supports(endpoint, route.Protocol)))
					.OrderBy(endpoint => endpoint.Id == declared.Id ? 0 : 1).ThenBy(endpoint => endpoint.Port).ThenBy(endpoint => endpoint.Id, StringComparer.Ordinal)
					.FirstOrDefault() ?? throw CreateException(source, binding.Id + "/container-ipv4");

				foreach(var protocol in group.Select(route => route.Protocol).Distinct(StringComparer.Ordinal))
				{
					plan.Connections.Add(new()
					{
						Endpoint = declared.Id,
						SelectedEndpoint = selected.Id,
						Host = source.Plan.Id,
						Port = selected.Port,
						Protocol = protocol,
					});
				}
			}
		}

		void ConfigurePublication(BindingDocument.Binding binding, string value)
		{
			var ports = binding.Endpoints.Select(endpoint => endpoint.Port).Distinct().ToArray();
			if(ports.Length != 1 || binding.Endpoints.Any(endpoint => endpoint.Transports.Length == 0))
				throw CreateException(source, binding.Id + "/publication-target");

			var transports = binding.Endpoints.SelectMany(endpoint => endpoint.Transports).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
			if(value == "auto")
			{
				foreach(var transport in transports)
					Publish(source, binding, transport, "127.0.0.1", ports[0]);
			}
			else
			{
				foreach(var address in value.Split(';', StringSplitOptions.TrimEntries))
				{
					if(!TryGetPublication(address, transports, out var target, out var host))
						throw CreateException(source, "publish!" + binding.Id);

					Publish(source, binding, target.Scheme, host.ToString(), target.Port);
				}
			}
		}

		static bool TryGetPublication(string address, string[] transports, out NetworkAddress target, out IPAddress host)
		{
			host = null;
			return NetworkAddress.TryParse(address, out target) &&
				target.Scheme is "tcp" or "udp" && target.Port is >= 1 and <= 65535 && target.Path is null or "" or "/" &&
				IPAddress.TryParse(target.Host, out host) && (host.AddressFamily != System.Net.Sockets.AddressFamily.InterNetworkV6 || host.ScopeId == 0) &&
				transports.Contains(target.Scheme, StringComparer.Ordinal) && address.EndsWith(":" + target.Port.ToString(CultureInfo.InvariantCulture), StringComparison.Ordinal);
		}
	}

	private static bool Supports(BindingDocument.Endpoint endpoint, string value)
	{
		var expected = NetworkProtocol.Parse(value);
		var protocols = endpoint.Protocols.Select(NetworkProtocol.Parse).Where(protocol => protocol.IsHttp).ToArray();
		return endpoint.Transports.Contains("tcp", StringComparer.Ordinal) &&
			protocols.Any(protocol => protocol.Name == expected.Name && protocol.HttpVersion == expected.HttpVersion) &&
			protocols.Where(protocol => (protocol.Name == "grpc") == (expected.Name == "grpc")).GroupBy(protocol => protocol.Name)
				.All(group => group.Any(protocol => protocol.HttpVersion == expected.HttpVersion));
	}

	private static void Publish(ServiceBuildContext source, BindingDocument.Binding binding, string transport, string address, int hostPort)
	{
		var endpoint = binding.Endpoints.Where(endpoint => endpoint.Host == "0.0.0.0" && endpoint.Transports.Contains(transport, StringComparer.Ordinal))
			.OrderBy(endpoint => endpoint.Id, StringComparer.Ordinal).FirstOrDefault() ?? throw CreateException(source, binding.Id + "/container-ipv4");
		var port = new ServicePlan.PortMapping
		{
			Name = "binding-" + source.Plan.Ports.Count.ToString(CultureInfo.InvariantCulture),
			Address = address,
			Host = hostPort,
			Container = endpoint.Port,
			Protocol = transport,
			Bindings = [new() { Application = source.Plan.Id, Binding = binding.Id, Endpoint = endpoint.Id }],
		};
		var existing = source.Plan.Ports.FirstOrDefault(candidate => ServicePlan.PortMapping.Conflicts(candidate, port));

		if(existing != null)
		{
			if(existing.Address != address || existing.Container != endpoint.Port || existing.Bindings.Any(reference => reference.Binding == binding.Id))
				throw CreateException(source, "publish!" + binding.Id);

			existing.Bindings.AddRange(port.Bindings);
		}
		else
			source.Plan.Ports.Add(port);
	}

	private static ContainerizationException CreateException(ServiceBuildContext source, string field) =>
		ApplicationHealth.CreateException(source.Plan.Id, field);
}
