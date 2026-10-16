/*
 *   _____                                ______
 *  /_   /  ____  ____  ____  _________  / __/ /_
 *    / /  / __ \/ __ \/ __ \/ ___/ __ \/ /_/ __/
 *   / /__/ /_/ / / / / /_/ /\_ \/ /_/ / __/ /_
 *  /____/\____/_/ /_/\__  /____/\____/_/  \__/
 *                   /____/
 *
 * Authors:
 *   钟峰(Popeye Zhong) <zongsoft@gmail.com>
 *
 * The MIT License (MIT)
 *
 * Copyright (C) 2020-2026 Zongsoft Corporation <http://www.zongsoft.com>
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
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Numerics;
using System.Globalization;
using System.Collections.Generic;
using System.Security.Cryptography;

namespace Zongsoft.Tools.Packager.Web;

partial class Configurator
{
	internal sealed partial class Nginx : IConfigurator
	{
		#region 配置生成
		public string Name => "nginx";

		public Result Configure(Definition definition, Context context)
		{
			ArgumentNullException.ThrowIfNull(definition);
			ArgumentNullException.ThrowIfNull(context);

			if(context.PackageName is "." or ".." || context.PackageName.Any(character => !char.IsAsciiLetterOrDigit(character) && character is not ('.' or '_' or '-')))
				throw DefinitionException.Create("Value", default, context.PackageName);

			var model = definition.Resolve(context, new(this.Name));
			var blocks = new List<Directive>();
			var listeners = new Dictionary<string, Diagnostic.Location>(StringComparer.OrdinalIgnoreCase);
			var cookies = new Dictionary<string, string>(StringComparer.Ordinal);

			var sites = model.Sites.Select(site => Routing.Resolve(site, context)).ToArray();

			foreach(var site in sites)
				this.GenerateSite(site, context, blocks, listeners, cookies);

			var path = $".web/nginx/{context.PackageName}.conf";
			var files = new List<Result.File>
			{
				new(path, Writer.Generate(blocks), Utility.Unix.Mode644),
				new(path + ".template", Writer.Generate(blocks, true), Utility.Unix.Mode644),
			};

			var bindings = Bindings.Export(sites, blocks, context.Bindings);
			_ = bindings.Serialize();

			return new(this.Name, files, model.Diagnostics) { Bindings = bindings };
		}

		private void GenerateSite(Definition.Site site, Context context, List<Directive> blocks, Dictionary<string, Diagnostic.Location> listeners, Dictionary<string, string> cookies)
		{
			var body = new List<Directive>();
			var native = Native(site.Directives, true);
			var name = "binding_" + Hash(context.PackageName + "\n" + site.Name.ToLowerInvariant());
			var kind = "$" + name + "_kind";
			var target = "$" + name + "_target";
			var missingHttp = "@" + name + "_http_missing";
			var missingGrpc = "@" + name + "_grpc_missing";

			foreach(var binding in site.Bindings)
			{
				var address = binding.Address.Contains(':') ? $"[{binding.Address}]:{binding.Port}" : $"{binding.Address}:{binding.Port}";
				body.Add(binding.Scheme == "https" ? Leaf("listen", address, "ssl") : Leaf("listen", address));
			}

			if(!native.Any(item => item.Name == "server_name"))
				body.Add(Leaf("server_name", site.Hosts.Count == 0 ? [""] : site.Hosts.ToArray()));

			if(site.Certificates != null)
			{
				body.Add(Resource("ssl_certificate", site.Certificates.File));
				body.Add(Resource("ssl_certificate_key", site.Certificates.Key));
			}

			var http2 = native.FirstOrDefault(item => item.Name == "http2");

			if(site.Routes.Any(route => route.Mapping.RequestKind == "grpc"))
			{
				if(http2 != null && (http2.Arguments.Count != 1 || Literal(http2.Arguments[0].Value) != "on"))
					throw DefinitionException.Create("Capability", site.Source, "gRPC ingress requires http2 on");

				if(http2 == null)
					body.Add(Leaf("http2", "on"));
			}

			body.AddRange(native);
			ValidateListeners(site, body, listeners);
			var authority = "$http_host";

			if(body.Any(item => item.Name == "listen" && item.Arguments.Any(argument => Literal(argument.Value) == "quic")))
			{
				authority = "$" + name + "_authority";
				blocks.Add(new("map", [new("$http_host", ArgumentKind.Expression), new(authority, ArgumentKind.Expression)],
				[
					Expression("default", "$http_host"),
					Expression("\"\"", "$host$is_request_port$request_port"),
				]));
			}

			blocks.Add(new("map", [new("$http_content_type", ArgumentKind.Expression), new(kind, ArgumentKind.Expression)],
			[
				Leaf("default", "http"),
				Leaf("\"~*^application/grpc(?:[+][^;[:space:]]+)?(?:[[:space:]]*;|[[:space:]]*$)\"", "grpc"),
			]));

			var dispatch = new List<Directive> { Leaf("default", missingHttp) };
			var pools = new Dictionary<string, string>(StringComparer.Ordinal);
			var index = 0;

			foreach(var route in site.Routes)
			{
				var location = "@" + name + "_" + (index++).ToString(CultureInfo.InvariantCulture);
				var pattern = "~*^" + route.Mapping.RequestKind + ":" +
					(route.Path == "/" ? "/" : Regex.Escape(route.Path) + "(?:/|[?]|$)");

				dispatch.Add(Leaf("\"" + pattern.Replace("\\", "\\\\") + "\"", location));
				body.Add(new("location", [new(location)], this.GenerateRoute(site, route, context, authority, blocks, body, pools, cookies)));
			}

			dispatch.Add(Leaf("\"~^grpc:\"", missingGrpc));
			blocks.Add(new("map", [new("\"" + kind + ":$request_uri\"", ArgumentKind.Expression), new(target, ArgumentKind.Expression)], dispatch));

			// 命名位置跳转保留原始 URI、查询参数和请求方法。
			body.Add(new("location", [new("/")], [Expression("error_page", "418", "=", target), Leaf("return", "418")]));
			body.Add(new("location", [new(missingHttp)], [Leaf("return", "404")]));
			body.Add(new("location", [new(missingGrpc)],
			[
				.. GrpcChecks(),
				Leaf("default_type", "application/grpc"),
				Leaf("add_header", "grpc-status", "12", "always"),
				Leaf("add_header", "grpc-message", "Unimplemented", "always"),
				Leaf("return", "200"),
			]));
			blocks.Add(new("server", [], body));
		}

		private static Directive[] GrpcChecks() =>
		[
			new("if", [new("($server_protocol !~ \"^HTTP/[23][.]\")", ArgumentKind.Expression)], [Leaf("return", "400")]),
			new("if", [new("($request_method != POST)", ArgumentKind.Expression)], [Leaf("add_header", "Allow", "POST", "always"), Leaf("return", "405")]),
		];

		private IReadOnlyList<Directive> GenerateRoute(Definition.Site site, Definition.Route route, Context context, string authority, List<Directive> blocks,
			List<Directive> body, Dictionary<string, string> pools, Dictionary<string, string> cookies)
		{
			var directives = new List<Directive>();
			var group = route.Server;
			var policy = group.Policy;
			var grpc = route.Mapping.RequestKind == "grpc";
			var prefix = grpc ? "grpc" : "proxy";

			if(grpc)
				directives.AddRange(GrpcChecks());

			if(policy.Balance == "least-requests" || grpc && (policy.Health != null || policy.Affinity.Kind == "cookie"))
				throw DefinitionException.Create("Capability", route.Source, "Nginx upstream policy");

			Number(policy.FailureCount, route.Source, context.MaximumInteger);
			Duration(policy.FailureTimeout, true, route.Source, context.MaximumInteger);
			Number(policy.Retry.Count + 1, route.Source, context.MaximumInteger);

			string address;
			var pooled = group.Kind == Definition.ServerKind.Pool || policy.Affinity.Kind == "cookie" || policy.Health != null || policy.Balance != "round-robin" ||
				policy.FailureCount != 3 || policy.FailureTimeout != TimeSpan.FromSeconds(30);
			var scheme = grpc ? group.Members[0].Endpoint.Scheme == "https" ? "grpcs" : "grpc" : group.Members[0].Endpoint.Scheme;

			if(pooled)
			{
				var key = Fingerprint(group) + route.Mapping.Target.Binding + route.Mapping.Target.Endpoint;

				if(!pools.TryGetValue(key, out var pool))
				{
					pool = "hoster_" + Hash(context.PackageName + "\n" + site.Name.ToLowerInvariant() + "\n" + key);
					pools.Add(key, pool);
					this.GeneratePool(site, route, group, pool, blocks, cookies, context.MaximumInteger);

					if(policy.Health != null)
						body.Add(Health(group, pool, context));
				}

				address = scheme + "://" + pool;
			}
			else
				address = scheme + "://" + group.Members[0].Endpoint.Authority;

			directives.Add(new(prefix + "_pass", [Target(address, route, !pooled)]));
			Tls(policy.Tls, directives, prefix, context.SystemTrust);

			if(!grpc)
				directives.Add(Leaf("proxy_http_version", Zongsoft.Communication.NetworkProtocol.Parse(route.Mapping.Protocol).HttpVersion == 2 ? "2" : "1.1"));

			if(policy.Retry.Count == 0 || policy.Retry.Conditions.Contains("off"))
				directives.Add(Leaf(prefix + "_next_upstream", "off"));
			else
			{
				directives.Add(Leaf(prefix + "_next_upstream", policy.Retry.Conditions.ToArray()));
				directives.Add(Leaf(prefix + "_next_upstream_tries", (policy.Retry.Count + 1).ToString(CultureInfo.InvariantCulture)));
			}

			foreach(var header in route.Headers)
			{
				if(grpc && (header.Name.Equals("Connection", StringComparison.OrdinalIgnoreCase) || header.Name.Equals("Upgrade", StringComparison.OrdinalIgnoreCase)))
					continue;

				var value = header.Expression && header.Value == "$http_host" && header.Name.Equals("Host", StringComparison.OrdinalIgnoreCase) ? authority : header.Value;
				directives.Add(new(prefix + "_set_header", [new(header.Name), new(value, header.Expression ? ArgumentKind.Raw : ArgumentKind.Literal)]));
			}

			var native = Native(route.Directives, false);

			foreach(var directive in native)
				directives.RemoveAll(item => item.Name == directive.Name);

			directives.AddRange(native);
			return directives;
		}

		private static Argument Target(string value, Definition.Route route, bool reference = true) =>
			reference && route.Mapping.Target.Kind == "binding" ?
			new(value, ArgumentKind.Binding, Binding: route.Mapping.Target.Binding, Endpoint: route.Mapping.Target.Endpoint) : new(value);

		private void GeneratePool(Definition.Site site, Definition.Route route, Definition.ServerGroup group, string name, List<Directive> blocks, Dictionary<string, string> cookies, long maximum)
		{
			var body = new List<Directive>();
			var policy = group.Policy;

			if(policy.Balance == "least-connections")
				body.Add(Leaf("least_conn"));

			if(policy.Health != null || policy.Affinity.Kind == "cookie")
				body.Add(Leaf("zone", name, "64k"));

			foreach(var member in group.Members)
			{
				Number(member.Weight, route.Source, maximum);
				Number(policy.FailureCount, route.Source, maximum);

				body.Add(new("server", [
					Target(member.Endpoint.Authority, route),
					new("weight=" + member.Weight.ToString(CultureInfo.InvariantCulture)),
					new("max_fails=" + policy.FailureCount.ToString(CultureInfo.InvariantCulture)),
					new("fail_timeout=" + Duration(policy.FailureTimeout, true, route.Source, maximum)),
				]));
			}

			if(policy.Affinity.Kind == "cookie")
			{
				var cookie = string.IsNullOrEmpty(policy.Affinity.Cookie) ? "HOSTER_ROUTE_" + name[7..] : policy.Affinity.Cookie;

				foreach(var host in Names(site))
				{
					var key = host.ToLowerInvariant() + "/" + cookie;
					if(cookies.TryGetValue(key, out var previous) && previous != name)
						throw DefinitionException.Create("Duplicate", route.Source, cookie);

					cookies[key] = name;
				}

				var arguments = new List<string> { "cookie", cookie, "path=/", "httponly", "samesite=lax" };
				var secure = site.Bindings.Count > 0 ? site.Bindings.All(item => item.Scheme == "https") :
					site.Directives.Where(item => item.Name == "listen").All(item => Tokenize(item.Value, item.Source).Contains("ssl"));

				if(secure)
					arguments.Add("secure");

				body.Add(Leaf("sticky", arguments.ToArray()));
			}

			blocks.Add(new("upstream", [new(name)], body));
			if(policy.Health != null)
				blocks.Add(new("match", [new(name + "_health")], [Leaf("status", Status(policy.Health.Status))]));
		}

		private static Directive Health(Definition.ServerGroup group, string pool, Context context)
		{
			var health = group.Policy.Health;
			var maximum = context.MaximumInteger;
			Number(health.FailureCount, default, maximum);
			Number(health.RecoveryCount, default, maximum);

			var body = new List<Directive>
			{
				Leaf("proxy_pass", group.Members[0].Endpoint.Scheme + "://" + pool),
				Leaf("proxy_method", "GET"),
				Leaf("proxy_connect_timeout", Duration(health.ConnectTimeout, false, default, maximum)),
				Leaf("proxy_send_timeout", Duration(health.SendTimeout, false, default, maximum)),
				Leaf("proxy_read_timeout", Duration(health.ReadTimeout, false, default, maximum)),
				Leaf("proxy_pass_request_headers", "off"),
				Leaf("proxy_pass_request_body", "off"),
			};

			var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
			foreach(var header in health.Headers)
				headers[header.Name] = header.Value;

			if(!headers.ContainsKey("Host"))
				body.Add(Expression("proxy_set_header", "Host", "$proxy_host"));

			if(!headers.ContainsKey("Connection"))
				body.Add(Leaf("proxy_set_header", "Connection", "close"));

			foreach(var header in headers)
				body.Add(Leaf("proxy_set_header", header.Key, header.Value));

			Tls(group.Policy.Tls, body, "proxy", context.SystemTrust);

			body.Add(Leaf("health_check", "uri=" + health.Path,
				"interval=" + Duration(health.Interval, false, default, maximum),
				"fails=" + health.FailureCount.ToString(CultureInfo.InvariantCulture),
				"passes=" + health.RecoveryCount.ToString(CultureInfo.InvariantCulture),
				"match=" + pool + "_health"));

			return new("location", [new("@" + pool + "_health")], body);
		}

		private static void Tls(Definition.TlsPolicy tls, List<Directive> directives, string prefix, string systemTrust)
		{
			if(tls == null)
				return;

			directives.Add(Leaf(prefix + "_ssl_verify", tls.Verify ? "on" : "off"));
			directives.Add(Leaf(prefix + "_ssl_server_name", string.IsNullOrEmpty(tls.Name) ? "off" : "on"));

			if(!string.IsNullOrEmpty(tls.Name))
				directives.Add(Leaf(prefix + "_ssl_name", tls.Name));

			if(tls.Trust is { } trust)
				directives.Add(Resource(prefix + "_ssl_trusted_certificate", trust));
			else if(tls.Verify)
				directives.Add(Leaf(prefix + "_ssl_trusted_certificate", systemTrust));
		}

		private static void Number(BigInteger number, Diagnostic.Location source, long maximum)
		{
			if(number > maximum)
				throw DefinitionException.Create("Capability", source, number);
		}

		private static string Duration(TimeSpan duration, bool seconds, Diagnostic.Location source, long maximum)
		{
			var unit = seconds ? TimeSpan.TicksPerSecond : TimeSpan.TicksPerMillisecond;
			if(duration.Ticks % unit != 0 || duration.Ticks / unit > maximum)
				throw DefinitionException.Create("Capability", source, duration);

			return (duration.Ticks / unit).ToString(CultureInfo.InvariantCulture) + (seconds ? "s" : "ms");
		}

		private static string[] Status(IReadOnlyList<int> statuses)
		{
			var result = new List<string>();

			for(var index = 0; index < statuses.Count; index++)
			{
				var start = statuses[index];
				var end = start;
				while(index + 1 < statuses.Count && statuses[index + 1] == end + 1)
					end = statuses[++index];

				result.Add(start == end ? start.ToString(CultureInfo.InvariantCulture) : $"{start}-{end}");
			}

			return result.ToArray();
		}

		private static string Hash(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant()[..24];
		private static string Fingerprint(Definition.ServerGroup group)
		{
			var policy = group.Policy;
			var health = policy.Health;

			return JsonSerializer.Serialize(new
			{
				Members = group.Members.Select(item => new
				{
					item.Endpoint,
					Weight = item.Weight.ToString(CultureInfo.InvariantCulture),
				}),
				policy.Balance,
				Failures = policy.FailureCount.ToString(CultureInfo.InvariantCulture),
				policy.FailureTimeout,
				Retry = new { policy.Retry.Conditions, Count = policy.Retry.Count.ToString(CultureInfo.InvariantCulture) },
				policy.Affinity,
				policy.Tls,
				Health = health == null ? null : new
				{
					health.Path,
					health.Interval,
					health.ConnectTimeout,
					health.SendTimeout,
					health.ReadTimeout,
					Failures = health.FailureCount.ToString(CultureInfo.InvariantCulture),
					Recoveries = health.RecoveryCount.ToString(CultureInfo.InvariantCulture),
					health.Status,
					Headers = health.Headers.OrderBy(item => item.Name, StringComparer.OrdinalIgnoreCase).Select(item => new { Name = item.Name.ToLowerInvariant(), item.Value }),
				},
			});
		}
		#endregion
	}
}
