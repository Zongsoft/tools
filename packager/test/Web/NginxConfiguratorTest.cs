using System;
using System.Linq;
using System.Collections.Generic;

using Zongsoft.Tools.Packager.Web;

using Xunit;

namespace Zongsoft.Tools.Packager.Tests.Web;

public class NginxConfiguratorTest
{
	[Fact]
	public void DefaultCertificateSupportsRelocationWithoutRewritingExternalPaths()
	{
		using var files = new MigrationTestDirectory();
		var result = Configure(files, "[api]\nbind!secure=https://*,https://[::]\nserver=http://app\ncertificate-key=file:/keys/private.pem");
		var content = Assert.Single(result.Files).Content;
		Assert.True(content.Relocatable);
		var text = content.Render("/opt/new");
		Assert.Contains("ssl_certificate \"/opt/new/.certificates/example.pem\";", text);
		Assert.Contains("ssl_certificate_key /keys/private.pem;", text);
		Assert.Contains("listen [::]:443 ssl;", text);
		Assert.DoesNotContain("http {", text);
		Assert.DoesNotContain("\n ", text);
	}

	[Fact]
	public void NativeGroupsMaskUnusedCommonValuesAndApplicationInference()
	{
		using var files = new MigrationTestDirectory();
		var result = Configure(files, "[api]\nbind!legacy=$(absent)\nhost=$(absent)\nnginx:listen!80\nnginx:server_name=example.com\nserver=~\n[api home]\nnginx:proxy_pass=http://app");
		var text = Assert.Single(result.Files).Content.Render("/opt/example");
		Assert.Contains("listen 80;", text);
		Assert.Contains("proxy_pass http://app;", text);
		Assert.NotEmpty(result.Diagnostics);
	}

	[Fact]
	public void EquivalentRoutesShareOnePoolAndOneHealthCheck()
	{
		using var files = new MigrationTestDirectory();
		var result = Configure(files, "[api]\nbind!legacy=http://*\nserver!a=http://app1 weight=3\nserver!b=http://app2\nserver-health=/health?ready=1\nserver-health-status=200-299,3XX\n[api a]\npath=/a\n[api b]\npath=/b");
		var text = Assert.Single(result.Files).Content.Render("/opt/example");
		Assert.Equal(1, text.Split("upstream hoster_").Length - 1);
		Assert.Equal(1, text.Split("health_check ").Length - 1);
		Assert.Contains("status 200-399;", text);
		Assert.Contains("server app1:80 weight=3 max_fails=3 fail_timeout=30s;", text);
		Assert.Contains("proxy_connect_timeout 5000ms;", text);
		Assert.Contains("proxy_next_upstream_tries 2;", text);
	}

	[Theory]
	[InlineData("nginx:listen!80;", "Directive")]
	[InlineData("nginx:if=($host) { return 400; }", "Directive")]
	[InlineData("nginx:http.server.listen=80", "Directive")]
	[InlineData("nginx:charset=utf-8;", "Directive")]
	[InlineData("nginx:worker_cpu_affinity=auto", "Directive")]
	[InlineData("nginx:log_format=custom '$request'", "Directive")]
	[InlineData("nginx:proxy_next_upstream_tries!2=3", "Directive")]
	public void NativeBlockOrStatementInjectionIsRejected(string entry, string code)
	{
		using var files = new MigrationTestDirectory();
		var error = Assert.Throws<DefinitionException>(() => Configure(files, "[api]\nbind!legacy=http://*\nserver=http://app\n" + entry));
		Assert.Equal(code, error.Diagnostic.Code);
	}

	[Fact]
	public void RegexAndHeaderPriorityArePreserved()
	{
		using var files = new MigrationTestDirectory();
		var result = Configure(files, "[api]\nbind!legacy=http://*\nserver=http://app\nnginx:proxy_set_header!Host=$(unused)\n[api route]\nmatch=regex\npath=^/items/[0-9]{2}$\nheader!Host=literal.example\nnginx:proxy_set_header!X-Remote=$remote_addr");
		var text = Assert.Single(result.Files).Content.Render("/opt/example");
		Assert.Contains("location ~* \"^/items/[0-9]{2}$\"", text);
		Assert.Contains("proxy_set_header Host literal.example;", text);
		Assert.Contains("proxy_set_header X-Remote $remote_addr;", text);
		Assert.DoesNotContain("$(unused)", text);
	}

	[Theory]
	[InlineData("nginx:listen=80\nnginx:server_name='api.example.com'")]
	[InlineData("bind!legacy=http://0.0.0.0:80\nhost=api.example.com")]
	public void RawAndCommonListenersUseTheSameConflictIdentity(string first)
	{
		using var files = new MigrationTestDirectory();
		var profile = "server=http://app\n[one]\n" + first + "\n[two]\nnginx:listen='*:80'\nnginx:server_name=API.EXAMPLE.COM";
		var error = Assert.Throws<DefinitionException>(() => Configure(files, profile));
		Assert.Equal("Duplicate", error.Diagnostic.Code);
		Assert.NotNull(error.Diagnostic.Related);
	}

	[Theory]
	[InlineData("http://app/api/")]
	[InlineData("'http://app/'")]
	[InlineData("http://app?query=1")]
	public void RegexNativeProxyCannotReplaceAStaticUri(string target)
	{
		using var files = new MigrationTestDirectory();
		var profile = "[api]\nnginx:listen!80\n[api route]\nmatch=regex\npath=^/api\nnginx:proxy_pass=" + target;
		Assert.Equal("Directive", Assert.Throws<DefinitionException>(() => Configure(files, profile)).Diagnostic.Code);
	}

	[Fact]
	public void NativeHeaderCanUseAQuotedNameAndAnEmptyValue()
	{
		using var files = new MigrationTestDirectory();
		var result = Configure(files, "[api]\nbind!legacy=http://*\nserver=http://app\nnginx:proxy_set_header='Host'");
		Assert.Contains("proxy_set_header Host \"\";", result.Files[0].Content.Render("/opt/app"));
	}


	internal static Configurator.Result Configure(MigrationTestDirectory files, string profile) =>
		new Configurator.Nginx().Configure(Definition.Load(files.Write("web.profile", profile)), new("example", "/opt/example", new Dictionary<string, string>()));
}
