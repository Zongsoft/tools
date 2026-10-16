using System;
using System.Linq;

using Xunit;
using Zongsoft.Tools.Containerizer.Protocol;

namespace Zongsoft.Tools.Containerizer.Tests;

public sealed class ApplicationHealthTests
{
	[Theory]
	[InlineData(null)]
	[InlineData("http://127.0.0.1:8069")]
	[InlineData("https://example.test:8443")]
	[InlineData("http://[::1]:8069")]
	public void DefaultHealthPreservesConfiguredArgumentsAndEnvironment(string address)
	{
		var component = Create(address);
		var source = ApplicationPlanner.Create(component, new());

		Assert.Equal(["CMD-SHELL", "kill -0 1"], source.Plan.Health.Test);
		Assert.Equal(address == null ? ["dotnet", "/opt/example/example.dll"] : new[] { "dotnet", "/opt/example/example.dll", "--urls", address }, source.Plan.Entrypoint);
		Assert.Equal("http://127.0.0.1:9000", source.Environment["ASPNETCORE_URLS"]);
		Assert.Empty(source.Plan.Ports);
		Assert.Empty(source.Plan.Bindings);
	}

	[Theory]
	[InlineData("[\"CMD\",\"/opt/example/health\",\"--ready\"]", "CMD", "/opt/example/health", "--ready")]
	[InlineData("[\"CMD-SHELL\",\"test -f /tmp/ready\"]", "CMD-SHELL", "test -f /tmp/ready", null)]
	public void ExplicitJsonHealthReplacesOnlyTheHealthCommand(string json, string kind, string command, string argument)
	{
		var component = Create("http://127.0.0.1:8069");
		component["health"] = json;
		var source = ApplicationPlanner.Create(component, new());

		Assert.Equal(argument == null ? [kind, command] : new[] { kind, command, argument }, source.Plan.Health.Test);
		Assert.Equal("http://127.0.0.1:8069", source.Plan.Entrypoint.Last());
		Assert.Equal("http://127.0.0.1:9000", source.Environment["ASPNETCORE_URLS"]);
		Assert.Empty(source.Plan.Ports);
	}

	[Theory]
	[InlineData("")]
	[InlineData("null")]
	[InlineData("[]")]
	[InlineData("[\"CMD\"]")]
	[InlineData("[\"NONE\",\"true\"]")]
	[InlineData("[\"CMD\",null]")]
	[InlineData("[\"CMD\",\" \" ]")]
	[InlineData("[\"CMD-SHELL\",\"true\",\"extra\"]")]
	[InlineData("{\"command\":\"true\"}")]
	public void InvalidExplicitHealthFailsWithoutSilentFallback(string json)
	{
		var component = Create(null);
		component["health"] = json;

		Assert.Equal(2, Assert.Throws<ContainerizationException>(() => ApplicationPlanner.Create(component, new())).Code);
	}

	[Theory]
	[InlineData("health")]
	[InlineData("HEALTH")]
	[InlineData("Health")]
	public void ApplicationHealthOverrideIsRejectedOnInfrastructureRegardlessOfCase(string key)
	{
		var manifest = new ContainerManifest();
		var component = new ContainerManifest.Component { Name = "redis" };
		component[key] = "[\"CMD\",\"true\"]";
		manifest.Components.Add(component);

		Assert.Equal(2, Assert.Throws<ContainerizationException>(manifest.Normalize).Code);
	}

	private static ContainerManifest.Component Create(string address)
	{
		var package = new PackageReader.Descriptor { Name = "example", Bindings = new() };
		package.Texts["example.service"] = "[Service]\nWorkingDirectory=/opt/example\nEnvironment=ASPNETCORE_URLS=http://127.0.0.1:9000\nExecStart=dotnet /opt/example/example.dll" + (address == null ? "" : " --urls " + address) + "\n";
		return new(new PackageReader.Candidate("example.tar.gz", package));
	}
}
