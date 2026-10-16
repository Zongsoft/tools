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
using System.Text;
using System.Text.Json;
using System.Net.Sockets;
using System.Globalization;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text.Json.Serialization;

using Zongsoft.Communication;

#if DEPLOYER
namespace Zongsoft.Tools.Deployer;
#elif PACKAGER
namespace Zongsoft.Tools.Packager;
#elif MIGRATOR
namespace Zongsoft.Tools.Migrator;
#elif CONTAINERIZER
namespace Zongsoft.Tools.Containerizer;
#else
namespace Zongsoft.Tools;
#endif

/// <summary>表示随安装包交付的网络绑定文档。</summary>
internal sealed class BindingDocument
{
	#region 常量定义
	private const int FORMAT_VERSION = 1;
	private const int MAXIMUM_LENGTH = 1024 * 1024;
	private const int MAXIMUM_DEPTH = 32;
	#endregion

	#region 公共常量
	/// <summary>包内绑定文档的保留文件名。</summary>
	public const string FileName = ".bindings";
	/// <summary>绑定文档的最大 UTF-8 字节数。</summary>
	public const int MaximumLength = MAXIMUM_LENGTH;
	#endregion

	#region 静态字段
	private static readonly JsonSerializerOptions _options = new()
	{
		PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
		UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
		MaxDepth = MAXIMUM_DEPTH,
		WriteIndented = false,
		DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
	};
	#endregion

	#region 公共属性
	/// <summary>获取文档的格式版本。</summary>
	[JsonRequired]
	public int Version { get; init; } = FORMAT_VERSION;
	/// <summary>获取应用和代理的监听绑定。</summary>
	[JsonRequired]
	public Binding[] Bindings { get; init; } = [];
	/// <summary>获取代理站点及其绑定关系。</summary>
	[JsonRequired]
	public Site[] Sites { get; init; } = [];
	#endregion

	#region 序列方法
	/// <summary>校验并序列化当前文档，不改变原有集合的顺序。</summary>
	/// <returns>以 UTF-8 编码的 JSON 文档。</returns>
	public byte[] Serialize()
	{
		this.Validate();

		var normalized = new BindingDocument
		{
			Version = this.Version,
			Bindings = this.Bindings.OrderBy(item => item.Id, StringComparer.Ordinal).Select(item => new Binding(
				item.Id,
				item.Owner,
				NormalizeNames(item.Aliases),
				item.Endpoints.Select(NormalizeEndpoint).OrderBy(GetEndpointKey, StringComparer.Ordinal).ToArray())).ToArray(),
			Sites = this.Sites.OrderBy(item => item.Id, StringComparer.Ordinal).Select(item => new Site(
				item.Id,
				item.Name,
				item.Hosts.Order(StringComparer.Ordinal).ToArray(),
				item.Listeners.OrderBy(item => item.Binding, StringComparer.Ordinal).ThenBy(item => item.Endpoint, StringComparer.Ordinal).ToArray(),
				item.Routes.Select(NormalizeRoute).OrderBy(item => item.RequestKind, StringComparer.Ordinal).ThenBy(item => item.Path, StringComparer.Ordinal).ToArray())).ToArray(),
		};

		var content = JsonSerializer.SerializeToUtf8Bytes(normalized, _options);
		if(content.Length > MAXIMUM_LENGTH)
			throw CreateException("/", "length");

		return content;
	}

	/// <summary>读取文档并校验格式、内容和引用关系。</summary>
	/// <param name="content">以 UTF-8 编码的 JSON 文档。</param>
	/// <returns>校验通过的绑定文档。</returns>
	public static BindingDocument Deserialize(ReadOnlySpan<byte> content)
	{
		if(content.Length > MAXIMUM_LENGTH || content.IsEmpty || content.StartsWith(new byte[] { 0xef, 0xbb, 0xbf }))
			throw CreateException("/", "encoding/length");

		try
		{
			_ = new UTF8Encoding(false, true).GetCharCount(content);

			using(var json = JsonDocument.Parse(content.ToArray(), new JsonDocumentOptions { MaxDepth = MAXIMUM_DEPTH }))
			{
				ValidateUniqueProperties(json.RootElement, "");
			}

			var document = JsonSerializer.Deserialize<BindingDocument>(content, _options) ?? throw CreateException("/", "document");
			document.Validate();

			return document;
		}
		catch(JsonException exception)
		{
			throw CreateException(exception.Path ?? "/", "json", exception);
		}
		catch(DecoderFallbackException exception)
		{
			throw CreateException("/", "unicode", exception);
		}
		catch(InvalidOperationException exception)
		{
			throw CreateException("/", "json", exception);
		}
	}
	#endregion

	#region 工厂方法
	/// <summary>根据所属组件和端点生成绑定身份。</summary>
	/// <param name="owner">所属组件。</param>
	/// <param name="aliases">绑定别名。</param>
	/// <param name="endpoints">绑定包含的端点。</param>
	/// <returns>具有内容身份的绑定。</returns>
	public static Binding CreateBinding(string owner, string[] aliases, Endpoint[] endpoints)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(owner);
		ArgumentNullException.ThrowIfNull(aliases);
		ArgumentNullException.ThrowIfNull(endpoints);

		var normalized = endpoints.Select(NormalizeEndpoint).DistinctBy(endpoint => endpoint.Id).OrderBy(GetEndpointKey, StringComparer.Ordinal).ToArray();
		var payload = "{\"owner\":" + EncodeString(owner) + ",\"endpoints\":[" + string.Join(',', normalized.Select(GetEndpointContent)) + "]}";

		return new(GetIdentity(payload), owner, NormalizeNames(aliases), normalized);
	}
	#endregion

	#region 校验方法
	private void Validate()
	{
		if(this.Version != FORMAT_VERSION || this.Bindings == null || this.Sites == null)
			throw CreateException("/", "document");

		var bindings = new Dictionary<string, Binding>(StringComparer.Ordinal);
		ValidateBindings();
		ValidateSites();

		void ValidateBindings()
		{
			var contents = new Dictionary<string, string>(StringComparer.Ordinal);

			foreach(var binding in this.Bindings)
			{
				if(IsInvalidBinding(binding) ||
				   !bindings.TryAdd(binding.Id, binding))
					throw CreateException("/bindings", "binding");

				if(NormalizeNames(binding.Aliases).Length != binding.Aliases.Length)
					throw CreateException("/bindings/" + binding.Id + "/aliases", "duplicate");

				var identities = new HashSet<string>(StringComparer.Ordinal);
				foreach(var endpoint in binding.Endpoints)
				{
					var normalized = NormalizeEndpoint(endpoint);
					var content = GetEndpointContent(normalized);
					if(endpoint.Id != normalized.Id || !identities.Add(endpoint.Id) || contents.TryGetValue(endpoint.Id, out var existing) && existing != content)
						throw CreateException("/bindings/" + binding.Id + "/endpoints", "identity");

					contents[endpoint.Id] = content;
				}

				if(CreateBinding(binding.Owner, [], binding.Endpoints).Id != binding.Id)
					throw CreateException("/bindings/" + binding.Id, "identity");
			}
		}

		void ValidateSites()
		{
			var sites = new HashSet<string>(StringComparer.Ordinal);
			var defaults = new HashSet<string>(StringComparer.Ordinal);

			foreach(var site in this.Sites)
			{
				if(IsInvalidSiteIdentity(site) || !sites.Add(site.Id) || site.Hosts == null || site.Listeners == null || site.Listeners.Length == 0 || site.Routes == null)
					throw CreateException("/sites", "site");

				if(site.Hosts.Any(host => !IsHost(host, true)) || site.Hosts.Distinct(StringComparer.OrdinalIgnoreCase).Count() != site.Hosts.Length)
					throw CreateException("/sites/" + site.Id + "/hosts", "host");

				var listeners = new HashSet<string>(StringComparer.Ordinal);
				foreach(var listener in site.Listeners)
				{
					if(listener == null)
						throw CreateException("/sites/" + site.Id + "/listeners", "null");

					var endpoint = GetEndpoint(bindings, listener.Binding, listener.Endpoint, "nginx");
					var key = listener.Binding + "/" + listener.Endpoint;
					if(endpoint.Scheme is not ("http" or "https") || !listeners.Add(key) || listener.Default && !defaults.Add(key))
						throw CreateException("/sites/" + site.Id + "/listeners", "listener");
				}

				var routes = new HashSet<string>(StringComparer.Ordinal);
				foreach(var route in site.Routes)
				{
					if(route == null || route.RequestKind is not ("http" or "grpc") || route.Path != NormalizeHttpPath(route.Path) ||
					   !routes.Add(route.RequestKind + ":" + route.Path) || route.Target == null ||
					   IsInvalidRouteProtocol(route))
						throw CreateException("/sites/" + site.Id + "/routes", "route");

					Validate(route, bindings);
				}
			}
		}

		static bool IsInvalidBinding(Binding binding)
		{
			return binding == null || string.IsNullOrEmpty(binding.Id) ||
				binding.Owner is not ("application" or "nginx") || binding.Aliases == null ||
				binding.Endpoints == null || binding.Endpoints.Length == 0;
		}

		static bool IsInvalidSiteIdentity(Site site)
		{
			return site == null || string.IsNullOrWhiteSpace(site.Name) || site.Name.Any(char.IsWhiteSpace) ||
				site.Name.Contains('/') || site.Id != "site:" + site.Name.ToLowerInvariant();
		}

		static bool IsInvalidRouteProtocol(SiteRoute route)
		{
			return !NetworkProtocol.TryParse(route.Protocol, out var protocol) || protocol.ToString() != route.Protocol ||
				protocol.HttpVersion == 0 || (route.RequestKind == "grpc") != (protocol.Name == "grpc");
		}
	}

	private static Endpoint GetEndpoint(Dictionary<string, Binding> bindings, string binding, string endpoint, string owner)
	{
		if(binding == null || endpoint == null || !bindings.TryGetValue(binding, out var result) || result.Owner != owner)
			throw CreateException("/sites/target", "binding");

		return result.Endpoints.FirstOrDefault(item => item.Id == endpoint) ?? throw CreateException("/sites/target/endpoint", "reference");
	}

	private static void Validate(SiteRoute route, Dictionary<string, Binding> bindings)
	{
		var target = route.Target;
		if(target.Kind == "binding")
		{
			var endpoint = NormalizeEndpoint(GetEndpoint(bindings, target.Binding, target.Endpoint, "application"));
			if(IsInvalidBindingTarget(endpoint))
				throw CreateException("/sites/routes/target", "binding-target");
		}
		else if(target.Kind == "remote")
		{
			if(target.Binding != null || target.Endpoint != null || (target.Members == null) == (route.Connection == null))
				throw CreateException("/sites/routes/target", "remote-target");

			if(target.Members == null)
			{
				if(target.Balance != null)
					throw CreateException("/sites/routes/target/balance", "single-target");
			}
			else
			{
				if(target.Members.Length < 2 || target.Balance is not ("round-robin" or "least-connections" or "least-requests"))
					throw CreateException("/sites/routes/target", "pool");

				var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
				foreach(var member in target.Members)
				{
					if(member == null || string.IsNullOrWhiteSpace(member.Name) || !names.Add(member.Name) || member.Weight < 1)
						throw CreateException("/sites/routes/target/members", "member");

					Validate(member.Connection, route.Protocol, route.Tls);
				}
			}
		}
		else
			throw CreateException("/sites/routes/target/kind", "kind");

		if(route.Connection != null)
			Validate(route.Connection, route.Protocol, route.Tls);
		if(route.Host != null && !IsAuthority(route.Host))
			throw CreateException("/sites/routes/host", "host");
		if(route.Policy != null)
			Validate(route.Policy);

		bool IsInvalidBindingTarget(Endpoint endpoint)
		{
			return target.Members != null || target.Balance != null || !endpoint.Protocols.Contains(route.Protocol, StringComparer.Ordinal) || route.Connection == null ||
				route.Connection.Scheme != endpoint.Scheme || route.Connection.Port != endpoint.Port || route.Connection.Path != endpoint.Path ||
				endpoint.Path != "/" && route.Path != endpoint.Path && !route.Path.StartsWith(endpoint.Path + "/", StringComparison.Ordinal);
		}
	}

	private static void Validate(Connection connection, string protocol, Tls tls)
	{
		if(connection == null || connection.Scheme is not ("http" or "https") || connection.Port is < 1 or > 65535 ||
		   !IsHost(connection.Host) || connection.Host is "0.0.0.0" or "::" || connection.Path != NormalizeHttpPath(connection.Path))
			throw CreateException("/sites/routes/connection", "connection");

		var normalized = NormalizeEndpoint(new(connection.Scheme, connection.Host, connection.Port, connection.Path, [protocol]));

		if(normalized.Host != connection.Host || normalized.Path != connection.Path)
			throw CreateException("/sites/routes/connection", "canonical-address");

		if(connection.Scheme == "http")
		{
			if(tls != null)
				throw CreateException("/sites/routes/tls", "plaintext");
		}
		else
		{
			if(tls == null || tls.Name != null && (!IsHost(tls.Name) || tls.Name.Contains('%')) || tls.Trust != null && !IsResource(tls.Trust) ||
			   tls.Verify && tls.Name == null && Uri.CheckHostName(connection.Host) != UriHostNameType.Dns)
				throw CreateException("/sites/routes/tls", "tls");
		}
	}

	private static bool IsHost(string host, bool wildcard = false)
	{
		if(string.IsNullOrEmpty(host) || host.Any(character => char.IsWhiteSpace(character) || char.IsControl(character)))
			return false;

		if(wildcard)
		{
			if(host.StartsWith("*.", StringComparison.Ordinal))
				host = host[2..];
			else if(host.StartsWith('.'))
				host = host[1..];
			else if(host.EndsWith(".*", StringComparison.Ordinal))
				host = host[..^2];
		}

		var separator = host.IndexOf('%');
		if(separator >= 0)
			return Uri.CheckHostName(host[..separator]) == UriHostNameType.IPv6 && separator < host.Length - 1 &&
				host[(separator + 1)..].All(character => char.IsAsciiLetterOrDigit(character) || character is '-' or '_' or '.');

		return Uri.CheckHostName(host) is UriHostNameType.Dns or UriHostNameType.IPv4 or UriHostNameType.IPv6;
	}

	private static bool IsAuthority(string authority) => !string.IsNullOrEmpty(authority) &&
		!authority.Any(character => char.IsWhiteSpace(character) || char.IsControl(character) || character is '/' or '\\' or '?' or '#' or '@' or '%') &&
		Uri.TryCreate("http://" + authority, UriKind.Absolute, out var uri) && uri.Port is > 0 and <= 65535 && uri.HostNameType is UriHostNameType.Dns or UriHostNameType.IPv4 or UriHostNameType.IPv6;

	private static bool IsResource(string reference) => !string.IsNullOrWhiteSpace(reference) &&
		reference.StartsWith("file:/", StringComparison.Ordinal) && reference.Length > 6 && reference[6] != '/' && !reference.Any(char.IsControl) && !reference.Contains('\\') && !reference[5..].Split('/').Any(part => part is "." or "..");

	private static bool IsToken(string value) => !string.IsNullOrEmpty(value) && value.All(character => char.IsAsciiLetterOrDigit(character) || "!#$%&'*+-.^_`|~".Contains(character));

	private static void Validate(RoutePolicy policy)
	{
		if(policy.FailureCount < 0 || policy.FailureTimeout < 0 || policy.RetryCount < 0 || policy.Retry == null || policy.Retry.Length == 0 ||
		   policy.Retry.Distinct(StringComparer.Ordinal).Count() != policy.Retry.Length ||
		   policy.Retry.Any(condition => condition is not ("error" or "timeout" or "invalid_header" or "http_500" or "http_502" or "http_503" or "http_504" or "http_403" or "http_404" or "http_429" or "off")) ||
		   policy.Retry.Contains("off") && policy.Retry.Length != 1 || policy.Affinity is not ("none" or "cookie") ||
		   policy.Affinity == "none" && policy.Cookie != null || policy.Cookie != null && !IsToken(policy.Cookie))
			throw CreateException("/sites/routes/policy", "policy");

		if(policy.Health is { } health && (string.IsNullOrEmpty(health.Path) || !health.Path.StartsWith('/') || health.Path.StartsWith("//", StringComparison.Ordinal) || health.Path.Contains('#') || health.Path.Any(character => char.IsWhiteSpace(character) || char.IsControl(character)) || health.Interval <= 0 || health.ConnectTimeout <= 0 || health.SendTimeout <= 0 || health.ReadTimeout <= 0 ||
		   health.FailureCount < 1 || health.RecoveryCount < 1 || health.Status == null || health.Status.Length == 0 || health.Status.Any(status => status is < 100 or > 599) ||
		   health.Headers == null || health.Headers.Keys.Distinct(StringComparer.OrdinalIgnoreCase).Count() != health.Headers.Count || health.Headers.Any(header => !IsToken(header.Key) || header.Value == null || header.Value.Any(char.IsControl))))
			throw CreateException("/sites/routes/policy/health", "health");
	}
	#endregion

	#region 私有方法
	private static SiteRoute NormalizeRoute(SiteRoute route) => new(route.RequestKind, route.Path, new RouteTarget
	{
		Kind = route.Target.Kind,
		Binding = route.Target.Binding,
		Endpoint = route.Target.Endpoint,
		Balance = route.Target.Balance,
		Members = route.Target.Members?.OrderBy(member => member.Name, StringComparer.Ordinal).ToArray(),
	}, route.Protocol, route.Connection, route.Host, route.Tls, route.Policy == null ? null : new RoutePolicy
	{
		FailureCount = route.Policy.FailureCount,
		FailureTimeout = route.Policy.FailureTimeout,
		Retry = route.Policy.Retry.Order(StringComparer.Ordinal).ToArray(),
		RetryCount = route.Policy.RetryCount,
		Affinity = route.Policy.Affinity,
		Cookie = route.Policy.Cookie,
		Health = route.Policy.Health == null ? null : new HealthPolicy
		{
			Path = route.Policy.Health.Path,
			Interval = route.Policy.Health.Interval,
			ConnectTimeout = route.Policy.Health.ConnectTimeout,
			SendTimeout = route.Policy.Health.SendTimeout,
			ReadTimeout = route.Policy.Health.ReadTimeout,
			FailureCount = route.Policy.Health.FailureCount,
			RecoveryCount = route.Policy.Health.RecoveryCount,
			Status = route.Policy.Health.Status.Distinct().Order().ToArray(),
			Headers = route.Policy.Health.Headers.OrderBy(header => header.Key, StringComparer.Ordinal).ToDictionary(header => header.Key, header => header.Value, StringComparer.Ordinal),
		},
	});

	private static string GetEndpointKey(Endpoint endpoint) => Convert.ToHexString(Encoding.UTF8.GetBytes(GetEndpointContent(endpoint)));
	private static string GetIdentity(string content) => Convert.ToHexString(SHA256.HashData(new UTF8Encoding(false, true).GetBytes(content))).ToLowerInvariant();
	private static string GetEndpointContent(Endpoint endpoint) =>
		"{\"scheme\":" + EncodeString(endpoint.Scheme) + ",\"host\":" + EncodeString(endpoint.Host) +
		",\"port\":" + endpoint.Port.ToString(CultureInfo.InvariantCulture) + ",\"path\":" + EncodeString(endpoint.Path) +
		",\"protocols\":[" + string.Join(',', endpoint.Protocols.Select(EncodeString)) + "]}";

	private static string EncodeString(string value)
	{
		if(value == null)
			throw CreateException("/", "null");

		var builder = new StringBuilder(value.Length + 2).Append('"');

		for(var index = 0; index < value.Length; index++)
		{
			var character = value[index];
			if(char.IsSurrogate(character))
			{
				if(!char.IsHighSurrogate(character) || index + 1 == value.Length || !char.IsLowSurrogate(value[index + 1]))
					throw CreateException("/", "unicode");

				builder.Append(character).Append(value[++index]);
				continue;
			}

			switch(character)
			{
				case '"':
					builder.Append("\\\"");
					break;
				case '\\':
					builder.Append("\\\\");
					break;
				case '\b':
					builder.Append("\\b");
					break;
				case '\t':
					builder.Append("\\t");
					break;
				case '\n':
					builder.Append("\\n");
					break;
				case '\f':
					builder.Append("\\f");
					break;
				case '\r':
					builder.Append("\\r");
					break;
				default:
					if(character < 32)
						builder.Append("\\u").Append(((int)character).ToString("x4", CultureInfo.InvariantCulture));
					else
						builder.Append(character);
					break;
			}
		}

		return builder.Append('"').ToString();
	}

	private static Endpoint NormalizeEndpoint(Endpoint endpoint)
	{
		if(endpoint == null || endpoint.Port is < 1 or > 65535 || !IsHost(endpoint.Host) || string.IsNullOrEmpty(endpoint.Path) || !endpoint.Path.StartsWith('/'))
			throw CreateException("/bindings/endpoints", "address");

		var host = endpoint.Host.Contains(':') ? "[" + endpoint.Host.Replace("%", "%25") + "]" : endpoint.Host;
		if(!NetworkAddress.TryParse($"{endpoint.Scheme}://{host}:{endpoint.Port.ToString(CultureInfo.InvariantCulture)}{endpoint.Path}", out var address))
			throw CreateException("/bindings/endpoints", "address");

		var path = address.Scheme is "http" or "https" ? NormalizeHttpPath(address.Path) : address.Path;
		if(address.Scheme is "tcp" or "udp" && path != "/")
			throw CreateException("/bindings/endpoints/path", "path");

		var result = new Endpoint(address.Scheme, address.Host + (address.Zone == null ? "" : "%" + address.Zone), address.Port, path, NormalizeProtocols(endpoint.Protocols));
		_ = result.Transports;
		return new(result.Scheme, result.Host, result.Port, result.Path, result.Protocols, GetIdentity(GetEndpointContent(result)));
	}

	private static string NormalizeHttpPath(string path)
	{
		if(string.IsNullOrEmpty(path) || path[0] != '/' || path.Any(character => !char.IsAsciiLetterOrDigit(character) && character is not ('/' or '-' or '.' or '_' or '~')))
			throw CreateException("/path", "http-path");

		if(path.Length > 1 && path.EndsWith('/'))
			path = path[..^1];

		if(path.Length > 1 && path[1..].Split('/').Any(part => part is "" or "." or ".."))
			throw CreateException("/path", "http-path");

		return path.ToLowerInvariant();
	}

	private static string[] NormalizeNames(string[] names)
	{
		if(names == null || names.Any(string.IsNullOrWhiteSpace))
			throw CreateException("/aliases", "name");

		return names.Select(name => name.Trim().ToLowerInvariant()).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
	}

	private static string[] NormalizeProtocols(string[] values)
	{
		if(values == null)
			throw CreateException("/protocols", "null");

		var protocols = new List<NetworkProtocol>(values.Length);
		foreach(var value in values)
		{
			if(!NetworkProtocol.TryParse(value, out var protocol))
				throw CreateException("/protocols", "protocol");

			protocols.Add(protocol.Carrier == null ? protocol.Name switch
			{
				"grpc" => NetworkProtocol.Parse("grpc+http2"),
				"protobuf" => NetworkProtocol.Parse("protobuf+http"),
				_ => protocol,
			} : protocol);
		}

		return protocols.Where(protocol => protocol.Carrier != "http" || !protocols.Any(other => other.Name == protocol.Name && other.HttpVersion > 0))
			.Select(protocol => protocol.ToString()).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
	}

	private static string[] GetTransports(string scheme, string[] protocols)
	{
		if(scheme is not ("http" or "https" or "tcp" or "udp" or "opc.tcp") || protocols == null)
			throw CreateException("/bindings/endpoints", "scheme/protocols");

		var tcp = scheme is "tcp" or "opc.tcp";
		var udp = scheme == "udp";

		foreach(var text in protocols)
		{
			if(!NetworkProtocol.TryParse(text, out var protocol))
				throw CreateException("/bindings/endpoints/protocols", "protocol");

			// 这两个简写属于绑定策略，Core 的通用解析器不补全承载。
			if(protocol.Carrier == null)
			{
				protocol = protocol.Name switch
				{
					"grpc" => NetworkProtocol.Parse("grpc+http2"),
					"protobuf" => NetworkProtocol.Parse("protobuf+http"),
					_ => protocol,
				};
			}

			if(protocol.Carrier != null && (scheme switch
			{
				"http" => !protocol.IsHttp || protocol.HttpVersion == 3,
				"https" => !protocol.IsHttp,
				"tcp" or "opc.tcp" => protocol.Carrier != "tcp",
				"udp" => protocol.Carrier != "udp",
				_ => true,
			}))
				throw CreateException("/bindings/endpoints/protocols", "scheme-conflict");

			// 未指定承载或 HTTP 版本时不猜测传输，只合并已确定的需求。
			tcp |= protocol.Transport == ProtocolType.Tcp;
			udp |= protocol.Transport == ProtocolType.Udp;
		}

		return (tcp, udp) switch
		{
			(true, true) => ["tcp", "udp"],
			(true, false) => ["tcp"],
			(false, true) => ["udp"],
			_ => [],
		};
	}

	private static void ValidateUniqueProperties(JsonElement element, string path)
	{
		if(element.ValueKind == JsonValueKind.Object)
		{
			var names = new HashSet<string>(StringComparer.Ordinal);

			foreach(var property in element.EnumerateObject())
			{
				var location = path + "/" + property.Name.Replace("~", "~0").Replace("/", "~1");
				if(!names.Add(property.Name) || property.Name is "hostType" or "transports")
					throw CreateException(location, "property");

				ValidateUniqueProperties(property.Value, location);
			}
		}
		else if(element.ValueKind == JsonValueKind.Array)
		{
			var index = 0;
			foreach(var item in element.EnumerateArray())
				ValidateUniqueProperties(item, path + "/" + (index++).ToString(CultureInfo.InvariantCulture));
		}
		else if(element.ValueKind == JsonValueKind.Null)
			throw CreateException(path, "null");
		else if(element.ValueKind == JsonValueKind.String)
			_ = EncodeString(element.GetString());
	}

	private static InvalidDataException CreateException(string path, string rule, Exception inner = null) =>
		new(string.Format(Properties.Resources.BindingsInvalid_Message, path, rule), inner);
	#endregion

	#region 嵌套类型
	/// <summary>表示同一组件的一组替代监听端点。</summary>
	/// <param name="id">身份。</param>
	/// <param name="owner">所属组件。</param>
	/// <param name="aliases">别名集合。</param>
	/// <param name="endpoints">监听端点集合。</param>
	public sealed class Binding(string id, string owner, string[] aliases, Endpoint[] endpoints)
	{
		/// <summary>获取身份。</summary>
		[JsonRequired]
		public string Id { get; init; } = id;
		/// <summary>获取所属组件。</summary>
		[JsonRequired]
		public string Owner { get; init; } = owner;
		/// <summary>获取别名集合。</summary>
		[JsonRequired]
		public string[] Aliases { get; init; } = aliases;
		/// <summary>获取监听端点集合。</summary>
		[JsonRequired]
		public Endpoint[] Endpoints { get; init; } = endpoints;
	}

	/// <summary>表示一个网络监听端点。</summary>
	/// <param name="scheme">地址方案。</param>
	/// <param name="host">主机名或 IP 地址。</param>
	/// <param name="port">端口。</param>
	/// <param name="path">路径。</param>
	/// <param name="protocols">协议能力集合。</param>
	/// <param name="id">读取文档时的端点身份；制作时省略，由规范内容生成。</param>
	public sealed class Endpoint(string scheme, string host, int port, string path, string[] protocols, string id = null)
	{
		/// <summary>获取端点内容身份。</summary>
		[JsonRequired]
		public string Id { get; init; } = id;
		/// <summary>获取地址方案。</summary>
		[JsonRequired]
		public string Scheme { get; init; } = scheme;
		/// <summary>获取主机名或 IP 地址。</summary>
		[JsonRequired]
		public string Host { get; init; } = host;
		/// <summary>获取从主机名或 IP 地址计算的主机种类，该值不参与 JSON 序列化。</summary>
		[JsonIgnore]
		public UriHostNameType HostType => Uri.CheckHostName(this.Host?.Split('%')[0]);
		/// <summary>获取端口。</summary>
		[JsonRequired]
		public int Port { get; init; } = port;
		/// <summary>获取路径。</summary>
		[JsonRequired]
		public string Path { get; init; } = path;
		/// <summary>获取协议能力集合。</summary>
		[JsonRequired]
		public string[] Protocols { get; init; } = protocols;
		/// <summary>获取根据地址方案和协议能力计算的已确定传输集合，该值不参与 JSON 序列化。</summary>
		/// <remarks>无法确定传输的能力不补入默认值；空集合不表示该端点无需网络传输。</remarks>
		[JsonIgnore]
		public string[] Transports => GetTransports(this.Scheme, this.Protocols);
	}

	/// <summary>表示代理站点及其监听和路由关系。</summary>
	/// <param name="id">身份。</param>
	/// <param name="name">名称。</param>
	/// <param name="hosts">站点主机名集合。</param>
	/// <param name="listeners">站点监听集合。</param>
	/// <param name="routes">请求路由集合。</param>
	public sealed class Site(string id, string name, string[] hosts, SiteListener[] listeners, SiteRoute[] routes)
	{
		/// <summary>获取身份。</summary>
		[JsonRequired]
		public string Id { get; init; } = id;
		/// <summary>获取名称。</summary>
		[JsonRequired]
		public string Name { get; init; } = name;
		/// <summary>获取站点主机名集合。</summary>
		[JsonRequired]
		public string[] Hosts { get; init; } = hosts;
		/// <summary>获取站点监听集合。</summary>
		[JsonRequired]
		public SiteListener[] Listeners { get; init; } = listeners;
		/// <summary>获取请求路由集合。</summary>
		[JsonRequired]
		public SiteRoute[] Routes { get; init; } = routes;
	}

	/// <summary>表示站点使用的代理监听端点。</summary>
	/// <param name="binding">引用的绑定身份。</param>
	/// <param name="endpoint">引用的监听端点身份。</param>
	/// <param name="default">是否为默认站点。</param>
	public sealed class SiteListener(string binding, string endpoint, bool @default)
	{
		/// <summary>获取引用的绑定身份。</summary>
		[JsonRequired]
		public string Binding { get; init; } = binding;
		/// <summary>获取引用的监听端点身份。</summary>
		[JsonRequired]
		public string Endpoint { get; init; } = endpoint;
		/// <summary>获取是否为默认站点。</summary>
		[JsonRequired]
		public bool Default { get; init; } = @default;
	}

	/// <summary>表示站点的请求路由及上游连接。</summary>
	/// <param name="requestKind">请求类别。</param>
	/// <param name="path">路径。</param>
	/// <param name="target">引用的本包端点或远程目标。</param>
	/// <param name="protocol">选定的协议组合。</param>
	/// <param name="connection">上游连接地址。</param>
	/// <param name="host">主机名或 IP 地址。</param>
	/// <param name="tls">TLS 验证设置。</param>
	/// <param name="policy">上游连接策略。</param>
	public sealed class SiteRoute(string requestKind, string path, RouteTarget target, string protocol, Connection connection = null, string host = null, Tls tls = null, RoutePolicy policy = null)
	{
		/// <summary>获取请求类别。</summary>
		[JsonRequired]
		public string RequestKind { get; init; } = requestKind;
		/// <summary>获取路径。</summary>
		[JsonRequired]
		public string Path { get; init; } = path;
		/// <summary>获取本包端点引用或远程目标。</summary>
		[JsonRequired]
		public RouteTarget Target { get; init; } = target;
		/// <summary>获取选定的协议组合。</summary>
		[JsonRequired]
		public string Protocol { get; init; } = protocol;
		/// <summary>获取上游连接地址。</summary>
		public Connection Connection { get; init; } = connection;
		/// <summary>获取主机名或 IP 地址。</summary>
		public string Host { get; init; } = host;
		/// <summary>获取 TLS 验证设置。</summary>
		public Tls Tls { get; init; } = tls;
		/// <summary>获取上游连接策略。</summary>
		public RoutePolicy Policy { get; init; } = policy;
	}

	/// <summary>表示本包端点或显式远程目标。</summary>
	public sealed class RouteTarget
	{
		/// <summary>获取目标类别：binding 或 remote。</summary>
		[JsonRequired]
		public string Kind { get; init; }
		/// <summary>获取本包绑定身份。</summary>
		public string Binding { get; init; }
		/// <summary>获取绑定内的端点身份。</summary>
		public string Endpoint { get; init; }
		/// <summary>获取显式远程池的均衡策略。</summary>
		public string Balance { get; init; }
		/// <summary>获取显式远程池成员；单目标时省略。</summary>
		public RemoteMember[] Members { get; init; }
	}

	/// <summary>表示显式远程池中的一个成员。</summary>
	public sealed class RemoteMember
	{
		/// <summary>获取成员名称。</summary>
		[JsonRequired]
		public string Name { get; init; }
		/// <summary>获取正整数权重。</summary>
		[JsonRequired]
		public long Weight { get; init; }
		/// <summary>获取成员连接地址。</summary>
		[JsonRequired]
		public Connection Connection { get; init; }
	}

	/// <summary>表示已完成默认值求解的上游连接策略，时间单位为毫秒。</summary>
	public sealed class RoutePolicy
	{
		/// <summary>获取允许的失败次数；零表示禁用计数。</summary>
		[JsonRequired]
		public long FailureCount { get; init; }
		/// <summary>获取失败观察窗口。</summary>
		[JsonRequired]
		public decimal FailureTimeout { get; init; }
		/// <summary>获取重试条件。</summary>
		[JsonRequired]
		public string[] Retry { get; init; }
		/// <summary>获取重试次数。</summary>
		[JsonRequired]
		public long RetryCount { get; init; }
		/// <summary>获取会话亲和类别：none 或 cookie。</summary>
		[JsonRequired]
		public string Affinity { get; init; }
		/// <summary>获取显式亲和 Cookie 名称。</summary>
		public string Cookie { get; init; }
		/// <summary>获取主动健康检查；省略表示不启用。</summary>
		public HealthPolicy Health { get; init; }
	}

	/// <summary>表示显式主动健康检查，时间单位为毫秒。</summary>
	public sealed class HealthPolicy
	{
		#region 公共属性
		/// <summary>获取请求路径。</summary>
		[JsonRequired]
		public string Path { get; init; }
		/// <summary>获取检查间隔。</summary>
		[JsonRequired]
		public decimal Interval { get; init; }
		/// <summary>获取连接超时。</summary>
		[JsonRequired]
		public decimal ConnectTimeout { get; init; }
		/// <summary>获取发送超时。</summary>
		[JsonRequired]
		public decimal SendTimeout { get; init; }
		/// <summary>获取读取超时。</summary>
		[JsonRequired]
		public decimal ReadTimeout { get; init; }
		/// <summary>获取判定失败所需的次数。</summary>
		[JsonRequired]
		public long FailureCount { get; init; }
		/// <summary>获取判定恢复所需的次数。</summary>
		[JsonRequired]
		public long RecoveryCount { get; init; }
		/// <summary>获取成功状态码集合。</summary>
		[JsonRequired]
		public int[] Status { get; init; }
		/// <summary>获取显式请求头。</summary>
		[JsonRequired]
		public Dictionary<string, string> Headers { get; init; }
		#endregion
	}

	/// <summary>表示代理使用的上游连接地址。</summary>
	/// <param name="scheme">地址方案。</param>
	/// <param name="host">主机名或 IP 地址。</param>
	/// <param name="port">端口。</param>
	/// <param name="path">路径。</param>
	public sealed class Connection(string scheme, string host, int port, string path)
	{
		/// <summary>获取地址方案。</summary>
		[JsonRequired]
		public string Scheme { get; init; } = scheme;
		/// <summary>获取主机名或 IP 地址。</summary>
		[JsonRequired]
		public string Host { get; init; } = host;
		/// <summary>获取端口。</summary>
		[JsonRequired]
		public int Port { get; init; } = port;
		/// <summary>获取路径。</summary>
		[JsonRequired]
		public string Path { get; init; } = path;
	}

	/// <summary>表示代理连接的 TLS 验证设置。</summary>
	/// <param name="verify">是否验证上游证书。</param>
	/// <param name="name">名称。</param>
	/// <param name="trust">信任根资源引用。</param>
	public sealed class Tls(bool verify, string name = null, string trust = null)
	{
		/// <summary>获取是否验证上游证书。</summary>
		[JsonRequired]
		public bool Verify { get; init; } = verify;
		/// <summary>获取名称。</summary>
		public string Name { get; init; } = name;
		/// <summary>获取信任根资源引用。</summary>
		public string Trust { get; init; } = trust;
	}
	#endregion
}
