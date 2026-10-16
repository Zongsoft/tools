using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Collections.Generic;

using Xunit;
using Zongsoft.Components;
using Zongsoft.Tools.Containerizer.Protocol;

namespace Zongsoft.Tools.Containerizer.Tests;

public sealed class WebIngressTests : IDisposable
{
	private readonly string _root = Path.Combine(Path.GetTempPath(), "containerizer-web-" + Guid.NewGuid().ToString("N"));
	public WebIngressTests() => Directory.CreateDirectory(_root);
	public void Dispose() => Directory.Delete(_root, true);

	[Fact]
	public void PackageInfersHosterAndPublishesFrontendWithoutInternalListener()
	{
		var manifest = this.CreateManifest(WebFixtures.CreateDocument(new WebFixtures.Site("name", ["http://0.0.0.0:80", "http://[::]:80"], ["api.example.test"]), new("port", ["http://0.0.0.0:8080", "http://[::]:8080"])));
		var sources = ServicePlanner.Prepare(manifest);
		var app = sources.Single(source => source.Plan.Id == "web");
		var nginx = sources.Single(source => source.Plan.Id == "nginx");
		Assert.Equal("nginx", manifest.Components[0]["dependences"]);
		Assert.Empty(app.Plan.Ports);
		Assert.Equal(["CMD-SHELL", "kill -0 1"], app.Plan.Health.Test);
		Assert.Equal([80, 8080], nginx.Plan.Ports.Select(port => port.Container));
		Assert.Equal(4, nginx.Plan.Web.Sum(site => site.Bindings.Count));
		Assert.Contains("web", nginx.Plan.Dependencies);
		WebIngress.Render(sources, _root);
		Assert.Contains("proxy_pass http://web:8069", File.ReadAllText(nginx.Configuration["/etc/nginx/containerizer/web.conf"].Source));
		var main = File.ReadAllText(nginx.Configuration["/etc/nginx/nginx.conf"].Source);
		Assert.Contains("include /etc/nginx/containerizer/web.conf;", main);
		Assert.DoesNotContain("conf.d", main);
	}

	[Theory]
	[InlineData(false)]
	[InlineData(true)]
	public void IncompleteDeclaredHosterNeverFallsBack(bool hasSites)
	{
		var document = hasSites ? WebFixtures.CreateDocument(new WebFixtures.Site("api", ["http://0.0.0.0:80"])) : new BindingDocument();
		var path = WebFixtures.WritePackage(Path.Combine(_root, "web.tar.gz"), "web", document, hasSites ? null : "server {}");
		Assert.Throws<ContainerizationException>(() => PackageReader.Read(path));
	}

	[Fact]
	public void NormalizingAgainKeepsInferredDependenciesAndComponentOrder()
	{
		var manifest = this.CreateManifest(WebFixtures.CreateDocument(new WebFixtures.Site("api", ["http://0.0.0.0:80"])));
		var names = manifest.Components.Select(component => component.Name).ToArray();
		var dependences = manifest.Components[0]["dependences"];

		manifest.Normalize();
		manifest.Normalize();

		Assert.Equal(names, manifest.Components.Select(component => component.Name));
		Assert.Equal(dependences, manifest.Components[0]["dependences"]);
		Assert.Single(manifest.Components, component => component.Name == "nginx");
		Assert.Single(ServicePlanner.Prepare(manifest).Single(source => source.Plan.Id == "nginx").Plan.Dependencies);
	}

	[Theory]
	[InlineData("[api]\nbind=http://0.0.0.0:80")]
	[InlineData("{\"version\":1,\"bindings\":[],\"sites\":[]}")]
	public void LegacyWebBindingsAreRejectedEvenWithValidRootJson(string legacy)
	{
		var document = WebFixtures.CreateDocument(new WebFixtures.Site("api", ["http://0.0.0.0:80"]));
		var path = WebFixtures.WritePackage(Path.Combine(_root, "web.tar.gz"), "web", document, WebFixtures.Template(document), new() { [".web/nginx/.bindings"] = legacy });
		Assert.Throws<ContainerizationException>(() => PackageReader.Read(path));
	}

	[Fact]
	public void JsonHostsAndExplicitDefaultsArePreserved()
	{
		var manifest = this.CreateManifest(WebFixtures.CreateDocument(new WebFixtures.Site("api", ["http://0.0.0.0:80", "http://[::]:80"], ["a.test", "b.test", "c.test"], true)));
		var site = Assert.Single(manifest.Components[0].Package.Web.Sites);
		Assert.Equal(["a.test", "b.test", "c.test"], site.Hosts);
		Assert.All(site.Bindings, binding => Assert.True(binding.IsExplicitDefault));
	}

	[Theory]
	[InlineData("80:0")]
	[InlineData("80:65536")]
	[InlineData("90:8080")]
	[InlineData("80:8080,80:none")]
	[InlineData("80:443")]
	[InlineData("80:8080,443:8080")]
	public void RejectsInvalidOrConflictingPortOverrides(string text) => Assert.Throws<ContainerizationException>(() => ServicePlanner.Prepare(this.CreateManifest(WebFixtures.CreateDocument(new WebFixtures.Site("http", ["http://0.0.0.0:80"]), new("https", ["https://0.0.0.0:443"], ["example.test"])), "port=" + text)));

	[Fact]
	public void PortNoneRetainsNativeBindingsAndRemovesPublicationAndProbeRequirement()
	{
		var manifest = this.CreateManifest(WebFixtures.CreateDocument(new WebFixtures.Site("api", ["https://127.0.0.1:443"], ["*.example.test"]), new("local", ["http://0.0.0.0:80"])), "port=80:18080,443:none");
		var sources = ServicePlanner.Prepare(manifest);
		var nginx = sources.Single(source => source.Plan.Id == "nginx");
		Assert.Equal(18080, Assert.Single(nginx.Plan.Ports).Host);
		Assert.Null(nginx.Plan.Web[0].Bindings[0].Publication);
		Assert.Empty(nginx.Plan.Web[0].ProbeHosts);
		Assert.Equal("127.0.0.1", nginx.Plan.Web[0].Bindings[0].Address);
		using var bundle = ContainerBundle.Open(this.WriteBundle(sources.Select(source => source.Plan).ToList()));
		Assert.Equal("none", bundle.Plan.Services.Single(service => service.Id == "web").Bindings.Single(binding => binding.Endpoints.Any(endpoint => endpoint.Port == 443)).Publication);
	}

	[Theory]
	[InlineData("auto", false)]
	[InlineData("none", false)]
	[InlineData("tcp://127.0.0.1:18069", true)]
	public void DirectPublicationPolicyPreservesIngressRouteAndTemplate(string publication, bool direct)
	{
		var document = WebFixtures.CreateDocument(new WebFixtures.Site("api", ["http://0.0.0.0:80"]));
		var manifest = this.CreateManifest(document);
		manifest.Components[0]["publish!web"] = publication;
		var sources = ServicePlanner.Prepare(manifest);
		var application = sources.Single(source => source.Plan.Id == "web");
		var ingress = sources.Single(source => source.Plan.Id == "nginx");
		WebIngress.Render(sources, _root);

		Assert.Equal(direct ? 1 : 0, application.Plan.Ports.Count);
		Assert.Equal(80, Assert.Single(ingress.Plan.Ports).Host);
		Assert.Contains("proxy_pass http://web:8069;", File.ReadAllText(ingress.Configuration["/etc/nginx/containerizer/web.conf"].Source));
		Assert.Single(application.Plan.Bindings.Single(binding => binding.Owner == "application").Connections);

		if(direct)
		{
			Assert.Equal(18069, application.Plan.Ports[0].Host);
			Assert.Equal(8069, application.Plan.Ports[0].Container);
		}

		using var bundle = ContainerBundle.Open(this.WriteBundle(sources.Select(source => source.Plan).ToList()));
		Assert.Equal(direct ? "explicit" : publication, bundle.Plan.Services.Single(service => service.Id == "web").Bindings.Single(binding => binding.Owner == "application").Publication);
	}

	[Theory]
	[InlineData("http://127.0.0.1:80")]
	[InlineData("http://192.0.2.10:80")]
	[InlineData("http://[::]:80")]
	public void UnpublishableFrontendFailsMaking(string bindings)
	{
		var manifest = this.CreateManifest(WebFixtures.CreateDocument(new WebFixtures.Site("api", [bindings])));
		Assert.Throws<ContainerizationException>(() => ServicePlanner.Prepare(manifest));
	}

	[Fact]
	public void UnequalDualStackIsRejected()
	{
		var manifest = this.CreateManifest(WebFixtures.CreateDocument(new WebFixtures.Site("a", ["http://0.0.0.0:80", "http://[::]:80"], ["a.test"]), new("b", ["http://0.0.0.0:80"], ["b.test"])));
		Assert.Throws<ContainerizationException>(() => ServicePlanner.Prepare(manifest));
	}

	[Fact]
	public void WildcardProbeMustMatchItsSiteWithoutBeingShadowed()
	{
		var manifest = this.CreateManifest(WebFixtures.CreateDocument(new WebFixtures.Site("tenant", ["http://0.0.0.0:80"], ["*.example.test"]), new("admin", ["http://0.0.0.0:80"], ["admin.example.test"])));
		Assert.Throws<ContainerizationException>(() => ServicePlanner.Prepare(manifest));
		manifest.Components[0]["probe-host!tenant"] = "admin.example.test";
		Assert.Throws<ContainerizationException>(() => ServicePlanner.Prepare(manifest));
		manifest.Components[0]["probe-host!tenant"] = "demo.example.test";
		var nginx = ServicePlanner.Prepare(manifest).Single(source => source.Plan.Id == "nginx");
		Assert.Equal(["demo.example.test"], nginx.Plan.Web.Single(site => site.Name == "tenant").ProbeHosts);
	}

	[Fact]
	public void HostlessSiteMustBeTheActualDefaultAndConflictingDefaultsFail()
	{
		var first = new WebFixtures.Site("api", ["http://0.0.0.0:80"], ["api.test"]);
		var second = new WebFixtures.Site("local", ["http://0.0.0.0:80"]);
		Assert.Throws<ContainerizationException>(() => ServicePlanner.Prepare(this.CreateManifest(WebFixtures.CreateDocument(first, second))));
		var nginx = ServicePlanner.Prepare(this.CreateManifest(WebFixtures.CreateDocument(first, second with { Default = true }))).Single(source => source.Plan.Id == "nginx");
		Assert.True(nginx.Plan.Web[1].Bindings[0].IsDefault);
		Assert.False(nginx.Plan.Web[0].Bindings[0].IsDefault);
		Assert.Throws<InvalidDataException>(() => WebFixtures.CreateDocument(first with { Default = true }, second with { Default = true }).Serialize());
	}

	[Fact]
	public void ResourcesAreCollectedOnceAndExternalInputsUseSourceAndReadonlyTargets()
	{
		var relative = "{{zongsoft:file:" + Convert.ToBase64String(Encoding.UTF8.GetBytes("./.certificates/site.pem")) + "}}";
		var external = "{{zongsoft:file:" + Convert.ToBase64String(Encoding.UTF8.GetBytes("/etc/tls/Trust.pem")) + "}}";
		var document = WebFixtures.CreateDocument(new WebFixtures.Site("api", ["http://0.0.0.0:80"]));
		var template = WebFixtures.Template(document) + $"ssl_certificate \"{relative}\";\nssl_certificate_key \"{relative}\";\nproxy_ssl_trusted_certificate \"{external}\";";
		var manifest = this.CreateManifest(document, template: template, files: new() { [".certificates/site.pem"] = "private fixture" });
		File.WriteAllText(Path.Combine(_root, "trust.pem"), "trust fixture");
		manifest.Components.Single(component => component.Name == "nginx")["file!/etc/tls/Trust.pem"] = "./trust.pem";
		var sources = ServicePlanner.Prepare(manifest);
		WebIngress.Render(sources, _root);
		var nginx = sources.Single(source => source.Plan.Id == "nginx");
		var owned = Assert.Single(nginx.Configuration, pair => pair.Key.StartsWith("/etc/nginx/containerizer-files/", StringComparison.Ordinal));
		Assert.Equal("private fixture", File.ReadAllText(owned.Value.Source));
		Assert.Equal(Path.Combine(_root, "trust.pem"), nginx.Configuration["/etc/tls/Trust.pem"].Source);
		var text = File.ReadAllText(nginx.Configuration["/etc/nginx/containerizer/web.conf"].Source);
		Assert.Equal(2, text.Split(owned.Key).Length - 1);
		Assert.Contains("/etc/tls/Trust.pem", text);
		Assert.DoesNotContain("private fixture", text);
	}

	[Fact]
	public void MissingResourcesAndReservedTemplateMarkersFailBeforeEngineUse()
	{
		var marker = "{{zongsoft:file:" + Convert.ToBase64String(Encoding.UTF8.GetBytes("./missing.pem")) + "}}";
		Assert.Throws<ContainerizationException>(() => ServicePlanner.Prepare(this.CreateManifest(WebFixtures.CreateDocument(new WebFixtures.Site("api", ["http://0.0.0.0:80"])), template: marker)));
		Assert.Throws<ContainerizationException>(() => ServicePlanner.Prepare(this.CreateManifest(WebFixtures.CreateDocument(new WebFixtures.Site("api", ["http://0.0.0.0:80"])), template: "{{zongsoft:unknown}}")));
		Assert.Throws<ContainerizationException>(() => ServiceSettings.Parse("port=80:8080;port=80:none"));
	}

	[Theory]
	[InlineData("reference")]
	[InlineData("site")]
	[InlineData("default")]
	[InlineData("binding")]
	[InlineData("port")]
	[InlineData("application")]
	[InlineData("port-application")]
	[InlineData("port-binding")]
	[InlineData("port-endpoint")]
	[InlineData("port-empty")]
	[InlineData("port-duplicate")]
	[InlineData("connection-original")]
	[InlineData("connection-selected")]
	[InlineData("connection-host")]
	[InlineData("connection-port")]
	[InlineData("connection-protocol")]
	[InlineData("listener-binding")]
	[InlineData("listener-endpoint")]
	[InlineData("listener-protocol")]
	[InlineData("publication-none")]
	[InlineData("publication-pending")]
	[InlineData("declaration-duplicate")]
	[InlineData("is-proxied")]
	[InlineData("null-declaration")]
	[InlineData("null-endpoint")]
	[InlineData("null-connection")]
	[InlineData("null-reference")]
	public void DeliveryProtocolRejectsBrokenWebRelationships(string failure)
	{
		var sources = ServicePlanner.Prepare(this.CreateManifest(WebFixtures.CreateDocument(new WebFixtures.Site("api", ["http://0.0.0.0:80"]))));
		var services = sources.Select(source => source.Plan).ToList();
		using var bundle = ContainerBundle.Open(this.WriteBundle(services));
		var nginx = services.Single(service => service.Id == "nginx");
		var application = services.Single(service => service.Id == "web");
		var binding = application.Bindings.Single(binding => binding.Owner == "application");
		var connection = binding.Connections[0];

		switch(failure)
		{
			case "reference":
				nginx.Web[0].Bindings[0].Publication = "unknown";
				break;
			case "site":
				nginx.Web.Add(nginx.Web[0]);
				break;
			case "default":
				nginx.Web[0].Bindings[0].IsDefault = false;
				break;
			case "binding":
				nginx.Web[0].Bindings.Add(nginx.Web[0].Bindings[0]);
				break;
			case "port":
				nginx.Ports.Add(nginx.Ports[0]);
				break;
			case "application":
				nginx.Web[0].Application = "unknown";
				break;
			case "port-application":
				nginx.Ports[0].Bindings[0].Application = "unknown";
				break;
			case "port-binding":
				nginx.Ports[0].Bindings[0].Binding = binding.Id;
				break;
			case "port-endpoint":
				nginx.Ports[0].Bindings[0].Endpoint = new string('f', 64);
				break;
			case "port-empty":
				nginx.Ports[0].Bindings.Clear();
				break;
			case "port-duplicate":
				nginx.Ports[0].Bindings.Add(nginx.Ports[0].Bindings[0]);
				break;
			case "connection-original":
				connection.Endpoint = new string('f', 64);
				break;
			case "connection-selected":
				connection.SelectedEndpoint = new string('f', 64);
				break;
			case "connection-host":
				connection.Host = "127.0.0.1";
				break;
			case "connection-port":
				connection.Port++;
				break;
			case "connection-protocol":
				connection.Protocol = "http3";
				break;
			case "listener-binding":
				nginx.Web[0].Bindings[0].Binding = binding.Id;
				break;
			case "listener-endpoint":
				nginx.Web[0].Bindings[0].Endpoint = new string('f', 64);
				break;
			case "listener-protocol":
				nginx.Web[0].Bindings[0].Protocol = "udp";
				break;
			case "publication-none":
				application.Bindings.Single(binding => binding.Owner == "nginx").Publication = "none";
				break;
			case "publication-pending":
				binding.Publication = "pending";
				break;
			case "declaration-duplicate":
				application.Bindings.Add(binding);
				break;
			case "is-proxied":
				binding.IsProxied = false;
				break;
			case "null-declaration":
				application.Bindings.Add(null);
				break;
			case "null-endpoint":
				binding.Endpoints.Add(null);
				break;
			case "null-connection":
				binding.Connections.Add(null);
				break;
			case "null-reference":
				nginx.Ports[0].Bindings.Add(null);
				break;
		}

		Assert.Equal(4, Assert.Throws<ContainerizationException>(() => ContainerBundle.Open(this.WriteBundle(services))).Code);
	}

	[Fact]
	public void GrpcTemplateReplacesTheWholeAuthorityAndPreservesTlsAndHost()
	{
		var application = BindingDocument.CreateBinding("application", ["rpc"], [new("https", "127.0.0.1", 4317, "/", ["grpc+http2"]), new("https", "0.0.0.0", 4318, "/", ["grpc+http2"])]);
		var declared = application.Endpoints.Single(endpoint => endpoint.Host == "127.0.0.1");
		var listener = BindingDocument.CreateBinding("nginx", [], [new("https", "0.0.0.0", 443, "/", ["http2"])]);
		var document = new BindingDocument
		{
			Bindings = [application, listener],
			Sites = [new("site:rpc", "rpc", ["rpc.example.test"], [new(listener.Id, listener.Endpoints[0].Id, false)],
				[new("grpc", "/", new() { Kind = "binding", Binding = application.Id, Endpoint = declared.Id }, "grpc+http2", new("https", "127.0.0.1", 4317, "/"), "tenant.example.test", new(true, "backend.example.test"))])],
		};
		var template = "grpc_pass grpcs://{{zongsoft:binding:" + application.Id + ":" + declared.Id + "}};\ngrpc_set_header Host tenant.example.test;\ngrpc_ssl_verify on;\ngrpc_ssl_name backend.example.test;\ngrpc_ssl_trusted_certificate /etc/pki/tls/certs/ca-bundle.crt;";
		var sources = ServicePlanner.Prepare(this.CreateManifest(document, template: template));
		WebIngress.Render(sources, _root);
		var nginx = sources.Single(source => source.Plan.Id == "nginx");
		var text = File.ReadAllText(nginx.Configuration["/etc/nginx/containerizer/web.conf"].Source);

		Assert.Equal("grpc_pass grpcs://web:4318;\ngrpc_set_header Host tenant.example.test;\ngrpc_ssl_verify on;\ngrpc_ssl_name backend.example.test;\ngrpc_ssl_trusted_certificate /etc/ssl/certs/ca-certificates.crt;".ReplaceLineEndings("\r\n"), text);
		Assert.DoesNotContain("{{zongsoft:", text);
		var route = Assert.Single(Assert.Single(sources.Single(source => source.Plan.Id == "web").Package.Bindings.Sites).Routes);
		Assert.Equal("tenant.example.test", route.Host);
		Assert.True(route.Tls.Verify);
		Assert.Equal("backend.example.test", route.Tls.Name);
		using var bundle = ContainerBundle.Open(this.WriteBundle(sources.Select(source => source.Plan).ToList()));
		Assert.Equal(4318, Assert.Single(bundle.Plan.Services.Single(service => service.Id == "web").Bindings.Single(binding => binding.Owner == "application").Connections).Port);
	}

	[Theory]
	[InlineData(false)]
	[InlineData(true)]
	public void QuicIngressKeepsUdpPublicationWithoutAddingTcpProbeHosts(bool includeTcp)
	{
		var application = BindingDocument.CreateBinding("application", ["web"], [new("http", "0.0.0.0", 8069, "/", ["http1"])]);
		var udp = BindingDocument.CreateBinding("nginx", [], [new("https", "0.0.0.0", 443, "/", ["http3"])]);
		var tcp = BindingDocument.CreateBinding("nginx", [], [new("https", "0.0.0.0", 443, "/", ["http1"])]);
		var document = new BindingDocument
		{
			Bindings = includeTcp ? [application, udp, tcp] : [application, udp],
			Sites = [new("site:api", "api", includeTcp ? ["api.example.test"] : ["*.example.test"],
				includeTcp ? [new(udp.Id, udp.Endpoints[0].Id, false), new(tcp.Id, tcp.Endpoints[0].Id, false)] : [new(udp.Id, udp.Endpoints[0].Id, false)],
				[new("http", "/", new() { Kind = "binding", Binding = application.Id, Endpoint = application.Endpoints[0].Id }, "http1", new("http", "127.0.0.1", 8069, "/"))])],
		};
		var sources = ServicePlanner.Prepare(this.CreateManifest(document));
		var nginx = sources.Single(source => source.Plan.Id == "nginx");
		var publication = Assert.Single(nginx.Plan.Ports, port => port.Protocol == "udp");

		Assert.Equal(443, publication.Host);
		Assert.Equal(443, publication.Container);
		Assert.Equal("127.0.0.1", publication.Address);
		Assert.Equal(udp.Id, Assert.Single(publication.Bindings).Binding);
		Assert.Equal(includeTcp ? 2 : 1, nginx.Plan.Ports.Count);
		Assert.Equal(includeTcp ? ["api.example.test"] : Array.Empty<string>(), Assert.Single(nginx.Plan.Web).ProbeHosts);
		using var bundle = ContainerBundle.Open(this.WriteBundle(sources.Select(source => source.Plan).ToList()));
		Assert.Contains(bundle.Plan.Services.Single(service => service.Id == "nginx").Web[0].Bindings, binding => binding.Protocol == "udp" && binding.Publication == publication.Name);
	}

	[Theory]
	[InlineData("server {}")]
	[InlineData("{{zongsoft:application}}")]
	[InlineData("{{zongsoft:binding:missing:missing}}")]
	public void RemovedOrUnknownApplicationMarkersFailBeforeRendering(string template)
	{
		var document = WebFixtures.CreateDocument(new WebFixtures.Site("api", ["http://0.0.0.0:80"]));
		Assert.Equal(2, Assert.Throws<ContainerizationException>(() => ServicePlanner.Prepare(this.CreateManifest(document, template: template))).Code);
	}

	private string WriteBundle(List<ServicePlan> services)
	{
		const string NAME = "web-test";
		var directory = Path.Combine(_root, "bundle");
		var plan = new ContainerBundlePlan
		{
			Name = NAME,
			Architecture = "x64",
			Distribution = "debian@13",
			Project = $"containerizer-{NAME}-{Files.HashText(NAME)[..8]}",
			DataRoot = Installation.Paths.GetDataPath(NAME),
			Services = services,
		};
		var files = new List<string> { "containerizer", "compose.yaml", "install.sh", "uninstall.sh" };

		foreach(var service in services)
		{
			service.Image = new()
			{
				Id = $"sha256:{new string('a', 64)}",
				Platform = "linux/amd64",
				Mode = "offline",
				Reference = $"containerizer/{plan.Project}/{service.Id}:fixture",
				Archive = $"images/{service.Id}.tar",
			};
			files.Add(service.Image.Archive);
		}

		foreach(var file in files)
		{
			var path = Path.Combine(directory, file);
			Files.Write(path, "fixture");
			plan.Files.Add(new() { Path = file, Length = new FileInfo(path).Length, Hash = Files.Hash(path) });
		}

		Files.Save(Path.Combine(directory, ContainerBundlePlan.FileName), plan, ProtocolJson.Default.ContainerBundlePlan);
		Files.Write(Path.Combine(directory, "checksums.sha256"), $"{Files.Hash(Path.Combine(directory, ContainerBundlePlan.FileName))}  {ContainerBundlePlan.FileName}\n{string.Join('\n', plan.Files.Select(file => $"{file.Hash}  {file.Path}"))}\n");
		return directory;
	}

	private ContainerManifest CreateManifest(BindingDocument bindings, string settings = null, string template = null, Dictionary<string, string> files = null)
	{
		var path = WebFixtures.WritePackage(Path.Combine(_root, "web.tar.gz"), "web", bindings, template ?? WebFixtures.Template(bindings), files);
		var manifest = ManifestFactory.Create(CreateContext(path, "--source:" + _root, "--name:example", "--version:1.0", "--distribution:debian"));
		if(settings != null)
			manifest.Components.Single(component => component.Name == "nginx")["settings"] = settings;
		return manifest;
	}

	private static CommandContext CreateContext(params string[] arguments) => new(new CommandExecutor(), CommandLine.Parse(Utility.FormatCommand("containerize", arguments.Select(argument => argument.Replace('\\', '/')).ToArray()))[0], new ContainerizeCommand(), null);
}
