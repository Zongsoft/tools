using System;
using System.Linq;
using System.Collections.Generic;

using Zongsoft.Tools.Packager.Web;

using Xunit;

namespace Zongsoft.Tools.Packager.Tests.Web;

public class DefinitionResolutionTest
{
	[Theory]
	[InlineData("server-failure-count=-1")]
	[InlineData("server-failure-count=1.5")]
	[InlineData("server-failure-timeout=0")]
	[InlineData("server-failure-timeout=1m30s")]
	[InlineData("server-retry=off,error")]
	[InlineData("server-retry-count=")]
	[InlineData("server-balance=")]
	[InlineData("server-affinity=invalid")]
	[InlineData("server-health-interval=")]
	[InlineData("server-health-connect-timeout=-1s")]
	[InlineData("server-health-send-timeout=0")]
	[InlineData("server-health-read-timeout=0")]
	[InlineData("server-health-failure-count=0")]
	[InlineData("server-health-recovery-count=1.5")]
	[InlineData("server-health-status=99")]
	[InlineData("server-health-status=600")]
	[InlineData("server-health-status=6xx")]
	[InlineData("server-health-status=299-200")]
	[InlineData("server-health-status=200,")]
	[InlineData("server-health-status=")]
	[InlineData("websocket=")]
	[InlineData("forwarded=auto")]
	public void InvalidEffectivePoliciesAreRejectedEvenWhenHealthIsDisabled(string policy)
	{
		using var files = new MigrationTestDirectory();
		Assert.Throws<DefinitionException>(() => DefinitionLoadingTest.Resolve(files.Write("web.profile", "[api]\nbind!legacy=http://*\nserver=http://app\n" + policy)));
	}

	[Theory]
	[InlineData("http://*:0")]
	[InlineData("https://*:65536")]
	[InlineData("http://api.example.com")]
	[InlineData("ws://*")]
	[InlineData("http://*,")]
	[InlineData("http://*/")]
	public void InvalidBindingsAreRejected(string binding)
	{
		using var files = new MigrationTestDirectory();
		Assert.Throws<DefinitionException>(() => NginxConfiguratorTest.Configure(files, "[api]\nbind!legacy=" + binding + "\nserver=http://app"));
	}

	[Theory]
	[InlineData("http://0.0.0.0")]
	[InlineData("http://[::]")]
	[InlineData("http://app/api")]
	[InlineData("http://app?x=1")]
	[InlineData("http://app#fragment")]
	[InlineData("http://user@host")]
	[InlineData("http://app1;http://app2")]
	public void BackendMustBeOneConnectableOrigin(string server)
	{
		using var files = new MigrationTestDirectory();
		Assert.Throws<DefinitionException>(() => NginxConfiguratorTest.Configure(files, "[api]\nbind!legacy=http://*\nserver=" + server));
	}

	[Fact]
	public void BindingDefaultsDeduplicateAndTlsClearUsesTheNewBackendName()
	{
		using var files = new MigrationTestDirectory();
		var path = files.Write("web.profile", "server-tls-verify=true\nserver-tls-trust=file:/etc/ca.pem\nserver-tls-name=old.example\n[api]\nbind!legacy=http://*,http://0.0.0.0:80\nserver=https://original.example\n[api new]\nserver=https://new.example\nserver-tls-name=\nserver-tls-trust=");
		var site = Assert.Single(DefinitionLoadingTest.Resolve(path).Sites);
		Assert.Single(site.Bindings);
		var route = Assert.Single(site.Routes);
		Assert.Equal("/", route.Path);
		Assert.Equal("new.example", route.Server.Policy.Tls.Name);
		Assert.True(route.Server.Policy.Tls.Verify);
		Assert.Null(route.Server.Policy.Tls.Trust);
	}

	[Fact]
	public void HealthOverridesRemainIndependentAndTimeSpanParsingUsesCore()
	{
		using var files = new MigrationTestDirectory();
		var path = files.Write("web.profile", "server-health=/health\nserver-health-status=2xx,301,302\nserver-health-interval=1.5s\nserver-health-header!X-Probe=ready\n[api]\nbind!legacy=http://*\nserver=http://app\n[api disabled]\npath=/off\nserver-health=\n[api enabled]\npath=/on\nserver-health-status=200,204\nserver-health-read-timeout=500ms");
		var routes = DefinitionLoadingTest.Resolve(path).Sites[0].Routes;
		Assert.Null(routes[0].Server.Policy.Health);
		var health = routes[1].Server.Policy.Health;
		Assert.Equal(TimeSpan.FromSeconds(1.5), health.Interval);
		Assert.Equal(TimeSpan.FromMilliseconds(500), health.ReadTimeout);
		Assert.Equal(TimeSpan.FromSeconds(5), health.ConnectTimeout);
		Assert.Equal([200, 204], health.Status);
		Assert.Equal("ready", Assert.Single(health.Headers).Value);
	}

	[Fact]
	public void ImportedSeparateKeyCanBeExplicitlyCleared()
	{
		using var files = new MigrationTestDirectory();
		files.Write("shared.profile", "[api]\nbind!secure=https://*\nserver=http://app\ncertificate=file:/tls/old.pem\ncertificate-key=file:/tls/old.key");
		var path = files.Write("web.profile", "#@import shared.profile\n[api]\ncertificate=file:/tls/new.pem\ncertificate-key=");
		var site = DefinitionLoadingTest.Resolve(path).Sites[0];
		Assert.Equal("/tls/new.pem", site.Certificates.File.Path);
		Assert.Equal(site.Certificates.File, site.Certificates.Key);
	}

	[Theory]
	[InlineData("prefix", "/")]
	[InlineData("EXACT", "/")]
	[InlineData("regex", "^/api/[0-9]+$")]
	public void MatchAndPathDefaults(string match, string expected)
	{
		using var files = new MigrationTestDirectory();
		var profile = "[api]\nbind!legacy=http://*\nserver=http://app\n[api route]\nmatch=" + match + (match == "regex" ? "\npath=" + expected : "\npath=");
		var route = DefinitionLoadingTest.Resolve(files.Write("web.profile", profile)).Sites[0].Routes[0];
		Assert.Equal(expected, route.Path);
	}

	[Fact]
	public void NativeCertificateRequiresBothPartsAndSkipsCommonDefaults()
	{
		using var files = new MigrationTestDirectory();
		var profile = "[api]\nbind!secure=https://*\nserver=http://app\ncertificate=$(absent)\nnginx:ssl_certificate=/tls/native.pem";
		Assert.Equal("Required", Assert.Throws<DefinitionException>(() => NginxConfiguratorTest.Configure(files, profile)).Diagnostic.Code);
		var result = NginxConfiguratorTest.Configure(files, profile + "\nnginx:ssl_certificate_key=/tls/native.key");
		Assert.False(result.Files[0].Content.Relocatable);
		Assert.Contains("ssl_certificate_key /tls/native.key;", result.Files[0].Content.Render("/opt/app"));
	}

	[Fact]
	public void EqualEndpointMembersAndMixedProtocolsAreRejected()
	{
		using var files = new MigrationTestDirectory();
		var profile = "[api]\nbind!legacy=http://*\nserver!a=http://app";
		Assert.Equal("Duplicate", Assert.Throws<DefinitionException>(() => NginxConfiguratorTest.Configure(files, profile + "\nserver!b=http://app:80/")).Diagnostic.Code);
		Assert.Equal("Value", Assert.Throws<DefinitionException>(() => NginxConfiguratorTest.Configure(files, profile + "\nserver!b=https://other")).Diagnostic.Code);
	}

	[Fact]
	public void CookieScopeAndPoolSeparationFollowEffectiveHealthPolicy()
	{
		using var files = new MigrationTestDirectory();
		var result = NginxConfiguratorTest.Configure(files, "[api]\nbind!secure=https://*\nserver!a=http://app\nserver-affinity=COOKIE\nserver-health=/health\n[api on]\npath=/on\n[api off]\npath=/off\nserver-health=off");
		var text = result.Files[0].Content.Render("/opt/app");
		Assert.Equal(2, text.Split("upstream hoster_").Length - 1);
		Assert.Equal(2, text.Split("sticky cookie HOSTER_ROUTE_").Length - 1);
		Assert.Contains("path=/ httponly samesite=lax secure;", text);
		Assert.Equal(1, text.Split("health_check ").Length - 1);
	}
}
