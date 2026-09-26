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
using System.Net;
using System.Net.Sockets;
using System.Numerics;
using System.Globalization;
using System.Collections.Generic;
using System.Text.RegularExpressions;

using Zongsoft.Common;

namespace Zongsoft.Tools.Packager.Web;

partial class Definition
{
	/// <summary>在声明覆盖后解析有效值，构建站点、路径及后端策略。</summary>
	private sealed class Resolver(Configurator.Context context, Rules rules)
	{
		#region 成员字段
		private static readonly Regex _address = new(@"^(?<scheme>https?)://(?<host>\[[^\]]+\]|[^/:?#@\s]+)(?::(?<port>[0-9]+))?(?<slash>/?)$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
		private static readonly Regex _header = new(@"^[!#$%&'*+\-.^_`|~0-9A-Za-z]+$", RegexOptions.CultureInvariant);
		private readonly List<Diagnostic> _diagnostics = [];
		#endregion

		#region 模型解析
		internal Model Resolve(Scope root, IReadOnlyList<Scope> sites)
		{
			if(sites.Count == 0)
				throw DefinitionException.Create("Required", root.Source, "site");

			var defaults = Merge(null, root);
			var result = new List<Site>();

			foreach(var site in sites)
				result.Add(this.ResolveSite(site, defaults));

			return new(result.AsReadOnly(), _diagnostics.AsReadOnly());
		}

		private Site ResolveSite(Scope scope, Settings defaults)
		{
			var values = Merge(defaults, scope);
			var native = this.Native(scope);
			var rawListen = native.Where(item => item.Name == "listen").ToArray();
			var rawHosts = native.Any(item => item.Name == "server_name");
			var bindings = rawListen.Length == 0 ? this.Bindings(values) : [];
			var hosts = rawHosts ? [] : this.Hosts(values);
			var secure = bindings.Any(item => item.Scheme == "https") || rawListen.Any(item => this.RawValue(item).Split(' ', StringSplitOptions.RemoveEmptyEntries).Contains("ssl", StringComparer.OrdinalIgnoreCase));
			var rawCertificate = native.Where(item => item.Name is "ssl_certificate" or "ssl_certificate_key").ToArray();

			if(bindings.Count == 0 && rawListen.Length == 0)
				throw DefinitionException.Create("Required", scope.Source, "bind");

			if(rawCertificate.Length > 0 && (!rawCertificate.Any(item => item.Name == "ssl_certificate") || !rawCertificate.Any(item => item.Name == "ssl_certificate_key")))
				throw DefinitionException.Create("Required", rawCertificate[0].Source, "ssl_certificate + ssl_certificate_key");

			if(rawListen.Length > 0)
				this.Overridden(values, "bind!", rawListen[0].Source);

			if(rawHosts)
				this.Overridden(values, "host", native.First(item => item.Name == "server_name").Source);

			Certificate certificate = null;
			if(rawCertificate.Length > 0)
			{
				this.Overridden(values, "certificate", rawCertificate[0].Source);
				this.Overridden(values, "certificate-key", rawCertificate[0].Source);
			}
			else if(secure)
			{
				var file = values.Get("certificate") is { } entry ? this.Resource(entry, false).Value : new ResourceReference($".certificates/{context.PackageName}.pem", true);
				var key = values.Get("certificate-key") is { } keyEntry ? this.Resource(keyEntry, true) ?? file : file;
				certificate = new(file, key);
			}

			var routes = new List<Route>();
			var children = scope.Children.Count == 0 ? [new Scope(string.Empty, 2, scope.Source)] : scope.Children;
			var seen = new HashSet<(MatchKind, string)>();

			foreach(var child in children)
			{
				var route = this.ResolveRoute(child, values, native);
				if(!seen.Add((route.Match, route.Path)))
					throw DefinitionException.Create("Duplicate", child.Source, route.Path);

				routes.Add(route);
			}

			return new(scope.Name, hosts, bindings, certificate, routes.AsReadOnly(),
				native.Where(item => item.Name != "proxy_set_header").Select(this.ExpandRaw).ToArray(), scope.Source);
		}

		private Route ResolveRoute(Scope scope, Settings parent, IReadOnlyList<RawDirective> siteNative)
		{
			var values = Merge(parent, scope);
			var native = this.Native(scope);
			var match = this.Choice(values, "match", "prefix", ["prefix", "exact", "regex"]) switch
			{
				"exact" => MatchKind.Exact,
				"regex" => MatchKind.Regex,
				_ => MatchKind.Prefix,
			};

			var path = this.Value(values, "path", match == MatchKind.Regex ? null : "/");
			if(match != MatchKind.Regex && string.IsNullOrEmpty(path))
				path = "/";

			if(string.IsNullOrEmpty(path) || path.Any(char.IsControl) || match != MatchKind.Regex && (!path.StartsWith('/') || path.Contains('?') || path.Contains('#')))
				throw DefinitionException.Create("Value", values.Source("path", scope.Source), path);

			var rawProxy = native.FirstOrDefault(item => item.Name == "proxy_pass");
			ServerGroup server = null;

			if(rawProxy.Name != null)
			{
				foreach(var entry in values.Servers)
					this.Warn(entry.Source, rawProxy.Source);
			}
			else
				server = this.Server(values, scope.Source);

			var forwarded = this.Boolean(values, "forwarded", true);
			var websocket = this.Boolean(values, "websocket", false);
			var headers = this.Headers(values, siteNative, native, forwarded, websocket);

			return new(scope.Name, path, match, server, headers, websocket,
				native.Where(item => item.Name != "proxy_set_header").Select(this.ExpandRaw).ToArray(), scope.Source);
		}
		#endregion

		#region 绑定与资源
		private IReadOnlyList<Binding> Bindings(Settings values)
		{
			var result = new List<Binding>();
			var seen = new HashSet<Binding>();

			foreach(var entry in values.Values.Values.Where(item => item.Name.StartsWith("bind!", StringComparison.OrdinalIgnoreCase)))
			{
				foreach(var address in this.Value(entry).Split(',', StringSplitOptions.TrimEntries))
				{
					var match = _address.Match(address);
					if(!match.Success || match.Groups["slash"].Length != 0)
						throw DefinitionException.Create("Value", entry.Source, address);

					var scheme = match.Groups["scheme"].Value.ToLowerInvariant();
					var host = match.Groups["host"].Value;
					var port = GetPort(match, scheme, entry.Source);

					if(host == "*")
						host = "0.0.0.0";
					else if(!IPAddress.TryParse(host.Trim('[', ']'), out var ip) || ip.AddressFamily == AddressFamily.InterNetworkV6 && !host.StartsWith('['))
						throw DefinitionException.Create("Value", entry.Source, host);
					else
						host = ip.ToString();

					var binding = new Binding(scheme, host, port);
					if(seen.Add(binding))
						result.Add(binding);
				}
			}

			return result.AsReadOnly();
		}

		private IReadOnlyList<string> Hosts(Settings values)
		{
			var text = this.Value(values, "host");
			if(text == null)
				return [];

			var result = text.Split(',', StringSplitOptions.TrimEntries);
			if(result.Any(item => string.IsNullOrEmpty(item) || item.Any(char.IsWhiteSpace) || item.IndexOfAny([';', '{', '}', '#', '"', '\'', '$', '/', '\\']) >= 0))
				throw DefinitionException.Create("Value", values.Source("host"), text);

			return result.Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
		}

		private static int GetPort(Match match, string scheme, Diagnostic.Location source)
		{
			if(!match.Groups["port"].Success)
				return scheme == "https" ? 443 : 80;

			if(!int.TryParse(match.Groups["port"].Value, NumberStyles.None, CultureInfo.InvariantCulture, out var port) || port is < 1 or > 65535)
				throw DefinitionException.Create("Value", source, match.Value);

			return port;
		}

		private static Endpoint Address(string value, Diagnostic.Location source)
		{
			var match = _address.Match(value);
			if(!match.Success)
				throw DefinitionException.Create("Value", source, value);

			var scheme = match.Groups["scheme"].Value.ToLowerInvariant();
			var host = match.Groups["host"].Value;

			if(host.StartsWith('[') && (!IPAddress.TryParse(host[1..^1], out var ipv6) || ipv6.AddressFamily != AddressFamily.InterNetworkV6))
				throw DefinitionException.Create("Value", source, value);

			host = host.Trim('[', ']');
			if(IPAddress.TryParse(host, out var ip))
			{
				if(ip.Equals(IPAddress.Any) || ip.Equals(IPAddress.IPv6Any) || ip.AddressFamily == AddressFamily.InterNetworkV6 && ip.ScopeId != 0)
					throw DefinitionException.Create("Value", source, value);

				host = ip.ToString();
			}
			else if(Uri.CheckHostName(host) != UriHostNameType.Dns || host == "*")
				throw DefinitionException.Create("Value", source, value);
			else
				host = new IdnMapping().GetAscii(host).ToLowerInvariant();

			return new(scheme, host, GetPort(match, scheme, source));
		}

		private ResourceReference? Resource(Declaration entry, bool optional)
		{
			var value = this.Value(entry);
			if(optional && value.Length == 0)
				return null;

			if(!value.StartsWith("file:", StringComparison.OrdinalIgnoreCase) || value.Length <= 6 || value[5] != '/' || value[6] == '/' || value.Any(char.IsControl))
				throw DefinitionException.Create("Value", entry.Source, value);

			return new(value[5..]);
		}
		#endregion

		#region 后端策略
		private ServerGroup Server(Settings values, Diagnostic.Location source)
		{
			var members = new List<ServerMember>();
			var pool = values.Servers.Count > 0 && values.Servers[0].Name.Contains('!');
			var kind = pool ? ServerKind.Pool : ServerKind.Single;

			if(pool)
			{
				var seen = new HashSet<Endpoint>();

				foreach(var entry in values.Servers)
				{
					var text = this.Value(entry);
					var parts = text.Split((char[])null, StringSplitOptions.RemoveEmptyEntries);

					if(parts.Length is < 1 or > 2 || parts.Length == 2 && !parts[1].StartsWith("weight=", StringComparison.OrdinalIgnoreCase))
						throw DefinitionException.Create("Value", entry.Source, text);

					var endpoint = Address(parts[0], entry.Source);
					var weight = parts.Length == 1 ? BigInteger.One : Integer(parts[1][7..], entry.Source, 1);

					if(!seen.Add(endpoint))
						throw DefinitionException.Create("Duplicate", entry.Source, endpoint.Address);

					if(members.Count > 0 && members[0].Endpoint.Scheme != endpoint.Scheme)
						throw DefinitionException.Create("Value", entry.Source, endpoint.Scheme);

					members.Add(new(entry.Name[(entry.Name.IndexOf('!') + 1)..], endpoint, weight));
				}
			}
			else
			{
				var entry = values.Servers.FirstOrDefault();
				var address = entry == null ? "~" : this.Value(entry);
				var location = entry?.Source ?? source;

				if(address == "~")
				{
					kind = ServerKind.Application;
					address = context.ApplicationAddress;
					if(string.IsNullOrWhiteSpace(address))
						throw DefinitionException.Create("Required", location, "server=~ / listen");
				}

				members.Add(new(string.Empty, Address(address, location), BigInteger.One));
			}

			return new(kind, members.AsReadOnly(), this.Policy(values, members));
		}

		private ServerPolicy Policy(Settings values, IReadOnlyList<ServerMember> members)
		{
			var balance = this.Choice(values, "server-balance", "round-robin", ["round-robin", "least-connections", "least-requests"]);
			var affinity = this.Choice(values, "server-affinity", "none", ["none", "cookie"]);
			var cookie = this.Value(values, "server-affinity-cookie");

			if(!string.IsNullOrEmpty(cookie) && !_header.IsMatch(cookie))
				throw DefinitionException.Create("Value", values.Source("server-affinity-cookie"), cookie);

			var retry = this.Value(values, "server-retry", "error,timeout").Split(',', StringSplitOptions.TrimEntries);
			retry = retry.Select(item => item.ToLowerInvariant()).Distinct().ToArray();

			if(retry.Any(item => item is not ("error" or "timeout" or "invalid_header" or
				"http_500" or "http_502" or "http_503" or "http_504" or "http_403" or "http_404" or "http_429" or "off")) ||
				retry.Contains("off") && retry.Length != 1)
				throw DefinitionException.Create("Value", values.Source("server-retry"), string.Join(',', retry));

			var retryCount = this.Count(values, "server-retry-count", 1);
			if(retryCount >= context.MaximumInteger)
				throw DefinitionException.Create("Capability", values.Source("server-retry-count"), retryCount);

			var tls = this.Tls(values, members);
			var health = this.Health(values);
			return new(balance, this.Count(values, "server-failure-count", 3), this.Duration(values, "server-failure-timeout", "30s"),
				new(retry, retryCount), new(affinity, cookie), tls, health);
		}

		private TlsPolicy Tls(Settings values, IReadOnlyList<ServerMember> members)
		{
			if(members[0].Endpoint.Scheme != "https")
				return null;

			var verify = this.Boolean(values, "server-tls-verify", false);
			var trust = values.Get("server-tls-trust") is { } trustEntry ? this.Resource(trustEntry, true) : null;
			var name = this.Value(values, "server-tls-name");

			if(string.IsNullOrEmpty(name))
			{
				var host = members[0].Endpoint.Host;
				name = !IPAddress.TryParse(host, out _) && members.All(item => string.Equals(item.Endpoint.Host, host, StringComparison.OrdinalIgnoreCase)) ? host : null;
			}
			else if(Uri.CheckHostName(name) is not (UriHostNameType.Dns or UriHostNameType.IPv4 or UriHostNameType.IPv6))
				throw DefinitionException.Create("Value", values.Source("server-tls-name"), name);

			if(verify && string.IsNullOrEmpty(name))
				throw DefinitionException.Create("Required", values.Source("server-tls-name", values.Scope.Source), "server-tls-name");

			return new(verify, trust, name);
		}
		#endregion

		#region 健康检查
		private HealthPolicy Health(Settings values)
		{
			var path = this.Value(values, "server-health");
			var enabled = !string.IsNullOrEmpty(path) && !path.Equals("off", StringComparison.OrdinalIgnoreCase);

			if(enabled && (!path.StartsWith('/') || path.StartsWith("//", StringComparison.Ordinal) || path.Contains('#') || path.Any(char.IsWhiteSpace)))
				throw DefinitionException.Create("Value", values.Source("server-health"), path);

			var statuses = new SortedSet<int>();
			var text = this.Value(values, "server-health-status", "2xx");

			foreach(var item in text.Split(',', StringSplitOptions.TrimEntries))
			{
				int start;
				int end;

				if(item.Length == 3 && item.EndsWith("xx", StringComparison.OrdinalIgnoreCase) && item[0] is >= '1' and <= '5')
				{
					start = (item[0] - '0') * 100;
					end = start + 99;
				}
				else
				{
					var range = item.Split('-', StringSplitOptions.TrimEntries);
					if(range.Length > 2 || !int.TryParse(range[0], NumberStyles.None, CultureInfo.InvariantCulture, out start) || !int.TryParse(range[^1], NumberStyles.None, CultureInfo.InvariantCulture, out end))
						throw DefinitionException.Create("Value", values.Source("server-health-status"), item);

				}

				if(start < 100 || end > 599 || start > end)
					throw DefinitionException.Create("Value", values.Source("server-health-status"), item);

				for(var status = start; status <= end; status++)
					statuses.Add(status);
			}

			var headers = values.Values.Values.Where(item => item.Name.StartsWith("server-health-header!", StringComparison.OrdinalIgnoreCase))
				.Select(entry => this.Header(entry, "server-health-header!".Length)).ToArray();
			var health = new HealthPolicy(path, this.Duration(values, "server-health-interval", "10s"),
				this.Duration(values, "server-health-connect-timeout", "5s"), this.Duration(values, "server-health-send-timeout", "5s"), this.Duration(values, "server-health-read-timeout", "5s"),
				this.Count(values, "server-health-failure-count", 3, 1), this.Count(values, "server-health-recovery-count", 2, 1), statuses.ToArray(), headers);

			return enabled ? health : null;
		}
		#endregion

		#region 请求头
		private Header Header(Declaration entry, int prefix)
		{
			var name = entry.Name[prefix..];
			if(!_header.IsMatch(name))
				throw DefinitionException.Create("Value", entry.Source, name);

			return new(name, context.Expand(entry.Value, entry.Source), false, entry.Source);
		}

		private IReadOnlyList<Header> Headers(Settings values, IReadOnlyList<RawDirective> siteNative, IReadOnlyList<RawDirective> routeNative, bool forwarded, bool websocket)
		{
			var headers = new Dictionary<string, Header>(StringComparer.OrdinalIgnoreCase);

			if(forwarded)
			{
				headers["Host"] = new("Host", "$http_host", true, default);
				headers["X-Real-IP"] = new("X-Real-IP", "$remote_addr", true, default);
				headers["X-Forwarded-For"] = new("X-Forwarded-For", "$proxy_add_x_forwarded_for", true, default);
				headers["X-Forwarded-Proto"] = new("X-Forwarded-Proto", "$scheme", true, default);
			}

			if(websocket)
			{
				headers["Upgrade"] = new("Upgrade", "$http_upgrade", true, default);
				headers["Connection"] = new("Connection", "upgrade", false, default);
			}

			var pending = new Dictionary<string, Func<Header>>(StringComparer.OrdinalIgnoreCase);
			var levels = new Stack<Settings>();

			for(var level = values; level != null; level = level.Parent)
				levels.Push(level);

			while(levels.TryPop(out var level))
			{
				foreach(var entry in level.Scope.Declarations.Where(item => item.Name.StartsWith("header!", StringComparison.OrdinalIgnoreCase)))
				{
					var current = entry;
					pending[entry.Name[7..]] = () => this.Header(current, 7);
				}

				var native = level.Scope.Depth == 1 ? siteNative : level.Scope.Depth == 2 ? routeNative : [];
				foreach(var raw in native.Where(item => item.Name == "proxy_set_header"))
				{
					var tokens = raw.Suffix == null ? Configurator.Nginx.Tokenize(raw.Value, raw.Source) : null;
					var name = Configurator.Nginx.Literal(context.Expand(raw.Suffix ?? tokens.FirstOrDefault(), raw.Source));

					if(name == null || !_header.IsMatch(name))
						throw DefinitionException.Create("Directive", raw.Source, name);

					var current = raw;
					pending[name] = () =>
					{
						var parts = Configurator.Nginx.Tokenize(this.RawValue(current), current.Source);
						if(current.Suffix == null)
							parts = parts.Skip(1).ToArray();

						if(parts.Count > 1)
							throw DefinitionException.Create("Directive", current.Source, current.Name);

						return new(name, parts.Count == 0 ? "\"\"" : parts[0], true, current.Source);
					};
				}
			}

			foreach(var (name, factory) in pending)
			{
				var header = factory();
				if(headers.TryGetValue(name, out var automatic))
					this.Warn(automatic.Source, header.Source);

				headers[name] = header;
			}

			return headers.Values.ToArray();
		}
		#endregion

		#region 原始指令
		private List<RawDirective> Native(Scope scope)
		{
			var items = new Dictionary<string, Declaration>(StringComparer.OrdinalIgnoreCase);

			foreach(var entry in scope.Declarations)
			{
				if(entry.Name.StartsWith(rules.Hoster + ":", StringComparison.OrdinalIgnoreCase))
					items[entry.Name] = entry;
			}

			return items.Values.Select(entry =>
			{
				var key = entry.Name[(rules.Hoster.Length + 1)..];
				var bang = key.IndexOf('!');
				return new RawDirective((bang < 0 ? key : key[..bang]).ToLowerInvariant(), bang < 0 ? null : key[(bang + 1)..], entry.Value, entry.Source);
			}).ToList();
		}

		private string RawValue(RawDirective raw) => context.Expand(raw.Value, raw.Source);
		private RawDirective ExpandRaw(RawDirective raw) => raw with
		{
			Suffix = raw.Suffix == null ? null : context.Expand(raw.Suffix, raw.Source),
			Value = this.RawValue(raw),
		};

		private void Overridden(Settings values, string name, Diagnostic.Location source)
		{
			foreach(var entry in values.Values.Values)
			{
				if(name.EndsWith('!') ? entry.Name.StartsWith(name, StringComparison.OrdinalIgnoreCase) : entry.Name.Equals(name, StringComparison.OrdinalIgnoreCase))
					this.Warn(entry.Source, source);
			}
		}

		private void Warn(Diagnostic.Location overridden, Diagnostic.Location source) =>
			_diagnostics.Add(new("Override", string.Format(Properties.Resources.Web_Override_Message, overridden.Entry), source, Diagnostic.Severity.Warning, overridden));
		#endregion

		#region 声明合并
		private static Settings Merge(Settings parent, Scope scope)
		{
			var values = parent == null ? new Dictionary<string, Declaration>(StringComparer.OrdinalIgnoreCase) : new(parent.Values, StringComparer.OrdinalIgnoreCase);
			var groups = new Dictionary<object, List<Declaration>>(ReferenceEqualityComparer.Instance);
			var servers = parent?.Servers ?? [];

			foreach(var entry in scope.Declarations)
			{
				if(entry.IsServer)
				{
					if(!groups.TryGetValue(entry.Owner, out var group))
						groups.Add(entry.Owner, group = []);

					group.Add(entry);
					servers = group;
				}
				else if(!entry.Name.Contains(':'))
					values[entry.Name] = entry;
			}

			return new(values, servers, parent, scope);
		}
		#endregion

		#region 值解析
		private string Value(Declaration entry) => context.Expand(entry.Value, entry.Source).Trim();

		private string Value(Settings values, string key, string fallback = null) => values.Get(key) is { } entry ? this.Value(entry) : fallback;

		private bool Boolean(Settings values, string key, bool fallback)
		{
			var value = this.Value(values, key);
			if(value == null)
				return fallback;

			return value.ToLowerInvariant() switch
			{
				"true" or "1" => true,
				"false" or "0" => false,
				_ => throw DefinitionException.Create("Value", values.Source(key), value),
			};
		}

		private string Choice(Settings values, string key, string fallback, string[] choices)
		{
			var value = this.Value(values, key, fallback).ToLowerInvariant();
			if(!choices.Contains(value))
				throw DefinitionException.Create("Value", values.Source(key), value);

			return value;
		}

		private static BigInteger Integer(string value, Diagnostic.Location source, int minimum = 0)
		{
			if(!BigInteger.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var result) || result < minimum)
				throw DefinitionException.Create("Value", source, value);

			return result;
		}

		private BigInteger Count(Settings values, string key, int fallback, int minimum = 0) =>
			values.Get(key) is { } entry ? Integer(this.Value(entry), entry.Source, minimum) : new(fallback);

		private TimeSpan Duration(Settings values, string key, string fallback)
		{
			var value = this.Value(values, key, fallback);

			try
			{
				if(string.IsNullOrEmpty(value) || !TimeSpanUtility.TryParse(value, out var result) || result <= TimeSpan.Zero)
					throw DefinitionException.Create("Value", values.Source(key), value);

				return result;
			}
			catch(Exception exception) when(exception is FormatException or OverflowException or ArgumentException)
			{
				throw DefinitionException.Create("Value", values.Source(key), value, exception);
			}
		}
		#endregion

		#region 嵌套类型
		private sealed record Settings(Dictionary<string, Declaration> Values, IReadOnlyList<Declaration> Servers, Settings Parent, Scope Scope)
		{
			internal Declaration Get(string name) => this.Values.GetValueOrDefault(name);
			internal Diagnostic.Location Source(string name, Diagnostic.Location fallback = default) => this.Get(name)?.Source ?? fallback;
		}
		#endregion
	}
}
