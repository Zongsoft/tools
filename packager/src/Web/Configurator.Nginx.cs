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

			foreach(var site in model.Sites)
				this.GenerateSite(site, context, blocks, listeners, cookies);

			return new(this.Name, [new($".web/nginx/{context.PackageName}.conf", Writer.Generate(blocks), Utility.Unix.Mode644)], model.Diagnostics);
		}

		private void GenerateSite(Definition.Site site, Context context, List<Directive> blocks, Dictionary<string, Diagnostic.Location> listeners, Dictionary<string, string> cookies)
		{
			var body = new List<Directive>();
			var native = Native(site.Directives, true);

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

			body.AddRange(native);
			ValidateListeners(site, body, listeners);
			var pools = new Dictionary<string, string>(StringComparer.Ordinal);

			foreach(var route in site.Routes)
			{
				var directives = new List<Directive>();

				if(route.Server != null)
				{
					var group = route.Server;
					var policy = group.Policy;

					if(policy.Balance == "least-requests")
						throw DefinitionException.Create("Capability", route.Source, "server-balance=least-requests");

					Number(policy.FailureCount, route.Source, context.MaximumInteger);
					Duration(policy.FailureTimeout, true, route.Source, context.MaximumInteger);

					string target;
					var pooled = group.Kind == Definition.ServerKind.Pool || policy.Affinity.Kind == "cookie" || policy.Health != null || policy.Balance != "round-robin" ||
						policy.FailureCount != 3 || policy.FailureTimeout != TimeSpan.FromSeconds(30);

					if(pooled)
					{
						var key = Fingerprint(group);

						if(!pools.TryGetValue(key, out var pool))
						{
							pool = "hoster_" + Hash(context.PackageName + "\n" + site.Name.ToLowerInvariant() + "\n" + key);
							pools.Add(key, pool);
							this.GeneratePool(site, route, group, pool, blocks, cookies, context.MaximumInteger);

							if(policy.Health != null)
								body.Add(Health(group, pool, context.MaximumInteger));
						}

						target = group.Members[0].Endpoint.Scheme + "://" + pool;
					}
					else
						target = group.Members[0].Endpoint.Address;

					directives.Add(Leaf("proxy_pass", target));
					Tls(policy.Tls, directives);

					if(policy.Retry.Count == 0 || policy.Retry.Conditions.Contains("off"))
						directives.Add(Leaf("proxy_next_upstream", "off"));
					else
					{
						directives.Add(Leaf("proxy_next_upstream", policy.Retry.Conditions.ToArray()));
						directives.Add(Leaf("proxy_next_upstream_tries", (policy.Retry.Count + 1).ToString(CultureInfo.InvariantCulture)));
					}
				}

				if(route.WebSocket)
					directives.Add(Leaf("proxy_http_version", "1.1"));

				foreach(var header in route.Headers)
					directives.Add(new("proxy_set_header", [new(header.Name), new(header.Value, header.Expression ? ArgumentKind.Raw : ArgumentKind.Literal)]));

				var routeNative = Native(route.Directives, false);
				ValidateProxy(route, routeNative);

				foreach(var raw in routeNative)
					directives.RemoveAll(item => item.Name == raw.Name);

				directives.AddRange(routeNative);
				var arguments = route.Match switch
				{
					Definition.MatchKind.Exact => new Argument[] { new("="), new(route.Path) },
					Definition.MatchKind.Regex => [new("~*"), new(route.Path, ArgumentKind.Pattern)],
					_ => [new(route.Path)],
				};

				body.Add(new("location", arguments, directives));
			}

			blocks.Add(new("server", [], body));
		}

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

				body.Add(Leaf("server", member.Endpoint.Authority,
					"weight=" + member.Weight.ToString(CultureInfo.InvariantCulture),
					"max_fails=" + policy.FailureCount.ToString(CultureInfo.InvariantCulture),
					"fail_timeout=" + Duration(policy.FailureTimeout, true, route.Source, maximum)));
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

		private static Directive Health(Definition.ServerGroup group, string pool, long maximum)
		{
			var health = group.Policy.Health;
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

			Tls(group.Policy.Tls, body);

			body.Add(Leaf("health_check", "uri=" + health.Path,
				"interval=" + Duration(health.Interval, false, default, maximum),
				"fails=" + health.FailureCount.ToString(CultureInfo.InvariantCulture),
				"passes=" + health.RecoveryCount.ToString(CultureInfo.InvariantCulture),
				"match=" + pool + "_health"));

			return new("location", [new("@" + pool + "_health")], body);
		}

		private static void Tls(Definition.TlsPolicy tls, List<Directive> directives)
		{
			if(tls == null)
				return;

			directives.Add(Leaf("proxy_ssl_verify", tls.Verify ? "on" : "off"));
			directives.Add(Leaf("proxy_ssl_server_name", string.IsNullOrEmpty(tls.Name) ? "off" : "on"));

			if(!string.IsNullOrEmpty(tls.Name))
				directives.Add(Leaf("proxy_ssl_name", tls.Name));

			if(tls.Trust is { } trust)
				directives.Add(Resource("proxy_ssl_trusted_certificate", trust));
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
