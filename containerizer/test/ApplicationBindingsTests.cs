using System;
using System.Linq;

using Xunit;
using Zongsoft.Tools.Containerizer.Protocol;

namespace Zongsoft.Tools.Containerizer.Tests;

public sealed class ApplicationBindingsTests
{
	[Theory]
	[InlineData("tcp")]
	[InlineData("udp")]
	public void RawTransportsPublishTheDeclaredPortAndReference(string transport)
	{
		var binding = BindingDocument.CreateBinding("application", ["raw"], [new(transport, "0.0.0.0", 9000, "/", [])]);
		var source = Create(new() { Bindings = [binding] });
		var port = Assert.Single(source.Plan.Ports);

		Assert.Equal("127.0.0.1", port.Address);
		Assert.Equal(9000, port.Host);
		Assert.Equal(9000, port.Container);
		Assert.Equal(transport, port.Protocol);
		var reference = Assert.Single(port.Bindings);
		Assert.Equal("example", reference.Application);
		Assert.Equal(binding.Id, reference.Binding);
		Assert.Equal(binding.Endpoints[0].Id, reference.Endpoint);
		var declaration = Assert.Single(source.Plan.Bindings);
		Assert.Equal("auto", declaration.Publication);
		Assert.False(declaration.IsProxied);
		Assert.Equal([transport], Assert.Single(declaration.Endpoints).Transports);
		Assert.Empty(declaration.Connections);
	}

	[Fact]
	public void MixedHttpVersionsPublishTcpAndUdpOnTheSameNumber()
	{
		var binding = BindingDocument.CreateBinding("application", ["web"], [new("https", "0.0.0.0", 8443, "/", ["http1", "http3"])]);
		var source = Create(new() { Bindings = [binding] });

		Assert.Equal(["tcp", "udp"], source.Plan.Ports.Select(port => port.Protocol));
		Assert.All(source.Plan.Ports, port =>
		{
			Assert.Equal(8443, port.Host);
			Assert.Equal(8443, port.Container);
			Assert.Equal(binding.Id, Assert.Single(port.Bindings).Binding);
		});
		ServicePlanner.Validate([source]);
	}

	[Theory]
	[InlineData("auto")]
	[InlineData("none")]
	public void ProxiedBindingRetainsConnectionWithoutDirectPublication(string publication)
	{
		var application = BindingDocument.CreateBinding("application", ["web"], [new("http", "0.0.0.0", 8069, "/", ["http1"])]);
		var source = Create(Proxy(application, application.Endpoints[0]), ("publish!web", publication));
		var declaration = source.Plan.Bindings.Single(binding => binding.Owner == "application");

		Assert.Empty(source.Plan.Ports);
		Assert.True(declaration.IsProxied);
		Assert.Equal(publication, declaration.Publication);
		var connection = Assert.Single(declaration.Connections);
		Assert.Equal(application.Endpoints[0].Id, connection.Endpoint);
		Assert.Equal(connection.Endpoint, connection.SelectedEndpoint);
		Assert.Equal("example", connection.Host);
		Assert.Equal(8069, connection.Port);
		Assert.Equal("http1", connection.Protocol);
	}

	[Theory]
	[InlineData(false)]
	[InlineData(true)]
	public void IdentityAndAliasSelectTheSameBindingWithoutChangingTheDeclaredPort(bool useIdentity)
	{
		var binding = BindingDocument.CreateBinding("application", ["public"], [new("tcp", "0.0.0.0", 9000, "/", [])]);
		var source = Create(new() { Bindings = [binding] }, ("publish!" + (useIdentity ? binding.Id.ToUpperInvariant() : "PUBLIC"), "tcp://192.0.2.20:19000"));
		var port = Assert.Single(source.Plan.Ports);

		Assert.Equal("192.0.2.20", port.Address);
		Assert.Equal(19000, port.Host);
		Assert.Equal(9000, port.Container);
		Assert.Equal("explicit", Assert.Single(source.Plan.Bindings).Publication);
		Assert.Equal(9000, Assert.Single(Assert.Single(source.Plan.Bindings).Endpoints).Port);
	}

	[Theory]
	[InlineData("missing")]
	[InlineData("ambiguous")]
	[InlineData("duplicate")]
	[InlineData("empty")]
	public void InvalidSelectorsFailBeforeProducingPublication(string failure)
	{
		var first = BindingDocument.CreateBinding("application", ["shared"], [new("tcp", "0.0.0.0", 9000, "/", [])]);
		var second = BindingDocument.CreateBinding("application", ["shared"], [new("tcp", "0.0.0.0", 9001, "/", [])]);
		var document = new BindingDocument { Bindings = failure == "ambiguous" ? [first, second] : [first] };
		var overrides = failure switch
		{
			"missing" => new[] { ("publish!absent", "none") },
			"empty" => [("publish!", "none")],
			"duplicate" => [("publish!shared", "none"), ("publish!" + first.Id, "auto")],
			_ => [("publish!shared", "none")],
		};

		Assert.Equal(2, Assert.Throws<ContainerizationException>(() => Create(document, overrides)).Code);
	}

	[Fact]
	public void ExplicitPublicationCanUseSeveralAddressesAndBothTransports()
	{
		var binding = BindingDocument.CreateBinding("application", ["web"], [new("https", "0.0.0.0", 8443, "/", ["http1", "http3"])]);
		var source = Create(new() { Bindings = [binding] }, ("publish!web", "tcp://127.0.0.1:18443;tcp://[::1]:18443;udp://127.0.0.1:18443"));

		Assert.Equal(new[] { ("127.0.0.1", "tcp"), ("::1", "tcp"), ("127.0.0.1", "udp") }, source.Plan.Ports.Select(port => (port.Address, port.Protocol)));
		Assert.All(source.Plan.Ports, port =>
		{
			Assert.Equal(18443, port.Host);
			Assert.Equal(8443, port.Container);
			Assert.Equal(binding.Endpoints[0].Id, Assert.Single(port.Bindings).Endpoint);
		});
		ServicePlanner.Validate([source]);
	}

	[Fact]
	public void UnresolvedTransportRequiresAnExplicitDecisionToSuppressPublication()
	{
		var binding = BindingDocument.CreateBinding("application", ["custom"], [new("http", "0.0.0.0", 9000, "/", [])]);
		Assert.Equal(2, Assert.Throws<ContainerizationException>(() => Create(new() { Bindings = [binding] })).Code);
		var source = Create(new() { Bindings = [binding] }, ("publish!custom", "none"));
		Assert.Empty(source.Plan.Ports);
		Assert.Empty(Assert.Single(Assert.Single(source.Plan.Bindings).Endpoints).Transports);
	}

	[Theory]
	[InlineData(false)]
	[InlineData(true)]
	public void PublicationVariablesResolveWhilePlanningPreservesTheDraftExpression(bool planning)
	{
		var binding = BindingDocument.CreateBinding("application", ["raw"], [new("tcp", "0.0.0.0", 9000, "/", [])]);
		var manifest = new ContainerManifest(planning);
		manifest.Evaluator.SetVariable("binding_test_port", "19000");
		const string EXPRESSION = "tcp://127.0.0.1:${binding_test_port}";
		var source = Create(new() { Bindings = [binding] }, manifest, ("publish!raw", EXPRESSION));

		Assert.Equal(19000, Assert.Single(source.Plan.Ports).Host);
		Assert.Equal(9000, source.Plan.Ports[0].Container);
		Assert.Equal(planning ? EXPRESSION : "tcp://127.0.0.1:19000", source.Component["publish!raw"]);
		Assert.Empty(source.MissingSettings);
		Assert.Equal("explicit", Assert.Single(source.Plan.Bindings).Publication);
	}

	[Fact]
	public void MissingPublicationVariablesRemainPendingOnlyInAPlan()
	{
		var binding = BindingDocument.CreateBinding("application", ["raw"], [new("tcp", "0.0.0.0", 9000, "/", [])]);
		const string EXPRESSION = "tcp://127.0.0.1:${binding_test_missing_port}";
		var source = Create(new() { Bindings = [binding] }, new ContainerManifest(true), ("publish!raw", EXPRESSION));

		Assert.Equal("pending", Assert.Single(source.Plan.Bindings).Publication);
		Assert.Empty(source.Plan.Ports);
		Assert.Equal(["publish!raw"], source.MissingSettings);
		Assert.Equal(EXPRESSION, source.Component["publish!raw"]);
		Assert.Equal(2, Assert.Throws<ContainerizationException>(() => Create(new() { Bindings = [binding] }, ("publish!raw", EXPRESSION))).Code);
	}

	[Theory]
	[InlineData("tcp://example.test:19000")]
	[InlineData("tcp://127.0.0.1")]
	[InlineData("tcp://127.0.0.1:0")]
	[InlineData("tcp://127.0.0.1:65536")]
	[InlineData("tcp://127.0.0.1:19000/path")]
	[InlineData("tcp://127.0.0.1:19000/")]
	[InlineData("tcp://127.0.0.1:19000?query=1")]
	[InlineData("tcp://[fe80::1%3]:19000")]
	[InlineData("udp://127.0.0.1:19000")]
	[InlineData("http://127.0.0.1:19000")]
	[InlineData("tcp://127.0.0.1:19000;")]
	[InlineData("tcp://127.0.0.1:19000;tcp://127.0.0.1:19000")]
	[InlineData("tcp://0.0.0.0:19000;tcp://127.0.0.1:19000")]
	[InlineData("AUTO")]
	[InlineData("")]
	public void InvalidExplicitPublicationFails(string publication)
	{
		var binding = BindingDocument.CreateBinding("application", ["raw"], [new("tcp", "0.0.0.0", 9000, "/", [])]);
		Assert.Equal(2, Assert.Throws<ContainerizationException>(() => Create(new() { Bindings = [binding] }, ("publish!raw", publication))).Code);
	}

	[Theory]
	[InlineData("127.0.0.1")]
	[InlineData("::")]
	[InlineData("::1")]
	[InlineData("backend.example.test")]
	[InlineData("192.0.2.10")]
	public void UnreachableApplicationAddressesRequireNone(string address)
	{
		var binding = BindingDocument.CreateBinding("application", ["web"], [new("http", address, 8069, "/", ["http1"])]);
		var document = new BindingDocument { Bindings = [binding] };
		Assert.Equal(2, Assert.Throws<ContainerizationException>(() => Create(document)).Code);
		var source = Create(document, ("publish!web", "none"));
		Assert.Empty(source.Plan.Ports);
		Assert.Equal(address, Assert.Single(Assert.Single(source.Plan.Bindings).Endpoints).Host);
		Assert.Equal("none", Assert.Single(source.Plan.Bindings).Publication);
		Assert.Equal(2, Assert.Throws<ContainerizationException>(() => Create(Proxy(binding, binding.Endpoints[0]), ("publish!web", "none"))).Code);
	}

	[Fact]
	public void ProxySelectsReachableAlternativeWithoutRewritingStartupOrEnvironment()
	{
		var application = BindingDocument.CreateBinding("application", ["web"], [new("http", "127.0.0.1", 8069, "/", ["http1"]), new("http", "0.0.0.0", 8070, "/", ["http1"])]);
		var declared = application.Endpoints.Single(endpoint => endpoint.Host == "127.0.0.1");
		var selected = application.Endpoints.Single(endpoint => endpoint.Host == "0.0.0.0");
		var source = Create(Proxy(application, declared));
		var declaration = source.Plan.Bindings.Single(binding => binding.Owner == "application");
		var connection = Assert.Single(declaration.Connections);

		Assert.Equal(declared.Id, connection.Endpoint);
		Assert.Equal(selected.Id, connection.SelectedEndpoint);
		Assert.Equal(8070, connection.Port);
		Assert.Equal("example", connection.Host);
		Assert.Equal(2, declaration.Endpoints.Count);
		Assert.Equal(["dotnet", "/opt/example/example.dll", "--urls", "http://127.0.0.1:8069"], source.Plan.Entrypoint);
		Assert.Equal("http://127.0.0.1:9000", source.Environment["ASPNETCORE_URLS"]);
		Assert.Equal(["CMD-SHELL", "kill -0 1"], source.Plan.Health.Test);
	}

	[Theory]
	[InlineData("/other", "http1")]
	[InlineData("/", "http2")]
	public void ProxyDoesNotSelectAnAlternativeWithADifferentPathOrProtocol(string path, string protocol)
	{
		var application = BindingDocument.CreateBinding("application", ["web"], [new("http", "127.0.0.1", 8069, "/", ["http1"]), new("http", "0.0.0.0", 8070, path, [protocol])]);
		Assert.Equal(2, Assert.Throws<ContainerizationException>(() => Create(Proxy(application, application.Endpoints.Single(endpoint => endpoint.Host == "127.0.0.1")))).Code);
	}

	[Theory]
	[InlineData("auto")]
	[InlineData("tcp://127.0.0.1:19000")]
	public void MultipleTargetPortsCannotBecomeOneDirectPublication(string publication)
	{
		var binding = BindingDocument.CreateBinding("application", ["raw"], [new("tcp", "0.0.0.0", 9000, "/", []), new("tcp", "0.0.0.0", 9001, "/", [])]);
		Assert.Equal(2, Assert.Throws<ContainerizationException>(() => Create(new() { Bindings = [binding] }, ("publish!raw", publication))).Code);
		Assert.Empty(Create(new() { Bindings = [binding] }, ("publish!raw", "none")).Plan.Ports);
	}

	[Fact]
	public void DistinctBindingsSharingTheSameSocketKeepBothReferences()
	{
		var first = BindingDocument.CreateBinding("application", ["first"], [new("http", "0.0.0.0", 9000, "/", ["http1"])]);
		var second = BindingDocument.CreateBinding("application", ["second"], [new("http", "0.0.0.0", 9000, "/metrics", ["http1"])]);
		var port = Assert.Single(Create(new() { Bindings = [first, second] }).Plan.Ports);

		Assert.Equal(2, port.Bindings.Count);
		Assert.Equal(new[] { first.Id, second.Id }.Order(), port.Bindings.Select(reference => reference.Binding).Order());
	}

	[Theory]
	[InlineData("127.0.0.1", "127.0.0.1", "tcp", true)]
	[InlineData("0.0.0.0", "127.0.0.1", "tcp", true)]
	[InlineData("127.0.0.1", "0.0.0.0", "tcp", true)]
	[InlineData("127.0.0.1", "127.0.0.2", "tcp", false)]
	[InlineData("0.0.0.0", "::", "tcp", false)]
	[InlineData("::", "::1", "tcp", true)]
	[InlineData("::1", "::", "tcp", true)]
	[InlineData("::1", "::2", "tcp", false)]
	[InlineData("127.0.0.1", "127.0.0.1", "udp", false)]
	public void ServiceSocketConflictsIncludeAddressFamilyAndTransport(string firstAddress, string secondAddress, string secondProtocol, bool conflict)
	{
		var first = new ServiceBuildContext(new() { Name = "first" }) { Plan = new() { Id = "first", Ports = [new() { Address = firstAddress, Host = 9000, Container = 9000, Protocol = "tcp" }] } };
		var second = new ServiceBuildContext(new() { Name = "second" }) { Plan = new() { Id = "second", Ports = [new() { Address = secondAddress, Host = 9000, Container = 9001, Protocol = secondProtocol }] } };

		if(conflict)
			Assert.Equal(2, Assert.Throws<ContainerizationException>(() => ServicePlanner.Validate([first, second])).Code);
		else
			ServicePlanner.Validate([first, second]);
	}

	private static ServiceBuildContext Create(BindingDocument document, params (string Key, string Value)[] overrides)
		=> Create(document, new ContainerManifest(), overrides);

	private static ServiceBuildContext Create(BindingDocument document, ContainerManifest manifest, params (string Key, string Value)[] overrides)
	{
		var package = new PackageReader.Descriptor { Name = "example", Bindings = BindingDocument.Deserialize(document.Serialize()) };
		package.Texts["example.service"] = "[Service]\nWorkingDirectory=/opt/example\nEnvironment=ASPNETCORE_URLS=http://127.0.0.1:9000\nExecStart=dotnet /opt/example/example.dll --urls http://127.0.0.1:8069\n";
		var component = new ContainerManifest.Component(new PackageReader.Candidate("example.tar.gz", package));

		foreach(var (key, value) in overrides)
			component[key] = value;

		return ApplicationPlanner.Create(component, manifest);
	}

	private static BindingDocument Proxy(BindingDocument.Binding application, BindingDocument.Endpoint endpoint)
	{
		var frontend = BindingDocument.CreateBinding("nginx", [], [new("http", "0.0.0.0", 80, "/", ["http1"])]);
		return new()
		{
			Bindings = [application, frontend],
			Sites = [new("site:api", "api", [], [new(frontend.Id, frontend.Endpoints[0].Id, false)],
				[new("http", "/", new() { Kind = "binding", Binding = application.Id, Endpoint = endpoint.Id }, "http1", new("http", "127.0.0.1", endpoint.Port, endpoint.Path))])],
		};
	}
}
