using System;
using System.Linq;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.Runtime.InteropServices;

using Zongsoft.Tools.Packager.Web;

using Xunit;

namespace Zongsoft.Tools.Packager.Tests.Web;

public class WebPolicyTest
{
	[Theory]
	[InlineData("30s", "30000ms")]
	[InlineData("500ms", "500ms")]
	[InlineData("1.5s", "1500ms")]
	[InlineData("1.5m", "90000ms")]
	[InlineData("00:00:02", "2000ms")]
	public void DurationsAreParsedAndEmittedExactly(string value, string expected)
	{
		using var files = new MigrationTestDirectory();
		var text = Generate(files, "server-health=/health\nserver-health-interval=" + value);
		Assert.Contains("interval=" + expected, text);
	}

	[Theory]
	[InlineData("server!a=http://app weight=9223372036854775808")]
	[InlineData("server-retry-count=9223372036854775807")]
	[InlineData("server!a=http://app\nserver-failure-timeout=500ms")]
	[InlineData("server-health=/\nserver-health-interval=00:00:00.0000001")]
	[InlineData("server-balance=least-requests")]
	public void UnrepresentableValuesFailInsteadOfBeingTruncated(string values)
	{
		using var files = new MigrationTestDirectory();
		Assert.Equal("Capability", Assert.Throws<DefinitionException>(() => Generate(files, values)).Diagnostic.Code);
	}

	[Fact]
	public void TargetArchitectureDeterminesNumericBounds()
	{
		using var files = new MigrationTestDirectory();
		var definition = Definition.Load(files.Write("web.profile", "[api]\nbind!legacy=http://*\nserver!a=http://app weight=2147483648"));
		var configurator = new Configurator.Nginx();
		Assert.Equal("Capability", Assert.Throws<DefinitionException>(() => configurator.Configure(definition, new("example", "/opt/app", new Dictionary<string, string>(), architecture: Architecture.X86))).Diagnostic.Code);
		Assert.Contains("weight=2147483648", configurator.Configure(definition, new("example", "/opt/app", new Dictionary<string, string>())).Files[0].Content.Render("/opt/app"));
	}

	[Theory]
	[InlineData("http://app", "http://app:80")]
	[InlineData("http://app/", "http://app:80")]
	[InlineData("https://APP/", "https://app:443")]
	[InlineData("http://[2001:db8::1]:8069", "http://[2001:db8::1]:8069")]
	public void ExplicitOriginsPreserveRequestUriByOmittingProxyUri(string origin, string expected)
	{
		using var files = new MigrationTestDirectory();
		Assert.Contains("proxy_pass " + expected + ";", Generate(files, "server=" + origin));
	}

	[Theory]
	[InlineData("server-retry=off", "off;")]
	[InlineData("server-retry-count=0", "off;")]
	[InlineData("server-retry=timeout,http_503\nserver-retry-count=2", "timeout http_503;")]
	public void RetryCountMeansAdditionalAttempts(string policy, string expected)
	{
		using var files = new MigrationTestDirectory();
		var text = Generate(files, policy);
		Assert.Contains("proxy_next_upstream " + expected, text);
		if(expected == "off;")
			Assert.DoesNotContain("proxy_next_upstream_tries", text);
		else
			Assert.Contains("proxy_next_upstream_tries 3;", text);
	}

	[Theory]
	[InlineData("server=https://backend.example", "backend.example")]
	[InlineData("server!a=https://backend.example", "backend.example")]
	[InlineData("server!a=https://backend.example:443\nserver!b=https://backend.example:8443", "backend.example")]
	[InlineData("server=https://192.0.2.1", null)]
	[InlineData("server!a=https://one.example\nserver!b=https://two.example", null)]
	public void TlsNamesComeOnlyFromEffectiveBackendAddresses(string servers, string name)
	{
		using var files = new MigrationTestDirectory();
		var text = Generate(files, servers);
		Assert.Contains("proxy_ssl_verify off;", text);
		Assert.Contains("proxy_ssl_server_name " + (name == null ? "off" : "on") + ";", text);
		if(name == null)
			Assert.Equal("Required", Assert.Throws<DefinitionException>(() => Generate(files, servers + "\nserver-tls-verify=true")).Diagnostic.Code);
		else
			Assert.Contains("proxy_ssl_name " + name + ";", text);
	}

	[Fact]
	public void HealthHeadersTlsAndTimeoutsAreIndependentFromBusinessRequests()
	{
		using var files = new MigrationTestDirectory();
		var text = Generate(files, "server=https://backend.example\nserver-tls-verify=true\nserver-tls-trust=file:/trust/ca.pem\nserver-health=/ready?mode=full\nserver-health-header!Host=probe.example\nserver-health-header!X-Empty=\nheader!X-Business=business\nwebsocket=true\nnginx:proxy_read_timeout=60s");
		var probe = text[text.IndexOf("location @", StringComparison.Ordinal)..text.IndexOf("location /", StringComparison.Ordinal)];
		Assert.Contains("proxy_read_timeout 5000ms;", probe);
		Assert.Contains("proxy_method GET;", probe);
		Assert.Contains("proxy_pass_request_headers off;", probe);
		Assert.Contains("proxy_set_header Host probe.example;", probe);
		Assert.Contains("proxy_set_header X-Empty \"\";", probe);
		Assert.Contains("proxy_ssl_verify on;", probe);
		Assert.Contains("proxy_ssl_trusted_certificate /trust/ca.pem;", probe);
		Assert.DoesNotContain("X-Business", probe);
		Assert.DoesNotContain("Upgrade", probe);
		Assert.DoesNotContain("X-Forwarded", probe);
		Assert.Contains("uri=/ready?mode=full", probe);
	}

	[Fact]
	public void RouteOverridesKeepOtherHeadersAndReplaceTheEntireBackendPool()
	{
		using var files = new MigrationTestDirectory();
		var path = files.Write("web.profile", "server!one=http://one\nserver!two=http://two\nheader!X-Root=root\nserver-retry=timeout\n[api]\nbind!legacy=http://*\nheader!X-Value=site\n[api route]\nserver!three=http://three\nheader!x-value=\nwebsocket=true\nnginx:proxy_set_header!Connection=close");
		var route = DefinitionLoadingTest.Resolve(path).Sites[0].Routes[0];
		Assert.Equal("three", Assert.Single(route.Server.Members).Name);
		Assert.Equal(["timeout"], route.Server.Policy.Retry.Conditions);
		Assert.Contains(route.Headers, item => item.Name == "X-Root" && item.Value == "root");
		Assert.Contains(route.Headers, item => item.Name == "x-value" && item.Value == "");
		Assert.Contains(route.Headers, item => item.Name == "Connection" && item.Value == "close");
		Assert.Contains(route.Headers, item => item.Name == "Host" && item.Value == "$http_host");
	}

	[Theory]
	[InlineData("server-health-status=100,599,1xx,5XX", "100-199 500-599")]
	[InlineData("server-health-status=200-299, 204, 3xx", "200-399")]
	[InlineData("server-health-status=200,204", "200 204")]
	public void StatusUnionIsNormalizedWithoutWideningTheAcceptedSet(string value, string expected)
	{
		using var files = new MigrationTestDirectory();
		Assert.Contains("status " + expected + ";", Generate(files, "server-health=/\n" + value));
	}

	[Theory]
	[InlineData("")]
	[InlineData("server-health=")]
	[InlineData("server-health=OFF")]
	[InlineData("server-health-status=2xx")]
	[InlineData("server-health-header!Host=probe.example")]
	public void AncillaryHealthSettingsNeverEnableProbes(string value)
	{
		using var files = new MigrationTestDirectory();
		Assert.DoesNotContain("health_check", Generate(files, value));
	}

	[Theory]
	[InlineData("header!X-Value=$host")]
	[InlineData("header!X-Value=$$(missing)")]
	[InlineData("header!X-Value=invalid\0text")]
	public void UnsupportedLiteralDollarDoesNotBecomeARuntimeExpression(string value)
	{
		using var files = new MigrationTestDirectory();
		Assert.Equal("Capability", Assert.Throws<DefinitionException>(() => Generate(files, value)).Diagnostic.Code);
	}

	[Fact]
	public async Task ConcurrentCallsHaveNoSharedPoolOrVariableStateAsync()
	{
		using var files = new MigrationTestDirectory();
		var definition = Definition.Load(files.Write("web.profile", "[api]\nbind!legacy=http://*\nserver!a=http://$(backend)\nserver-health=/"));
		var configurator = new Configurator.Nginx();
		var tasks = Enumerable.Range(0, 8).Select(index => Task.Run(() =>
		{
			var context = new Configurator.Context("app" + index, "/opt/app" + index, new Dictionary<string, string> { ["backend"] = "backend" + index });
			var first = configurator.Configure(definition, context).Files[0].Content.Render(context.InstallPath);
			Assert.Equal(first, configurator.Configure(definition, context).Files[0].Content.Render(context.InstallPath));
			Assert.Contains("server backend" + index + ":80", first);
			return first;
		}, TestContext.Current.CancellationToken));
		Assert.Equal(8, (await Task.WhenAll(tasks)).Distinct().Count());
	}

	[Theory]
	[InlineData(8069, 8080)]
	[InlineData(5001, 81)]
	[InlineData(5002, 82)]
	[InlineData(5003, 83)]
	[InlineData(5011, 8011)]
	[InlineData(5012, 8012)]
	public void ExistingHostingLayoutsSupportDualStackNamedAndPortOnlySites(int backend, int port)
	{
		using var files = new MigrationTestDirectory();
		var text = NginxConfiguratorTest.Configure(files,
			"server=http://127.0.0.1:" + backend + "\n[api]\nhost=api.example.com\nbind!legacy=http://*,http://[::]\nnginx:ssl_session_cache=shared:SSL:1m\nnginx:ssl_session_timeout=10m\nnginx:ssl_ciphers=PROFILE=SYSTEM\nnginx:ssl_prefer_server_ciphers=on\n[port]\nhost=_\nbind!legacy=http://*:" + port + ",http://[::]:" + port)
			.Files[0].Content.Render("/opt/app");
		Assert.Equal(2, text.Split("server {").Length - 1);
		Assert.Equal(2, text.Split("proxy_pass http://127.0.0.1:" + backend + ";").Length - 1);
		Assert.Contains("listen [::]:" + port + ";", text);
		Assert.Contains("ssl_session_cache shared:SSL:1m;", text);
	}

	[Fact]
	public void ExplicitPassivePolicyOnOneOriginIsNotSilentlyIgnored()
	{
		using var files = new MigrationTestDirectory();
		var text = Generate(files, "server-failure-count=0\nserver-failure-timeout=1m");
		Assert.Contains("server app:80 weight=1 max_fails=0 fail_timeout=60s;", text);
		Assert.Contains("proxy_pass http://hoster_", text);
	}


	private static string Generate(MigrationTestDirectory files, string values)
	{
		var server = values.Split('\n').Any(line => line.StartsWith("server=", StringComparison.Ordinal) || line.StartsWith("server!", StringComparison.Ordinal)) ? "" : "server=http://app\n";
		return NginxConfiguratorTest.Configure(files, "[api]\nbind!legacy=http://*\n" + server + values).Files[0].Content.Render("/opt/app");
	}
}
