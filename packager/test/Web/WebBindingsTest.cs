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
		var bindings = BindingDocument.Deserialize(result.Bindings.Serialize());

		Assert.Equal(["api.example.test", "www.example.test"], Assert.Single(bindings.Sites).Hosts);
		Assert.Equal(["0.0.0.0", "::"], bindings.Bindings.SelectMany(binding => binding.Endpoints).Select(endpoint => endpoint.Host).Order(StringComparer.Ordinal));
		Assert.Equal(2, result.Files.Count);
	}

	[Fact]
	public void NativeOverridesAndExplicitDefaultsAreExported()
	{
		using var files = new MigrationTestDirectory();
		var result = NginxConfiguratorTest.Configure(files, "[api]\nhost=old.example.test\nbind!old=http://*:1234\nserver=http://app\nnginx:listen=8080 default_server\nnginx:server_name=new.example.test");
		var bindings = BindingDocument.Deserialize(result.Bindings.Serialize());
		Assert.Equal(["new.example.test"], Assert.Single(bindings.Sites).Hosts);
		Assert.True(Assert.Single(bindings.Sites[0].Listeners).Default);
		Assert.Equal(8080, Assert.Single(Assert.Single(bindings.Bindings).Endpoints).Port);
	}

	[Theory]
	[InlineData("nginx:listen=unix:/run/app.sock")]
	[InlineData("nginx:listen=443 quic\nnginx:http3=off")]
	[InlineData("nginx:listen=80 proxy_protocol")]
	[InlineData("nginx:server_name=~^api[0-9]+[.]example[.]test$")]
	public void UnsupportedIngressFailsBeforePublication(string directive)
	{
		using var files = new MigrationTestDirectory();
		Assert.Equal("Capability", Assert.Throws<DefinitionException>(() => NginxConfiguratorTest.Configure(files,
			"[ordinary]\nbind!plain=http://*:8080\nserver=http://app\n[special]\nbind!plain=http://*:8081\nserver=http://app\n" + directive)).Diagnostic.Code);
	}

	[Theory]
	[InlineData("")]
	[InlineData("server-balance=least-connections\n")]
	public void ApplicationAndResourcesAreMarkedWithoutChangingTheOrdinaryConfiguration(string policy)
	{
		using var files = new MigrationTestDirectory();
		var definition = Definition.Load(files.Write("web.profile", "[api]\nbind!secure=https://*\n" + policy));
		var binding = BindingDocument.CreateBinding("application", ["web"], [new("http", "0.0.0.0", 8069, "/", ["http1"])]);
		var result = new Configurator.Nginx().Configure(definition, new("example", "/opt/example", Utility.CreateEvaluator(), new() { Bindings = [binding] }));
		var ordinary = result.Files.Single(file => file.Path.EndsWith(".conf", StringComparison.Ordinal)).Content.Render("/opt/example");
		var template = result.Files.Single(file => file.Path.EndsWith(".template", StringComparison.Ordinal)).Content.Render("/opt/example");

		Assert.Contains("127.0.0.1:8069", ordinary);
		Assert.DoesNotContain("{{zongsoft:", ordinary);
		Assert.Contains("{{zongsoft:binding:" + binding.Id + ":" + binding.Endpoints[0].Id + "}}", template);
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
		Assert.DoesNotContain("{{zongsoft:binding:", template);
		Assert.Contains("{{zongsoft:file:" + Convert.ToBase64String(Encoding.UTF8.GetBytes("/keys/site.pem")) + "}}", template);
	}
}
