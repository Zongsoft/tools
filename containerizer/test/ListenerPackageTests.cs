using System;
using System.IO;
using System.Text;
using System.Formats.Tar;
using System.IO.Compression;
using System.Collections.Generic;

using Xunit;

using Zongsoft.Tools.Containerizer.Protocol;

namespace Zongsoft.Tools.Containerizer.Tests;

public sealed class ListenerPackageTests
{
	#region 包元数据
	[Theory]
	[InlineData(null)]
	[InlineData("")]
	[InlineData("http://127.0.0.1:8069")]
	public void ReadsNativeListenerAndKeepsOldPackagesOnProcessHealth(string listen)
	{
		var directory = Path.Combine(Path.GetTempPath(), "containerizer-listener-" + Guid.NewGuid().ToString("N"));
		Directory.CreateDirectory(directory);

		try
		{
			var path = WritePackage(directory, listen);
			var package = PackageReader.Read(path);
			Assert.Equal(listen, package.Listen);
			var source = new ServiceBuildContext { Plan = new() { Id = "example", Kind = "application" } };
			ApplicationImageBuilder.ResolveEntry(package, source);

			if(string.IsNullOrEmpty(listen))
			{
				Assert.Equal(["CMD-SHELL", "kill -0 1"], source.Plan.Health.Test);
				Assert.Equal("http://127.0.0.1:8069", source.Plan.Entrypoint[^1]);
			}
			else
			{
				Assert.Contains("'http://127.0.0.1:8069/'", source.Plan.Health.Test[1]);
				Assert.Equal("http://0.0.0.0:8069", source.Plan.Entrypoint[^1]);
			}
		}
		finally { Directory.Delete(directory, true); }
	}

	[Fact]
	public void InvalidNativeMetadataFailsDuringPackageReading()
	{
		var directory = Path.Combine(Path.GetTempPath(), "containerizer-listener-" + Guid.NewGuid().ToString("N"));
		Directory.CreateDirectory(directory);

		try
		{
			Assert.Equal(2, Assert.Throws<ContainerizationException>(() => PackageReader.Read(WritePackage(directory, "not-a-url"))).Code);
		}
		finally { Directory.Delete(directory, true); }
	}

	#endregion

	#region 辅助方法
	private static string WritePackage(string directory, string listen)
	{
		var path = Path.Combine(directory, "example.tar.gz");
		var metadata = new Dictionary<string, string> { ["PackageName"] = "example", ["Architecture"] = "x64", ["Version"] = "1.0.0", ["InstallPath"] = "/opt/example" };

		if(listen != null)
			metadata["Listen"] = listen;

		using(var output = File.Create(path))
		using(var gzip = new GZipStream(output, CompressionLevel.Optimal))
		using(var writer = new TarWriter(gzip))
		{
			writer.WriteEntry(new PaxGlobalExtendedAttributesTarEntry(metadata));
			using var content = new MemoryStream(Encoding.UTF8.GetBytes("[Service]\nWorkingDirectory=/opt/example\nExecStart=dotnet /opt/example/example.dll --urls http://127.0.0.1:8069\n"));
			writer.WriteEntry(new PaxTarEntry(TarEntryType.RegularFile, "example.service") { DataStream = content });
		}

		File.WriteAllText(path[..^7] + ".sh", "#!/bin/sh\nexit 0\n");
		return path;
	}
	#endregion
}
