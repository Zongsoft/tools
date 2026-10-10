using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Collections.Generic;

using Zongsoft.Tools.Packager.Web;

using Xunit;

namespace Zongsoft.Tools.Packager.Tests.Web;

public class WebBindingsTest
{
	[Theory]
	[InlineData(",")]
	[InlineData(";")]
	[InlineData(" , ")]
	[InlineData(" ; ")]
	public void ListsProduceCanonicalBindings(string separator)
	{
		using var files = new MigrationTestDirectory();
		var result = NginxConfiguratorTest.Configure(files, $"[api]\nhost=api.example.test{separator}www.example.test\nbind!public=http://*{separator}http://[::]\nserver=http://app");
		var bindings = Assert.Single(result.Files, file => file.Path.EndsWith("/.bindings", StringComparison.Ordinal)).Content.Render("/opt/example");

		Assert.Equal("[api]\r\nhost=api.example.test,www.example.test\r\nbind=http://0.0.0.0:80,http://[::]:80\r\n\r\n", bindings);
		Assert.Equal(3, result.Files.Count);
	}

	[Fact]
	public void NativeOverridesAndExplicitDefaultsAreExported()
	{
		using var files = new MigrationTestDirectory();
		var result = NginxConfiguratorTest.Configure(files, "[api]\nhost=old.example.test\nbind!old=http://*:1234\nserver=http://app\nnginx:listen=8080 default_server\nnginx:server_name=new.example.test");
		var bindings = result.Files.Single(file => file.Path.EndsWith("/.bindings", StringComparison.Ordinal)).Content.Render("/opt/example");
		Assert.Equal("[api]\r\nhost=new.example.test\r\nbind=http://0.0.0.0:8080\r\ndefault=http://0.0.0.0:8080\r\n\r\n", bindings);
	}

	[Theory]
	[InlineData("nginx:listen=unix:/run/app.sock")]
	[InlineData("nginx:listen=443 quic")]
	[InlineData("nginx:listen=80 proxy_protocol")]
	[InlineData("nginx:server_name=~^api[0-9]+[.]example[.]test$")]
	public void UnsupportedIngressOmitsTheWholeDescription(string directive)
	{
		using var files = new MigrationTestDirectory();
		var result = NginxConfiguratorTest.Configure(files, "[ordinary]\nbind!plain=http://*:8080\nserver=http://app\n[special]\nbind!plain=http://*:8081\nserver=http://app\n" + directive);

		Assert.DoesNotContain(result.Files, file => file.Path.EndsWith("/.bindings", StringComparison.Ordinal));
		Assert.Contains(result.Files, file => file.Path.EndsWith(".conf.template", StringComparison.Ordinal));
		Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Level == Diagnostic.Severity.Warning && diagnostic.Code == "Capability");
	}

	[Theory]
	[InlineData("")]
	[InlineData("server-balance=least-connections\n")]
	public void ApplicationAndResourcesAreMarkedWithoutChangingTheOrdinaryConfiguration(string policy)
	{
		using var files = new MigrationTestDirectory();
		var definition = Definition.Load(files.Write("web.profile", "[api]\nbind!secure=https://*\nserver=~\n" + policy));
		var result = new Configurator.Nginx().Configure(definition, new("example", "/opt/example", Utility.CreateEvaluator(), "http://127.0.0.1:8069"));
		var ordinary = result.Files.Single(file => file.Path.EndsWith(".conf", StringComparison.Ordinal)).Content.Render("/opt/example");
		var template = result.Files.Single(file => file.Path.EndsWith(".template", StringComparison.Ordinal)).Content.Render("/opt/example");

		Assert.Contains("127.0.0.1:8069", ordinary);
		Assert.DoesNotContain("{{zongsoft:", ordinary);
		Assert.Contains("{{zongsoft:application}}:8069", template);
		Assert.Contains("{{zongsoft:file:" + Convert.ToBase64String(Encoding.UTF8.GetBytes("./.certificates/example.pem")) + "}}", template);
		Assert.DoesNotContain("/opt/example", template);
	}

	[Fact]
	public void ExplicitLoopbackBackendAndNativeCertificateRetainTheirIdentity()
	{
		using var files = new MigrationTestDirectory();
		var result = NginxConfiguratorTest.Configure(files, "[api]\nbind!secure=https://*\nserver=http://127.0.0.1:9000\nnginx:ssl_certificate=/keys/site.pem\nnginx:ssl_certificate_key=/keys/site.pem");
		var template = result.Files.Single(file => file.Path.EndsWith(".template", StringComparison.Ordinal)).Content.Render("/opt/example");

		Assert.Contains("proxy_pass http://127.0.0.1:9000;", template);
		Assert.DoesNotContain("{{zongsoft:application}}", template);
		Assert.Contains("{{zongsoft:file:" + Convert.ToBase64String(Encoding.UTF8.GetBytes("/keys/site.pem")) + "}}", template);
	}
}
