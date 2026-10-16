using System;
using System.Linq;
using System.Text.RegularExpressions;

using Zongsoft.Tools.Packager.Web;

using Xunit;

namespace Zongsoft.Tools.Packager.Tests.Web;

public class WebRoutingTest
{
	[Fact]
	public void AutomaticServicesSeparateKindsAndPreserveLongestPaths()
	{
		using var files = new MigrationTestDirectory();
		var web = Application("web", new BindingDocument.Endpoint("http", "0.0.0.0", 8069, "/", ["http1"]));
		var metrics = Application("metrics", new BindingDocument.Endpoint("http", "0.0.0.0", 9464, "/metrics", ["http1"]));
		var telemetry = Application("telemetry", new BindingDocument.Endpoint("http", "0.0.0.0", 4317, "/opentelemetry", ["grpc+http2"]));
		var result = Configure(files, "[api]\nbind!public=http://*\n", [web, metrics, telemetry]);
		var site = Assert.Single(result.Bindings.Sites);
		var text = Configuration(result);

		Assert.Equal(web.Id, Route(site, "http", "/").Target.Binding);
		Assert.Equal(metrics.Id, Route(site, "http", "/metrics").Target.Binding);
		Assert.Equal(web.Id, Route(site, "http", "/opentelemetry").Target.Binding);
		Assert.Equal(telemetry.Id, Route(site, "grpc", "/opentelemetry").Target.Binding);
		Assert.DoesNotContain(site.Routes, route => route.RequestKind == "grpc" && route.Path != "/opentelemetry");

		Assert.Contains("proxy_pass http://127.0.0.1:8069;", text);
		Assert.Contains("proxy_pass http://127.0.0.1:9464;", text);
		Assert.Contains("grpc_pass grpc://127.0.0.1:4317;", text);
		Assert.Contains("http2 on;", text);
		Assert.Contains("add_header grpc-status 12 always;", text);
		Assert.Contains("return 404;", text);
		Assert.Contains("error_page 418 = $binding_", text);
		Assert.DoesNotContain("proxy_pass http://127.0.0.1:4317;", text);
		Assert.Equal(result.Bindings.Serialize(), BindingDocument.Deserialize(result.Bindings.Serialize()).Serialize());
	}

	[Theory]
	[InlineData("http1")]
	[InlineData("grpc+http2")]
	public void DistinctBindingsAtTheSameKindAndPathFail(string protocol)
	{
		using var files = new MigrationTestDirectory();
		var first = Application("first", new BindingDocument.Endpoint("http", "127.0.0.1", 8001, "/api", [protocol]));
		var second = Application("second", new BindingDocument.Endpoint("http", "127.0.0.1", 8002, "/api", [protocol]));

		Assert.Equal("Conflict", Assert.Throws<DefinitionException>(() => Configure(files, "[api]\nbind!public=http://*", [first, second])).Diagnostic.Code);
	}

	[Fact]
	public void SamePathForDifferentKindsIsValid()
	{
		using var files = new MigrationTestDirectory();
		var http = Application("web", new BindingDocument.Endpoint("http", "127.0.0.1", 8001, "/", ["http1"]));
		var grpc = Application("rpc", new BindingDocument.Endpoint("http", "127.0.0.1", 8002, "/", ["grpc+http2"]));
		var site = Assert.Single(Configure(files, "[api]\nbind!public=http://*", [http, grpc]).Bindings.Sites);

		Assert.Equal(2, site.Routes.Length);
		Assert.Equal(http.Id, Route(site, "http", "/").Target.Binding);
		Assert.Equal(grpc.Id, Route(site, "grpc", "/").Target.Binding);
	}

	[Theory]
	[InlineData("")]
	[InlineData(" ")]
	[InlineData("${blank}")]
	public void AliasRestrictsSiteAndEmptyValuesInherit(string empty)
	{
		using var files = new MigrationTestDirectory();
		var first = Application("first", new BindingDocument.Endpoint("http", "127.0.0.1", 8001, "/", ["http1"]));
		var second = Application("second", new BindingDocument.Endpoint("http", "127.0.0.1", 8002, "/", ["http1"]));
		var profile = "server=FIRST\n[api]\nbind!public=http://*\nserver=" + empty + "\n[api child]\npath=/child\nserver=" + empty;
		var result = Configure(files, profile, [first, second], new() { ["blank"] = " \t " });
		var site = Assert.Single(result.Bindings.Sites);

		Assert.Equal(["/", "/child"], site.Routes.Select(route => route.Path).Order(StringComparer.Ordinal));
		Assert.All(site.Routes, route => Assert.Equal(first.Id, route.Target.Binding));
		Assert.DoesNotContain("8002", Configuration(result));
	}

	[Fact]
	public void ChildAliasOverridesOnlyItsPath()
	{
		using var files = new MigrationTestDirectory();
		var first = Application("first", new BindingDocument.Endpoint("http", "127.0.0.1", 8001, "/", ["http1"]));
		var second = Application("second", new BindingDocument.Endpoint("http", "127.0.0.1", 8002, "/", ["http1"]));
		var result = Configure(files, "server=first\n[api]\nbind!public=http://*\n[api child]\npath=/child\nserver=second", [first, second]);
		var site = Assert.Single(result.Bindings.Sites);

		Assert.Equal(first.Id, Route(site, "http", "/").Target.Binding);
		Assert.Equal(second.Id, Route(site, "http", "/child").Target.Binding);
		Assert.Equal(2, site.Routes.Length);
		Assert.True(Configuration(result).IndexOf("~*^http:/child", StringComparison.Ordinal) < Configuration(result).IndexOf("\"~*^http:/\"", StringComparison.Ordinal));
	}

	[Theory]
	[InlineData("/Metrics", true)]
	[InlineData("/mEtRiCs/", true)]
	[InlineData("/metrics/child?x=1", true)]
	[InlineData("/metrics?q=/other", true)]
	[InlineData("/metrics-extra", false)]
	[InlineData("/metricsx/", false)]
	[InlineData("/other?path=/metrics", false)]
	[InlineData("/%6detrics", false)]
	public void OriginalRequestMapUsesCaseInsensitiveSegmentBoundaries(string uri, bool matches)
	{
		using var files = new MigrationTestDirectory();
		var result = Configure(files, "[api]\nbind!public=http://*\n[api route]\npath=/Metrics/\nserver=http://backend:8001/metrics", []);
		var text = Configuration(result);
		var pattern = Regex.Match(text, "\"~\\*\\^(http:/metrics[^\"]+)\"").Groups[1].Value;

		Assert.NotEmpty(pattern);
		Assert.Equal(matches, Regex.IsMatch("http:" + uri, "^" + pattern, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant));
		Assert.Contains(":$request_uri\"", text);
		Assert.Contains("proxy_pass http://backend:8001;", text);
		Assert.DoesNotContain("proxy_pass http://backend:8001/metrics", text);
		Assert.DoesNotContain("rewrite ", text);
	}

	[Theory]
	[InlineData("/a//b")]
	[InlineData("/a/../b")]
	[InlineData("/a/./b")]
	[InlineData("/a%2fb")]
	[InlineData("/a?query")]
	[InlineData("/a#fragment")]
	public void UnrepresentableScopesFail(string path)
	{
		using var files = new MigrationTestDirectory();

		Assert.Equal("Value", Assert.Throws<DefinitionException>(() => Configure(files, "[api]\nbind!public=http://*\nserver=http://backend\n[api route]\npath=" + path, [])).Diagnostic.Code);
	}

	[Fact]
	public void AlternativeEndpointsChooseOneStableConnection()
	{
		using var files = new MigrationTestDirectory();
		var endpoints = new BindingDocument.Endpoint[]
		{
			new BindingDocument.Endpoint("http", "backend.example", 8001, "/", ["http1"]),
			new BindingDocument.Endpoint("http", "::", 8001, "/", ["http1"]),
			new BindingDocument.Endpoint("http", "2001:db8::1", 8001, "/", ["http1"]),
			new BindingDocument.Endpoint("http", "192.0.2.2", 8001, "/", ["http1"]),
			new BindingDocument.Endpoint("http", "192.0.2.1", 8001, "/", ["http1"]),
		};
		var first = Configure(files, "[api]\nbind!public=http://*", [Application("web", endpoints)]);
		var second = Configure(files, "[api]\nbind!public=http://*", [Application("web", endpoints.Reverse().ToArray())]);
		var route = Assert.Single(Assert.Single(first.Bindings.Sites).Routes);

		Assert.Equal("192.0.2.1", route.Connection.Host);
		Assert.Null(route.Target.Members);
		Assert.Equal(Configuration(first), Configuration(second));
		Assert.DoesNotContain("upstream hoster_", Configuration(first));
		Assert.Single(first.Bindings.Bindings, binding => binding.Owner == "application");
		Assert.Equal(5, first.Bindings.Bindings.Single(binding => binding.Owner == "application").Endpoints.Length);
	}

	[Theory]
	[InlineData("0.0.0.0", "127.0.0.1")]
	[InlineData("::", "::1")]
	public void WildcardEndpointsRetainListenIdentity(string listen, string connection)
	{
		using var files = new MigrationTestDirectory();
		var application = Application("web", new BindingDocument.Endpoint("http", listen, 8001, "/", ["http1"]));
		var result = Configure(files, "[api]\nbind!public=http://*", [application]);
		var route = Assert.Single(Assert.Single(result.Bindings.Sites).Routes);
		var stored = Assert.Single(result.Bindings.Bindings, binding => binding.Id == route.Target.Binding);

		Assert.Equal(listen, Assert.Single(stored.Endpoints).Host);
		Assert.Equal(application.Endpoints[0].Id, route.Target.Endpoint);
		Assert.Equal(connection, route.Connection.Host);
		Assert.Contains("{{zongsoft:binding:" + stored.Id + ":" + route.Target.Endpoint + "}}", Template(result));
		Assert.DoesNotContain("upstream hoster_", Configuration(result));
	}

	[Theory]
	[InlineData("http2", "http2", "2")]
	[InlineData("http1,http3", "http1", "1.1")]
	[InlineData("http1,http2", "http2", "2")]
	[InlineData("http1,http2,protobuf+http1", "http1", "1.1")]
	public void DeclaredHttpVersionsSelectOnlyImplementableCapabilities(string protocols, string selected, string version)
	{
		using var files = new MigrationTestDirectory();
		var result = Configure(files, "[api]\nbind!public=http://*", [Application("web", new BindingDocument.Endpoint("https", "backend.example", 8001, "/", protocols.Split(',')))]);
		var route = Assert.Single(Assert.Single(result.Bindings.Sites).Routes);

		Assert.Equal(selected, route.Protocol);
		Assert.Contains("proxy_http_version " + version + ";", Configuration(result));
		Assert.DoesNotContain("grpc_pass", Configuration(result));
	}

	[Theory]
	[InlineData("http3")]
	[InlineData("grpc+http3")]
	[InlineData("http1,protobuf+http2")]
	[InlineData("")]
	public void UnsupportedVersionsFail(string protocols)
	{
		using var files = new MigrationTestDirectory();
		var application = Application("web", new BindingDocument.Endpoint("https", "backend.example", 8001, "/", protocols.Length == 0 ? [] : protocols.Split(',')));

		Assert.Equal("Capability", Assert.Throws<DefinitionException>(() => Configure(files, "[api]\nbind!public=http://*", [application])).Diagnostic.Code);
	}

	[Fact]
	public void UsableAlternativeWinsOverPreferredUnsupportedAddress()
	{
		using var files = new MigrationTestDirectory();
		var application = Application("web",
			new BindingDocument.Endpoint("https", "192.0.2.1", 8001, "/", ["http3"]),
			new BindingDocument.Endpoint("https", "backend.example", 8001, "/", ["http2"]));
		var result = Configure(files, "[api]\nbind!public=http://*", [application]);
		var route = Assert.Single(Assert.Single(result.Bindings.Sites).Routes);

		Assert.Equal("backend.example", route.Connection.Host);
		Assert.Equal("http2", route.Protocol);
		Assert.Equal(application.Endpoints.Single(endpoint => endpoint.Host == "backend.example").Id, route.Target.Endpoint);
	}

	[Fact]
	public void TlsUsesSystemTrustAndIndependentOriginalHost()
	{
		using var files = new MigrationTestDirectory();
		var result = Configure(files, "[api]\nbind!public=http://*\nforwarded=false", [Application("web", new BindingDocument.Endpoint("https", "backend.example", 8443, "/", ["http1"]))]);
		var route = Assert.Single(Assert.Single(result.Bindings.Sites).Routes);
		var text = Configuration(result);

		Assert.True(route.Tls.Verify);
		Assert.Equal("backend.example", route.Tls.Name);
		Assert.Null(route.Tls.Trust);
		Assert.Null(route.Host);
		Assert.Contains("proxy_ssl_verify on;", text);
		Assert.Contains("proxy_ssl_name backend.example;", text);
		Assert.Contains("proxy_ssl_trusted_certificate /etc/ssl/certs/ca-certificates.crt;", text);
		Assert.Contains("proxy_set_header Host $http_host;", text);
		Assert.DoesNotContain("map $http_host", text);
		Assert.DoesNotContain("X-Forwarded", text);
	}

	[Theory]
	[InlineData("0.0.0.0")]
	[InlineData("::")]
	public void WildcardTlsRequiresExplicitName(string host)
	{
		using var files = new MigrationTestDirectory();
		var application = Application("web", new BindingDocument.Endpoint("https", host, 8443, "/", ["http1"]));

		Assert.Equal("Required", Assert.Throws<DefinitionException>(() => Configure(files, "[api]\nbind!public=http://*", [application])).Diagnostic.Code);
	}

	[Fact]
	public void ExplicitTlsNameAndHostRemainIndependent()
	{
		using var files = new MigrationTestDirectory();
		var result = Configure(files, "[api]\nbind!public=http://*\nserver-tls-name=certificate.example\nserver-tls-trust=file:/trust/ca.pem\nheader!Host=business.example",
			[Application("web", new BindingDocument.Endpoint("https", "0.0.0.0", 8443, "/", ["http1"]))]);
		var route = Assert.Single(Assert.Single(result.Bindings.Sites).Routes);
		var text = Configuration(result);

		Assert.Equal("127.0.0.1", route.Connection.Host);
		Assert.Equal("certificate.example", route.Tls.Name);
		Assert.Equal("file:/trust/ca.pem", route.Tls.Trust);
		Assert.Equal("business.example", route.Host);
		Assert.Contains("proxy_ssl_name certificate.example;", text);
		Assert.Contains("proxy_set_header Host business.example;", text);
		Assert.Contains("proxy_ssl_trusted_certificate /trust/ca.pem;", text);
		Assert.DoesNotContain("proxy_set_header Host certificate.example;", text);
	}

	[Fact]
	public void QuicAndTcpSharePortWithDistinctTransportBindings()
	{
		using var files = new MigrationTestDirectory();
		var result = Configure(files, "[api]\nnginx:listen!443=ssl\nnginx:listen!0.0.0.0:443=quic reuseport\nnginx:ssl_certificate=/tls/site.pem\nnginx:ssl_certificate_key=/tls/site.key\nserver=http://backend", []);
		var site = Assert.Single(result.Bindings.Sites);
		var endpoints = result.Bindings.Bindings.Where(binding => binding.Owner == "nginx").SelectMany(binding => binding.Endpoints).ToArray();

		Assert.Equal(2, site.Listeners.Length);
		Assert.Equal(2, site.Listeners.Select(listener => listener.Binding).Distinct().Count());
		Assert.All(endpoints, endpoint => Assert.Equal(443, endpoint.Port));
		Assert.Equal(["http1"], Assert.Single(endpoints, endpoint => endpoint.Transports.Contains("tcp")).Protocols);
		Assert.Equal(["http3"], Assert.Single(endpoints, endpoint => endpoint.Transports.Contains("udp")).Protocols);
		Assert.Contains("listen 443 ssl;", Configuration(result));
		Assert.Contains("listen 0.0.0.0:443 quic reuseport;", Configuration(result));
	}

	[Theory]
	[InlineData("http1", "", null)]
	[InlineData("grpc+http2", "", null)]
	[InlineData("http1", "header!Host=business.example:9443", "business.example:9443")]
	[InlineData("grpc+http2", "header!Host=business.example:9443", "business.example:9443")]
	[InlineData("http1", "nginx:proxy_set_header!Host='business.example:9443'", "'business.example:9443'")]
	public void QuicAuthorityFallbackPreservesExplicitHost(string protocol, string settings, string expected)
	{
		using var files = new MigrationTestDirectory();
		var application = Application("backend", new BindingDocument.Endpoint("http", "0.0.0.0", 8069, "/", [protocol]));
		var result = Configure(files, "[api]\nnginx:listen!443=ssl\nnginx:listen!0.0.0.0:443=quic reuseport\nnginx:ssl_certificate=/tls/site.pem\nnginx:ssl_certificate_key=/tls/site.key\n" + settings, [application]);
		var text = Configuration(result);
		var map = Assert.Single(Regex.Matches(text, @"map \$http_host (?<authority>\$binding_[a-f0-9]+_authority) \{(?<body>.*?)\}", RegexOptions.Singleline).Cast<Match>());
		var authority = map.Groups["authority"].Value;
		var body = map.Groups["body"].Value;
		var prefix = protocol.StartsWith("grpc", StringComparison.Ordinal) ? "grpc" : "proxy";
		var route = Assert.Single(Assert.Single(result.Bindings.Sites).Routes);

		Assert.Contains("default $http_host;", body);
		Assert.Contains("\"\" $host$is_request_port$request_port;", body);
		Assert.Contains(prefix + "_set_header Host " + (expected ?? authority) + ";", text);
		Assert.Contains(prefix + "_set_header Host " + (expected ?? authority) + ";", Template(result));
		Assert.Equal(expected?.Trim('\''), route.Host);
		Assert.Equal(protocol, route.Protocol);

		if(expected != null)
			Assert.DoesNotContain(prefix + "_set_header Host " + authority + ";", text);
		else
			Assert.DoesNotContain(prefix + "_set_header Host $http_host;", text);
	}

	[Theory]
	[InlineData(false)]
	[InlineData(true)]
	public void GrpcRoutesAndMissingTargetsEnforceProtocolAndMethod(bool hasGrpcRoute)
	{
		using var files = new MigrationTestDirectory();
		var http = Application("http", new BindingDocument.Endpoint("http", "0.0.0.0", 8069, "/", ["http1"]));
		var grpc = Application("grpc", new BindingDocument.Endpoint("http", "0.0.0.0", 4317, "/", ["grpc+http2"]));
		var text = Configuration(Configure(files, "[api]\nbind!public=http://*", hasGrpcRoute ? [http, grpc] : [http]));
		var locations = Regex.Matches(text, @"^\tlocation (?<name>@[^\s]+) \{\r\n(?<body>.*?)^\t\}\r\n", RegexOptions.Multiline | RegexOptions.Singleline).Cast<Match>().ToArray();
		var missing = Assert.Single(locations, location => location.Groups["name"].Value.EndsWith("_grpc_missing", StringComparison.Ordinal)).Groups["body"].Value;

		AssertGrpcChecks(missing, "return 200;");
		Assert.Contains("default_type application/grpc;", missing);
		Assert.Contains("add_header grpc-status 12 always;", missing);
		Assert.Contains("add_header grpc-message Unimplemented always;", missing);
		var ordinary = Assert.Single(locations, location => location.Groups["body"].Value.Contains("proxy_pass", StringComparison.Ordinal)).Groups["body"].Value;
		Assert.DoesNotContain("$server_protocol", ordinary);
		Assert.DoesNotContain("$request_method", ordinary);

		if(hasGrpcRoute)
		{
			var route = Assert.Single(locations, location => location.Groups["body"].Value.Contains("grpc_pass", StringComparison.Ordinal)).Groups["body"].Value;
			AssertGrpcChecks(route, "grpc_pass grpc://127.0.0.1:4317;");
		}
		else
			Assert.DoesNotContain("grpc_pass", text);
	}

	[Fact]
	public void LastDeclarationOwnerWinsAfterInterleavedImport()
	{
		using var files = new MigrationTestDirectory();
		files.Write("shared.profile", "[api]\nserver!imported=http://imported\nserver!second=http://imported-second");
		var result = Configure(files, "[api]\nbind!public=http://*\nserver!before=http://before\n#@import shared.profile\n[api]\nserver!after=http://after", []);
		var route = Assert.Single(Assert.Single(result.Bindings.Sites).Routes);

		Assert.Equal(["before", "after"], route.Target.Members.Select(member => member.Name));
		Assert.Contains("server before:80", Configuration(result));
		Assert.Contains("server after:80", Configuration(result));
		Assert.DoesNotContain("imported", Configuration(result));
	}

	[Theory]
	[InlineData("web")]
	[InlineData("http://backend/metrics")]
	public void DisjointExplicitScopesFail(string server)
	{
		using var files = new MigrationTestDirectory();
		var application = Application("web", new BindingDocument.Endpoint("http", "127.0.0.1", 8001, "/metrics", ["http1"]));

		Assert.Equal("Conflict", Assert.Throws<DefinitionException>(() => Configure(files, "[api]\nbind!public=http://*\n[api route]\npath=/other\nserver=" + server, [application])).Diagnostic.Code);
	}

	[Fact]
	public void NativeLiteralHostIsRecorded()
	{
		using var files = new MigrationTestDirectory();
		var result = Configure(files, "[api]\nbind!public=http://*\nserver=https://backend.example\nnginx:proxy_set_header!Host='business.example'", []);
		var route = Assert.Single(Assert.Single(result.Bindings.Sites).Routes);

		Assert.Equal("business.example", route.Host);
		Assert.Equal("backend.example", route.Tls.Name);
		Assert.Contains("proxy_set_header Host 'business.example';", Configuration(result));
	}

	[Theory]
	[InlineData("$host")]
	[InlineData("")]
	public void DynamicOrEmptyNativeHostFails(string host)
	{
		using var files = new MigrationTestDirectory();

		Assert.Equal("Capability", Assert.Throws<DefinitionException>(() => Configure(files, "[api]\nbind!public=http://*\nserver=http://backend\nnginx:proxy_set_header!Host=" + host, [])).Diagnostic.Code);
	}

	[Theory]
	[InlineData("application/grpc", true)]
	[InlineData("Application/Grpc+proto", true)]
	[InlineData("application/grpc+json; charset=utf-8", true)]
	[InlineData("application/grpc ; charset=utf-8", true)]
	[InlineData("application/grpc-web", false)]
	[InlineData("application/grpc-web+proto", false)]
	[InlineData("application/grpcjunk", false)]
	[InlineData("text/plain", false)]
	[InlineData("", false)]
	public void ContentTypeMapSeparatesNativeGrpcFromGrpcWeb(string contentType, bool grpc)
	{
		using var files = new MigrationTestDirectory();
		var text = Configuration(Configure(files, "[api]\nbind!public=http://*", []));
		var pattern = Regex.Match(text, "\"~\\*(\\^application/grpc[^\"]+)\"").Groups[1].Value;

		Assert.NotEmpty(pattern);
		Assert.Equal(grpc, Regex.IsMatch(contentType, pattern.Replace("[:space:]", "\\s"), RegexOptions.IgnoreCase | RegexOptions.CultureInvariant));
		Assert.Contains("map $http_content_type", text);
		Assert.Contains("\"~^grpc:\" @binding_", text);
		Assert.DoesNotContain("proxy_pass", text);
	}

	[Fact]
	public void ExplicitGrpcPoolPreservesWeightsTlsRetryAndRequestAuthority()
	{
		using var files = new MigrationTestDirectory();
		var result = Configure(files, "[api]\nbind!public=http://*\nserver!one=grpcs://one.example:7443/telemetry weight=2\nserver!two=grpcs://two.example:7443/telemetry weight=5\nserver-tls-name=certificate.example\nheader!Host=business.example\nserver-retry=timeout\nserver-retry-count=2\nserver-balance=least-connections", []);
		var route = Assert.Single(Assert.Single(result.Bindings.Sites).Routes);
		var text = Configuration(result);

		Assert.Equal("grpc", route.RequestKind);
		Assert.Equal("/telemetry", route.Path);
		Assert.Equal("grpc+http2", route.Protocol);
		Assert.Equal("remote", route.Target.Kind);
		Assert.Null(route.Target.Binding);
		Assert.Null(route.Connection);
		Assert.Equal([2L, 5L], route.Target.Members.Select(member => member.Weight));
		Assert.All(route.Target.Members, member => Assert.Equal("https", member.Connection.Scheme));
		Assert.All(route.Target.Members, member => Assert.Equal("/telemetry", member.Connection.Path));
		Assert.Equal("least-connections", route.Target.Balance);
		Assert.Equal(["timeout"], route.Policy.Retry);
		Assert.Equal(2, route.Policy.RetryCount);
		Assert.Equal("certificate.example", route.Tls.Name);
		Assert.Equal("business.example", route.Host);
		Assert.Contains("grpc_pass grpcs://hoster_", text);
		Assert.Contains("grpc_next_upstream_tries 3;", text);
		Assert.Contains("grpc_set_header Host business.example;", text);
		Assert.Contains("grpc_ssl_verify on;", text);
		Assert.DoesNotContain("proxy_pass", text);
		Assert.DoesNotContain("{{zongsoft:binding:", Template(result));
	}

	[Theory]
	[InlineData("server-health=/health")]
	[InlineData("server-affinity=cookie")]
	[InlineData("nginx:http2=off")]
	public void UnsupportedGrpcMappingsFailExplicitly(string setting)
	{
		using var files = new MigrationTestDirectory();

		Assert.Equal("Capability", Assert.Throws<DefinitionException>(() => Configure(files, "[api]\nbind!public=http://*\nserver=grpc://backend:4317\n" + setting, [])).Diagnostic.Code);
	}

	[Theory]
	[InlineData("http1,http2", "http1")]
	[InlineData("http2", null)]
	public void WebsocketRequiresAnExplicitHttpOneCapability(string protocols, string expected)
	{
		using var files = new MigrationTestDirectory();
		var application = Application("web", new BindingDocument.Endpoint("http", "127.0.0.1", 8001, "/", protocols.Split(',')));

		if(expected == null)
		{
			Assert.Equal("Capability", Assert.Throws<DefinitionException>(() => Configure(files, "[api]\nbind!public=http://*\nwebsocket=true", [application])).Diagnostic.Code);
			return;
		}

		var result = Configure(files, "[api]\nbind!public=http://*\nwebsocket=true", [application]);
		Assert.Equal(expected, Assert.Single(Assert.Single(result.Bindings.Sites).Routes).Protocol);
		Assert.Contains("proxy_http_version 1.1;", Configuration(result));
		Assert.Contains("proxy_set_header Upgrade $http_upgrade;", Configuration(result));
	}

	private static void AssertGrpcChecks(string body, string terminal)
	{
		var version = Regex.Match(body, "if \\(\\$server_protocol !~ \"(?<pattern>[^\"]+)\"\\) \\{(?<body>.*?)\\}", RegexOptions.Singleline);
		var method = Regex.Match(body, @"if \(\$request_method != POST\) \{(?<body>.*?)\}", RegexOptions.Singleline);

		Assert.True(version.Success);
		Assert.True(method.Success);
		Assert.Contains("return 400;", version.Groups["body"].Value);
		Assert.Contains("add_header Allow POST always;", method.Groups["body"].Value);
		Assert.Contains("return 405;", method.Groups["body"].Value);
		Assert.Equal(new[] { false, false, true, true, false }, new[] { "HTTP/1.0", "HTTP/1.1", "HTTP/2.0", "HTTP/3.0", "HTTP/4.0" }.Select(protocol => Regex.IsMatch(protocol, version.Groups["pattern"].Value, RegexOptions.CultureInvariant)));
		Assert.Contains(terminal, body);
		Assert.True(version.Index < method.Index);
		Assert.True(method.Index + method.Length < body.IndexOf(terminal, StringComparison.Ordinal));
	}

	private static BindingDocument.Binding Application(string alias, params BindingDocument.Endpoint[] endpoints) =>
		BindingDocument.CreateBinding("application", [alias], endpoints);
	private static BindingDocument.SiteRoute Route(BindingDocument.Site site, string kind, string path) =>
		Assert.Single(site.Routes, route => route.RequestKind == kind && route.Path == path);
	private static string Configuration(Configurator.Result result) =>
		Assert.Single(result.Files, file => file.Path.EndsWith(".conf", StringComparison.Ordinal)).Content.Render("/opt/example");
	private static string Template(Configurator.Result result) =>
		Assert.Single(result.Files, file => file.Path.EndsWith(".template", StringComparison.Ordinal)).Content.Render("/opt/example");
	private static Configurator.Result Configure(MigrationTestDirectory files, string profile, BindingDocument.Binding[] bindings, global::Zongsoft.Common.Variables variables = null) =>
		new Configurator.Nginx().Configure(Definition.Load(files.Write("web.profile", profile)),
			new("example", "/opt/example", Utility.CreateEvaluator(variables), new() { Bindings = bindings }));
}
