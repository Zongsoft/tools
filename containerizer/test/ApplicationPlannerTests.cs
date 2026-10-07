using System;
using System.IO;
using System.Text.Json;

using Xunit;

using Zongsoft.Tools.Containerizer.Protocol;

namespace Zongsoft.Tools.Containerizer.Tests;

public sealed class ApplicationPlannerTests
{
	[Fact]
	public void InferredApplicationEntryUsesProcessHealthInCompose()
	{
		var package = new PackageReader.Descriptor { Name = "example" };
		package.Texts["example.service"] = "[Service]\nType=simple\nWorkingDirectory=/opt/example\nExecStart=dotnet /opt/example/example.dll\n";
		var source = ApplicationPlanner.Create(new(new PackageReader.Candidate("example.tar.gz", package)), new());
		Assert.Equal(["dotnet", "/opt/example/example.dll"], source.Plan.Entrypoint);

		var directory = Path.Combine(Path.GetTempPath(), "containerizer-health-" + Guid.NewGuid().ToString("N"));
		Directory.CreateDirectory(directory);

		try
		{
			ComposeWriter.Write(new() { Project = "example", Name = "example" }, [source], directory);
			using var document = JsonDocument.Parse(File.ReadAllText(Path.Combine(directory, "compose.yaml")));
			var health = document.RootElement.GetProperty("services").GetProperty("example").GetProperty("healthcheck");

			Assert.Equal("CMD-SHELL", health.GetProperty("test")[0].GetString());
			Assert.Equal("kill -0 1", health.GetProperty("test")[1].GetString());
		}
		finally { Directory.Delete(directory, true); }
	}

	[Fact]
	public void ProcessHealthDoesNotSupplyAnUnknownStartupCommand()
	{
		var component = new ContainerManifest.Component(new PackageReader.Candidate("example.tar.gz", new() { Name = "example" }));
		Assert.Equal(2, Assert.Throws<ContainerizationException>(() => ApplicationPlanner.Create(component, new())).Code);
	}

	[Fact]
	public void TemplateConfigurationMountsResolveFromTheReleaseWithoutAnEnvironmentFile()
	{
		var directory = Path.Combine(Path.GetTempPath(), "containerizer-compose-" + Guid.NewGuid().ToString("N"));
		Directory.CreateDirectory(directory);

		try
		{
			var source = new ServiceBuildContext(new() { Name = "nginx" }) { Plan = new() { Id = "nginx" } };
			source.Plan.Mounts.Add(new() { Source = "config/nginx/$site.conf", Target = "/etc/nginx/conf.d/site.conf", ReadOnly = true });
			ComposeWriter.Write(new() { Project = "example", Name = "example" }, [source], directory);
			using var document = JsonDocument.Parse(File.ReadAllText(Path.Combine(directory, "compose.yaml")));
			var mount = document.RootElement.GetProperty("services").GetProperty("nginx").GetProperty("volumes")[0];
			Assert.Equal("./config/nginx/$$site.conf", mount.GetProperty("source").GetString());
			Assert.True(mount.GetProperty("read_only").GetBoolean());
			Assert.False(mount.GetProperty("bind").GetProperty("create_host_path").GetBoolean());
		}
		finally { Directory.Delete(directory, true); }
	}
}
