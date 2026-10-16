using System;
using System.Text.Json;

using Xunit;
using Zongsoft.Tools.Containerizer.Protocol;

namespace Zongsoft.Tools.Containerizer.Executor.Tests;

public sealed class BindingProtocolTests
{
	[Fact]
	public void JsonRoundtripPreservesDeclarationsSelectionsAndPublicationReferences()
	{
		var bindingId = new string('a', 64);
		var originalId = new string('b', 64);
		var selectedId = new string('c', 64);
		var service = new ServicePlan
		{
			Id = "example",
			Kind = "application",
			Bindings =
			[
				new()
				{
					Id = bindingId,
					Owner = "application",
					Aliases = ["api", "web"],
					IsProxied = true,
					Publication = "explicit",
					Endpoints =
					[
						new() { Id = originalId, Scheme = "https", Host = "127.0.0.1", Port = 4317, Path = "/opentelemetry", Protocols = ["grpc+http2"], Transports = ["tcp"] },
						new() { Id = selectedId, Scheme = "https", Host = "0.0.0.0", Port = 4318, Path = "/opentelemetry", Protocols = ["grpc+http2"], Transports = ["tcp"] },
					],
					Connections = [new() { Endpoint = originalId, SelectedEndpoint = selectedId, Host = "example", Port = 4318, Protocol = "grpc+http2" }],
				},
			],
			Ports = [new() { Name = "binding-0", Address = "::1", Host = 14318, Container = 4318, Protocol = "tcp", Bindings = [new() { Application = "example", Binding = bindingId, Endpoint = selectedId }] }],
		};
		var json = JsonSerializer.Serialize(service, ProtocolJson.Default.ServicePlan);
		var restored = JsonSerializer.Deserialize(json, ProtocolJson.Default.ServicePlan);
		var declaration = Assert.Single(restored.Bindings);

		Assert.Equal(bindingId, declaration.Id);
		Assert.Equal("application", declaration.Owner);
		Assert.Equal(["api", "web"], declaration.Aliases);
		Assert.True(declaration.IsProxied);
		Assert.Equal("explicit", declaration.Publication);
		Assert.Equal(2, declaration.Endpoints.Count);
		Assert.Equal("127.0.0.1", declaration.Endpoints[0].Host);
		Assert.Equal("0.0.0.0", declaration.Endpoints[1].Host);
		Assert.Equal("/opentelemetry", declaration.Endpoints[1].Path);
		Assert.Equal(["grpc+http2"], declaration.Endpoints[1].Protocols);
		Assert.Equal(["tcp"], declaration.Endpoints[1].Transports);
		var connection = Assert.Single(declaration.Connections);
		Assert.Equal(originalId, connection.Endpoint);
		Assert.Equal(selectedId, connection.SelectedEndpoint);
		Assert.Equal("example", connection.Host);
		Assert.Equal(4318, connection.Port);
		Assert.Equal("grpc+http2", connection.Protocol);
		var port = Assert.Single(restored.Ports);
		Assert.Equal("::1", port.Address);
		Assert.Equal(14318, port.Host);
		Assert.Equal(4318, port.Container);
		Assert.Equal("tcp", port.Protocol);
		var reference = Assert.Single(port.Bindings);
		Assert.Equal("example", reference.Application);
		Assert.Equal(bindingId, reference.Binding);
		Assert.Equal(selectedId, reference.Endpoint);
		Assert.Equal(json, JsonSerializer.Serialize(restored, ProtocolJson.Default.ServicePlan));
	}

	[Fact]
	public void JsonRoundtripKeepsUdpWebBindingAndItsPublicationIdentity()
	{
		var service = new ServicePlan { Id = "nginx", Kind = "ingress", Web = [new() { Application = "example", Name = "api", Hosts = ["api.example.test"], Bindings = [new() { Scheme = "https", Address = "0.0.0.0", Port = 443, Protocol = "udp", Binding = new string('a', 64), Endpoint = new string('b', 64), Publication = "web-udp-443", IsDefault = true, IsExplicitDefault = true }] }] };
		var json = JsonSerializer.Serialize(service, ProtocolJson.Default.ServicePlan);
		var binding = Assert.Single(Assert.Single(JsonSerializer.Deserialize(json, ProtocolJson.Default.ServicePlan).Web).Bindings);

		Assert.Equal("udp", binding.Protocol);
		Assert.Equal("https", binding.Scheme);
		Assert.Equal("0.0.0.0", binding.Address);
		Assert.Equal(443, binding.Port);
		Assert.Equal(new string('a', 64), binding.Binding);
		Assert.Equal(new string('b', 64), binding.Endpoint);
		Assert.Equal("web-udp-443", binding.Publication);
		Assert.True(binding.IsDefault);
		Assert.True(binding.IsExplicitDefault);
	}

	[Theory]
	[InlineData("{\"bindings\":[{\"listenerAddress\":\"http://0.0.0.0:80\"}]}")]
	[InlineData("{\"bindings\":[{\"endpoints\":[{\"addressFamily\":\"IPv4\"}]}]}")]
	[InlineData("{\"bindings\":[{\"connections\":[{\"futureProtocol\":true}]}]}")]
	[InlineData("{\"ports\":[{\"bindings\":[{\"alias\":\"web\"}]}]}")]
	public void UnknownNestedBindingFieldsAreRejected(string json)
	{
		Assert.Throws<JsonException>(() => JsonSerializer.Deserialize(json, ProtocolJson.Default.ServicePlan));
	}
}
