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
}
