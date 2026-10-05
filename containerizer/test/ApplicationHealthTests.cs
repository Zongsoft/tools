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
		var (package, source) = Create(listen);
		ApplicationImageBuilder.ResolveEntry(package, source);
		Assert.Equal(["dotnet", "/opt/example/example.dll", "--urls", binding], source.Plan.Entrypoint);
		Assert.Equal("CMD-SHELL", source.Plan.Health.Test[0]);
		Assert.Contains("'" + probe + "'", source.Plan.Health.Test[1]);
		Assert.DoesNotContain("--fail", source.Plan.Health.Test[1]);
		Assert.Equal(8069, Assert.Single(source.Plan.Ports).Host);
		Assert.True(source.HealthUsesCurl);
	}

	[Fact]
	public void MultipleListenersPreferFirstHttpWithoutFollowingRedirects()
	{
		var (package, source) = Create("https://example.test:8443; http://localhost:8069;http://0.0.0.0:8070");
		ApplicationImageBuilder.ResolveEntry(package, source);
		Assert.Equal("https://0.0.0.0:8443;http://0.0.0.0:8069;http://0.0.0.0:8070", source.Plan.Entrypoint.Last());
		Assert.Contains("'http://127.0.0.1:8069/'", source.Plan.Health.Test[1]);
		Assert.Contains("--header 'Host: localhost:8069'", source.Plan.Health.Test[1]);
		Assert.DoesNotContain("--location", source.Plan.Health.Test[1]);
		Assert.DoesNotContain("kill -0", source.Plan.Health.Test[1]);
	}

	[Fact]
	public void HttpsUsesPackageDnsNameWithoutDisablingCertificateVerification()
	{
		var (package, source) = Create("https://example.test:8443");
		ApplicationImageBuilder.ResolveEntry(package, source);
		Assert.Contains("--resolve 'example.test:8443:127.0.0.1'", source.Plan.Health.Test[1]);
		Assert.Contains("'https://example.test:8443/'", source.Plan.Health.Test[1]);
		Assert.DoesNotContain("--insecure", source.Plan.Health.Test[1]);
		Assert.DoesNotContain("kill -0", source.Plan.Health.Test[1]);
	}

	[Fact]
	public void HttpsDnsListenerSuppliesTlsName()
	{
		var (package, source) = Create("https://example.test:8443");
		ApplicationImageBuilder.ResolveEntry(package, source);
		Assert.Contains("--resolve 'example.test:8443:127.0.0.1'", source.Plan.Health.Test[1]);
	}

	[Theory]
	[InlineData("https://127.0.0.1:8443")]
	[InlineData("https://0.0.0.0:8443")]
	public void HttpsIpListenerCannotSupplyDnsIdentity(string listen)
	{
		var (package, source) = Create(listen);
		Assert.Equal(2, Assert.Throws<ContainerizationException>(() => ApplicationImageBuilder.ResolveEntry(package, source)).Code);
		Assert.Null(source.Plan.Health.Test);
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
		var (package, source) = Create(listen);
		Assert.Equal(2, Assert.Throws<ContainerizationException>(() => ApplicationImageBuilder.ResolveEntry(package, source)).Code);
		Assert.Null(source.Plan.Health.Test);
	}

	[Theory]
	[InlineData(null)]
	[InlineData("")]
	public void MissingOrEmptyMetadataUsesProcessLiveness(string listen)
	{
		var (package, source) = Create(listen);
		ApplicationImageBuilder.ResolveEntry(package, source);
		Assert.Equal(["CMD-SHELL", "kill -0 1"], source.Plan.Health.Test);
		Assert.Equal(["dotnet", "/opt/example/example.dll"], source.Plan.Entrypoint);
	}

	[Theory]
	[InlineData("debian@13", "apt-get")]
	[InlineData("ubuntu@22.04", "apt-get")]
	[InlineData("rocky@9", "dnf")]
	public void ProbeDependenciesAreInstalledEvenForSelfContainedHosts(string distribution, string installer)
	{
		Assert.Contains(installer, ApplicationImageBuilder.ProbeInstall(distribution, true));
		Assert.Contains("ca-certificates", ApplicationImageBuilder.ProbeInstall(distribution, true));
		Assert.Contains("curl", ApplicationImageBuilder.ProbeInstall(distribution, true));
		Assert.Empty(ApplicationImageBuilder.ProbeInstall(distribution, false));
	}
	#endregion

	#region 辅助方法
	private static (PackageReader.Descriptor, ServiceBuildContext) Create(string listen)
	{
		var package = new PackageReader.Descriptor { Name = "example", Listen = listen };
		package.Texts["example.service"] = "[Service]\nWorkingDirectory=/opt/example\nExecStart=dotnet /opt/example/example.dll" + (string.IsNullOrEmpty(listen) ? "" : " --urls " + listen) + "\n";
		return (package, new() { Plan = new() { Id = "example", Kind = "application" } });
	}
	#endregion
}
