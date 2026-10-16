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
using System.Numerics;
using System.Collections.Generic;

using Zongsoft.Communication;

namespace Zongsoft.Tools.Packager.Web;

/// <summary>将站点选择与应用声明组合为按请求类别和原始基路径分派的确定路由。</summary>
internal static class Routing
{
	#region 路由组合
	internal static Definition.Site Resolve(Definition.Site site, Configurator.Context context)
	{
		var fallback = site.Default with { Path = NormalizePath(site.Default.Path, site.Default.Source) };
		var scopes = site.Routes.Select(route => route with { Path = NormalizePath(route.Path, route.Source) }).ToArray();
		var applications = context.Bindings.Bindings.Where(binding => binding.Owner == "application" &&
			binding.Endpoints.Any(endpoint => endpoint.Scheme is "http" or "https")).ToArray();

		if(scopes.Select(scope => scope.Path).Distinct(StringComparer.Ordinal).Count() != scopes.Length)
			throw DefinitionException.Create("Duplicate", site.Source, "path");

		foreach(var scope in scopes.Prepend(fallback))
		{
			if(scope.Match != Definition.MatchKind.Prefix || scope.Server == null)
				throw DefinitionException.Create("Capability", scope.Source, "binding routes require a literal prefix and a structured server");
			if(scope.Directives.Any(directive => directive.Name is "proxy_pass" or "grpc_pass" or "include" or "rewrite" or "try_files" or "return" or "error_page"))
				throw DefinitionException.Create("Capability", scope.Source, "native request dispatch cannot be represented in bindings");

			var selected = Select(applications, scope.Server.Selector, scope.Source);

			if(scope.Server.Selector != null && !selected.SelectMany(binding => binding.Endpoints)
				.Any(endpoint => (endpoint.Scheme is "http" or "https") && (Contains(scope.Path, endpoint.Path) || Contains(endpoint.Path, scope.Path))))
				throw DefinitionException.Create("Conflict", scope.Source, "server binding/path: " + scope.Server.Selector);

			if(scope.Server.Kind != Definition.ServerKind.Application &&
			   !scope.Server.Members.Any(member => Contains(scope.Path, member.Endpoint.Path) || Contains(member.Endpoint.Path, scope.Path)))
				throw DefinitionException.Create("Conflict", scope.Source, "server path");
		}

		var paths = applications.SelectMany(binding => binding.Endpoints).Where(endpoint => endpoint.Scheme is "http" or "https").Select(endpoint => endpoint.Path)
			.Concat(scopes.Select(scope => scope.Path))
			.Concat(scopes.Prepend(fallback).SelectMany(scope => scope.Server.Members).Select(member => member.Endpoint.Path))
			.Append(NormalizePath(fallback.Path, fallback.Source)).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal);
		var routes = new List<Definition.Route>();

		foreach(var kind in new[] { "http", "grpc" })
		{
			foreach(var path in paths)
			{
				var scope = scopes.Where(candidate => Contains(candidate.Path, path) && Applies(candidate.Server, kind))
					.OrderByDescending(candidate => candidate.Path.Length).FirstOrDefault() ?? fallback;
				var group = scope.Server;
				Definition.Route route;

				if(group.Kind == Definition.ServerKind.Application)
					route = Application(scope, path, kind, Select(applications, group.Selector, scope.Source));
				else
					route = Remote(scope, path, kind);

				if(route != null)
					routes.Add(route);
			}
		}

		return site with { Routes = routes.OrderBy(route => route.Mapping.RequestKind, StringComparer.Ordinal).ThenByDescending(route => route.Path.Length).ThenBy(route => route.Path, StringComparer.Ordinal).ToArray() };
	}

	private static bool Applies(Definition.ServerGroup group, string kind) =>
		group.Kind == Definition.ServerKind.Application || (group.Members[0].Endpoint.Protocol == "grpc+http2" ? "grpc" : "http") == kind;

	private static BindingDocument.Binding[] Select(BindingDocument.Binding[] bindings, string selector, Diagnostic.Location source)
	{
		if(selector == null)
			return bindings;

		var matches = bindings.Where(binding => binding.Aliases.Contains(selector, StringComparer.OrdinalIgnoreCase)).ToArray();
		if(matches.Length == 0)
			throw DefinitionException.Create("Required", source, "server binding: " + selector);

		return matches;
	}

	private static Definition.Route Application(Definition.Route scope, string path, string kind, BindingDocument.Binding[] bindings)
	{
		var candidates = bindings.SelectMany(binding => binding.Endpoints.Where(endpoint =>
			(endpoint.Scheme is "http" or "https") && Contains(scope.Path, path) && Contains(endpoint.Path, path) && Includes(endpoint, kind)).Select(endpoint => (Binding: binding, Endpoint: endpoint))).ToArray();

		if(candidates.Length == 0)
			return null;

		var length = candidates.Max(candidate => candidate.Endpoint.Path.Length);
		candidates = candidates.Where(candidate => candidate.Endpoint.Path.Length == length).ToArray();
		if(candidates.Select(candidate => candidate.Binding.Id).Distinct(StringComparer.Ordinal).Count() != 1)
			throw DefinitionException.Create("Conflict", scope.Source, kind + ":" + path + " => " + string.Join(", ", candidates.Select(candidate => candidate.Binding.Id).Distinct()));

		// 同一服务的替代地址按固定规则选取，不探测、不组成负载均衡池。
		var usable = candidates.Select(candidate => (candidate.Binding, candidate.Endpoint, Protocol: Protocol(candidate.Endpoint, kind, scope.WebSocket)))
			.Where(candidate => candidate.Protocol != null).OrderBy(candidate => Rank(candidate.Endpoint)).ThenBy(candidate => candidate.Endpoint.Host, StringComparer.Ordinal)
			.ThenBy(candidate => candidate.Endpoint.Port).ThenBy(candidate => candidate.Endpoint.Id, StringComparer.Ordinal).ToArray();

		if(usable.Length == 0)
			throw DefinitionException.Create("Capability", scope.Source, "Nginx upstream mapping: " + string.Join(',', candidates.SelectMany(candidate => candidate.Endpoint.Protocols)));

		var selected = usable[0];
		var endpoint = selected.Endpoint;
		var protocol = selected.Protocol;
		var host = endpoint.Host switch { "0.0.0.0" => "127.0.0.1", "::" => "::1", _ => endpoint.Host };
		var connection = new BindingDocument.Connection(endpoint.Scheme, host, endpoint.Port, endpoint.Path);
		var member = new Definition.ServerGroup.Member("", new(endpoint.Scheme, host, endpoint.Port, endpoint.Path, protocol), BigInteger.One);
		var policy = scope.Server.Policy with { Tls = Tls(scope.Server.Policy.Tls, [member], scope.Source) };
		var target = new BindingDocument.RouteTarget { Kind = "binding", Binding = selected.Binding.Id, Endpoint = endpoint.Id };

		return Complete(scope, path, kind, protocol, scope.Server with { Members = [member], Policy = policy }, target, connection);
	}

	private static Definition.Route Remote(Definition.Route scope, string path, string kind)
	{
		var group = scope.Server;
		var endpoint = group.Members[0].Endpoint;

		if(!Applies(group, kind) || !Contains(endpoint.Path, path) || !Contains(NormalizePath(scope.Path, scope.Source), path))
			return null;

		var policy = group.Policy with { Tls = Tls(group.Policy.Tls, group.Members, scope.Source) };
		var target = group.Kind == Definition.ServerKind.Pool ?
			new BindingDocument.RouteTarget
			{
				Kind = "remote",
				Balance = policy.Balance,
				Members = group.Members.Select(member => new BindingDocument.RemoteMember
				{
					Name = member.Name,
					Weight = Number(member.Weight, scope.Source),
					Connection = Connection(member.Endpoint),
				}).ToArray(),
			} : new BindingDocument.RouteTarget { Kind = "remote" };

		return Complete(scope, path, kind, endpoint.Protocol, group with { Policy = policy }, target,
			group.Kind == Definition.ServerKind.Pool ? null : Connection(endpoint));
	}

	private static Definition.Route Complete(Definition.Route scope, string path, string kind, string protocol, Definition.ServerGroup server,
		BindingDocument.RouteTarget target, BindingDocument.Connection connection)
	{
		var header = scope.Headers.FirstOrDefault(header => header.Name.Equals("Host", StringComparison.OrdinalIgnoreCase));
		if(header.Expression && header.Value != "$http_host" && Configurator.Nginx.Literal(header.Value) is { Length: > 0 } literal)
			header = header with { Value = literal, Expression = false };

		if(header.Expression && header.Value != "$http_host")
			throw DefinitionException.Create("Capability", header.Source, "dynamic upstream Host");

		var tls = server.Policy.Tls;
		var mapping = new BindingDocument.SiteRoute(kind, path, target, protocol, connection,
			header.Expression || string.IsNullOrEmpty(header.Value) ? null : header.Value,
			tls == null ? null : new(tls.Verify, tls.Name, tls.Trust is { } trust ? "file:" + trust.Path : null), Policy(server.Policy, scope.Source));

		return scope with { Path = path, Server = server, Mapping = mapping };
	}
	#endregion

	#region 能力选择
	private static bool Includes(BindingDocument.Endpoint endpoint, string kind)
	{
		var protocols = endpoint.Protocols.Select(NetworkProtocol.Parse).Where(protocol => protocol.IsHttp).ToArray();
		return kind == "grpc" ? protocols.Any(protocol => protocol.Name == "grpc") :
			protocols.Length == 0 || protocols.Any(protocol => protocol.Name != "grpc");
	}

	private static string Protocol(BindingDocument.Endpoint endpoint, string kind, bool websocket)
	{
		var protocols = endpoint.Protocols.Select(NetworkProtocol.Parse).Where(protocol => protocol.IsHttp && (protocol.Name == "grpc") == (kind == "grpc")).ToArray();
		var versions = new HashSet<int>(kind == "grpc" ? [2] : [1, 2]);

		foreach(var semantic in protocols.GroupBy(protocol => protocol.Name))
			versions.IntersectWith(semantic.Select(protocol => protocol.HttpVersion));

		if(protocols.Length == 0 || versions.Count == 0 || kind == "http" && websocket && !versions.Contains(1))
			return null;

		var version = kind == "http" && websocket ? 1 : versions.Max();
		return protocols.Where(protocol => protocol.HttpVersion == version).OrderBy(protocol => protocol.Name != null).ThenBy(protocol => protocol.Name, StringComparer.Ordinal).First().ToString();
	}

	private static int Rank(BindingDocument.Endpoint endpoint) => endpoint.Host is "0.0.0.0" or "::" ? 2 :
		endpoint.HostType == UriHostNameType.Dns ? 3 : endpoint.HostType == UriHostNameType.IPv4 ? 0 : 1;

	private static Definition.TlsPolicy Tls(Definition.TlsPolicy policy, IReadOnlyList<Definition.ServerGroup.Member> members, Diagnostic.Location source)
	{
		if(members[0].Endpoint.Scheme != "https")
			return null;

		policy ??= new(true, null, null);
		var name = policy.Name;
		var host = members[0].Endpoint.Host;

		if(string.IsNullOrEmpty(name) && Uri.CheckHostName(host) == UriHostNameType.Dns && members.All(member => member.Endpoint.Host == host))
			name = host;
		if(policy.Verify && string.IsNullOrEmpty(name))
			throw DefinitionException.Create("Required", source, "server-tls-name");

		return policy with { Name = name };
	}
	#endregion

	#region 元数据转换
	private static BindingDocument.Connection Connection(Definition.Endpoint endpoint) => new(endpoint.Scheme, endpoint.Host, endpoint.Port, endpoint.Path);

	private static BindingDocument.RoutePolicy Policy(Definition.ServerPolicy policy, Diagnostic.Location source) => new()
	{
		FailureCount = Number(policy.FailureCount, source),
		FailureTimeout = Milliseconds(policy.FailureTimeout),
		Retry = policy.Retry.Conditions.ToArray(),
		RetryCount = Number(policy.Retry.Count, source),
		Affinity = policy.Affinity.Kind,
		Cookie = policy.Affinity.Kind == "cookie" ? policy.Affinity.Cookie : null,
		Health = policy.Health is not { } health ? null : new()
		{
			Path = health.Path,
			Interval = Milliseconds(health.Interval),
			ConnectTimeout = Milliseconds(health.ConnectTimeout),
			SendTimeout = Milliseconds(health.SendTimeout),
			ReadTimeout = Milliseconds(health.ReadTimeout),
			FailureCount = Number(health.FailureCount, source),
			RecoveryCount = Number(health.RecoveryCount, source),
			Status = health.Status.ToArray(),
			Headers = health.Headers.ToDictionary(header => header.Name, header => header.Value, StringComparer.OrdinalIgnoreCase),
		},
	};

	private static decimal Milliseconds(TimeSpan duration) => (decimal)duration.Ticks / TimeSpan.TicksPerMillisecond;
	private static long Number(BigInteger value, Diagnostic.Location source) => value > long.MaxValue ? throw DefinitionException.Create("Capability", source, value) : (long)value;

	internal static string NormalizePath(string path, Diagnostic.Location source)
	{
		path = path.Length > 1 && path.EndsWith('/') ? path[..^1] : path;

		if(path.Length == 0 || !path.StartsWith('/') || path.Any(character => !char.IsAsciiLetterOrDigit(character) && character is not ('/' or '-' or '.' or '_' or '~')) ||
		   path.Length > 1 && path[1..].Split('/').Any(part => part is "" or "." or ".."))
			throw DefinitionException.Create("Value", source, path);

		return path.ToLowerInvariant();
	}

	private static bool Contains(string parent, string path) => parent == "/" || path == parent || path.StartsWith(parent + "/", StringComparison.Ordinal);
	#endregion
}
