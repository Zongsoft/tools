using System;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Formats.Tar;
using System.IO.Compression;

using Xunit;

using Zongsoft.Tools.Containerizer.Protocol;

namespace Zongsoft.Tools.Containerizer.Executor.Tests;

public sealed class ArchiveTests : IDisposable
{
	private readonly string _root = Path.Combine(Path.GetTempPath(), "containerizer-archive-" + Guid.NewGuid().ToString("N"));
	public ArchiveTests() => Directory.CreateDirectory(_root);
	public void Dispose() => Directory.Delete(_root, true);

	[Theory]
	[InlineData("../outside")]
	[InlineData("/outside")]
	[InlineData("C:/outside")]
	[InlineData("dir/../../outside")]
	public void ExtractionRejectsEscapingPaths(string name)
	{
		var archive = Path.Combine(_root, "test.tar.gz");

		using(var output = File.Create(archive))
		using(var gzip = new GZipStream(output, CompressionLevel.Optimal))
		using(var writer = new TarWriter(gzip))
		using(var data = new MemoryStream(Encoding.UTF8.GetBytes("content")))
			writer.WriteEntry(new PaxTarEntry(TarEntryType.RegularFile, name) { DataStream = data });

		Assert.Throws<ContainerizationException>(() => Files.Extract(archive, Path.Combine(_root, "extracted")));
		Assert.False(File.Exists(Path.Combine(_root, "outside")));
	}

	[Fact]
	public void ExtractionRejectsSymbolicLinks()
	{
		var archive = Path.Combine(_root, "test.tar.gz");

		using(var output = File.Create(archive))
		using(var gzip = new GZipStream(output, CompressionLevel.Optimal))
		using(var writer = new TarWriter(gzip))
			writer.WriteEntry(new PaxTarEntry(TarEntryType.SymbolicLink, "link") { LinkName = "../outside" });

		Assert.Throws<ContainerizationException>(() => Files.Extract(archive, Path.Combine(_root, "extracted")));
	}

	[Fact]
	public void ProtocolRejectsUnknownFields()
	{
		Assert.Throws<JsonException>(() => JsonSerializer.Deserialize("{\"schema\":1,\"futureBehavior\":true}", ProtocolJson.Default.DeliveryPlan));
	}

	[Fact]
	public void StatePropertiesUseTheirCurrentProtocolFieldNames()
	{
		var installation = new Installation { IsInMaintenance = true, Pending = new() { IsReinstall = true } };
		var state = JsonSerializer.Serialize(installation, ProtocolJson.Default.Installation);
		using var stateJson = JsonDocument.Parse(state);

		Assert.True(stateJson.RootElement.GetProperty("isInMaintenance").GetBoolean());
		Assert.False(stateJson.RootElement.TryGetProperty("maintenance", out _));
		Assert.True(stateJson.RootElement.GetProperty("pending").GetProperty("isReinstall").GetBoolean());
		Assert.False(stateJson.RootElement.GetProperty("pending").TryGetProperty("reinstall", out _));
		var restored = JsonSerializer.Deserialize(state, ProtocolJson.Default.Installation);
		Assert.True(restored.IsInMaintenance);
		Assert.True(restored.Pending.IsReinstall);

		var service = new ServicePlan { Web = [new() { Bindings = [new() { IsExplicitDefault = true }] }] };
		var plan = JsonSerializer.Serialize(service, ProtocolJson.Default.ServicePlan);
		using var planJson = JsonDocument.Parse(plan);
		var binding = planJson.RootElement.GetProperty("web")[0].GetProperty("bindings")[0];

		Assert.True(binding.GetProperty("isExplicitDefault").GetBoolean());
		Assert.False(binding.TryGetProperty("explicitDefault", out _));
		Assert.True(JsonSerializer.Deserialize(plan, ProtocolJson.Default.ServicePlan).Web[0].Bindings[0].IsExplicitDefault);
	}
}
