using System;
using System.Linq;

using Xunit;

using Zongsoft.Tools.Containerizer.Protocol;

namespace Zongsoft.Tools.Containerizer.Tests;

public sealed class ApplicationHealthTests
{
	#region 监听与探测
	[Theory]
	[InlineData("http://127.0.0.1:8069", "http://0.0.0.0:8069", "http://127.0.0.1:8069/")]
	[InlineData("http://localhost:8069", "http://0.0.0.0:8069", "http://127.0.0.1:8069/")]
	[InlineData("http://*:8069", "http://0.0.0.0:8069", "http://127.0.0.1:8069/")]
	[InlineData("http://+:8069", "http://0.0.0.0:8069", "http://127.0.0.1:8069/")]
	[InlineData("http://[::1]:8069", "http://[::]:8069", "http://[::1]:8069/")]
	[InlineData("http://[::]:8069", "http://[::]:8069", "http://[::1]:8069/")]
	public void ListenerBindsAllInterfacesAndProbesLoopback(string listen, string binding, string probe)
	{
		var component = Create(listen);
		var source = ApplicationPlanner.Create(component, new());
		Assert.Equal(["dotnet", "/opt/example/example.dll", "--urls", binding], source.Plan.Entrypoint);
		Assert.Equal("CMD-SHELL", source.Plan.Health.Test[0]);
		Assert.Contains("'" + probe + "'", source.Plan.Health.Test[1]);
		Assert.DoesNotContain("--fail", source.Plan.Health.Test[1]);
		Assert.Equal(8069, Assert.Single(source.Plan.Ports).Host);
	}

	[Fact]
	public void MultipleListenersPreferFirstHttpWithoutFollowingRedirects()
	{
		var component = Create("https://example.test:8443; http://localhost:8069;http://0.0.0.0:8070");
		var source = ApplicationPlanner.Create(component, new());
		Assert.Equal("https://0.0.0.0:8443;http://0.0.0.0:8069;http://0.0.0.0:8070", source.Plan.Entrypoint.Last());
		Assert.Contains("'http://127.0.0.1:8069/'", source.Plan.Health.Test[1]);
		Assert.Contains("--header 'Host: localhost:8069'", source.Plan.Health.Test[1]);
		Assert.DoesNotContain("--location", source.Plan.Health.Test[1]);
		Assert.DoesNotContain("kill -0", source.Plan.Health.Test[1]);
	}

	[Fact]
	public void HttpsUsesPackageDnsNameWithoutDisablingCertificateVerification()
	{
		var component = Create("https://example.test:8443");
		var source = ApplicationPlanner.Create(component, new());
		Assert.Contains("--resolve 'example.test:8443:127.0.0.1'", source.Plan.Health.Test[1]);
		Assert.Contains("'https://example.test:8443/'", source.Plan.Health.Test[1]);
		Assert.DoesNotContain("--insecure", source.Plan.Health.Test[1]);
		Assert.DoesNotContain("kill -0", source.Plan.Health.Test[1]);
	}

	[Fact]
	public void HttpsDnsListenerSuppliesTlsName()
	{
		var component = Create("https://example.test:8443");
		var source = ApplicationPlanner.Create(component, new());
		Assert.Contains("--resolve 'example.test:8443:127.0.0.1'", source.Plan.Health.Test[1]);
	}

	[Theory]
	[InlineData("https://127.0.0.1:8443")]
	[InlineData("https://0.0.0.0:8443")]
	public void HttpsIpListenerCannotSupplyDnsIdentity(string listen)
	{
		var component = Create(listen);
		Assert.Equal(2, Assert.Throws<ContainerizationException>(() => ApplicationPlanner.Create(component, new())).Code);
	}

	[Theory]
	[InlineData("ftp://localhost:8069")]
	[InlineData("http://user:password@localhost:8069")]
	[InlineData("http://localhost:0")]
	[InlineData("http://localhost:8069/health")]
	[InlineData("http://localhost:8069?q=1")]
	[InlineData("http://localhost:8069#fragment")]
	[InlineData("http://*evil:8069")]
	[InlineData("http://localhost:8069;")]
	[InlineData("8069")]
	[InlineData("http://192.0.2.1:8069")]
	public void InvalidListenersFailWithoutProcessFallback(string listen)
	{
		var component = Create(listen);
		Assert.Equal(2, Assert.Throws<ContainerizationException>(() => ApplicationPlanner.Create(component, new())).Code);
	}

	[Theory]
	[InlineData(null)]
	[InlineData("")]
	public void MissingOrEmptyMetadataUsesProcessLiveness(string listen)
	{
		var component = Create(listen);
		var source = ApplicationPlanner.Create(component, new());
		Assert.Equal(["CMD-SHELL", "kill -0 1"], source.Plan.Health.Test);
		Assert.Equal(["dotnet", "/opt/example/example.dll"], source.Plan.Entrypoint);
	}

	#endregion

	#region 辅助方法
	private static ContainerManifest.Component Create(string listen)
	{
		var package = new PackageReader.Descriptor { Name = "example", ListenerAddresses = listen };
		package.Texts["example.service"] = "[Service]\nWorkingDirectory=/opt/example\nExecStart=dotnet /opt/example/example.dll" + (string.IsNullOrEmpty(listen) ? "" : " --urls " + listen) + "\n";
		return new(new PackageReader.Candidate("example.tar.gz", package));
	}
	#endregion
}
