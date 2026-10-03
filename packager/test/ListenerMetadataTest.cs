using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Reflection;
using System.Formats.Tar;
using System.Threading.Tasks;
using System.IO.Compression;
using System.Buffers.Binary;
using System.Collections.Generic;

using Xunit;

using Zongsoft.Terminals;
using Zongsoft.Components;

namespace Zongsoft.Tools.Packager.Tests;

public sealed class ListenerMetadataTest
{
	#region 元数据
	[Theory]
	[InlineData("tar", "8069", "http://127.0.0.1:8069")]
	[InlineData("deb", "8069", "http://127.0.0.1:8069")]
	[InlineData("rpm", "8069", "http://127.0.0.1:8069")]
	[InlineData("tar", "https://example.test:8443;http://0.0.0.0:8069", "https://example.test:8443;http://0.0.0.0:8069")]
	[InlineData("deb", "https://example.test:8443;http://0.0.0.0:8069", "https://example.test:8443;http://0.0.0.0:8069")]
	[InlineData("rpm", "https://example.test:8443;http://0.0.0.0:8069", "https://example.test:8443;http://0.0.0.0:8069")]
	public async Task GeneratedHostRecordsEffectiveListenerInNativeMetadata(string format, string value, string expected)
	{
		using var directory = new MigrationTestDirectory();
		directory.Write("example.dll", "application fixture");
		directory.Write(".env", "packager_listener=" + value + "\n");
		var path = await Execute(directory.Path, format, "--listen:$(packager_listener)");
		Assert.Equal(expected, ReadListener(path, format));

		var entries = PackageArtifactTest.ReadArchive(path, format);
		var service = Assert.Single(entries, entry => entry.Name.EndsWith("example.service", StringComparison.Ordinal));
		Assert.Contains("--urls " + expected, Encoding.UTF8.GetString(service.Content));
		Assert.DoesNotContain(entries, entry => entry.Name.EndsWith(".packager.json", StringComparison.Ordinal));
	}

	[Theory]
	[InlineData("tar", "omitted")]
	[InlineData("deb", "omitted")]
	[InlineData("rpm", "omitted")]
	[InlineData("tar", "existing")]
	[InlineData("deb", "existing")]
	[InlineData("rpm", "existing")]
	[InlineData("tar", "disabled")]
	[InlineData("deb", "disabled")]
	[InlineData("rpm", "disabled")]
	public async Task UnusedListenerIsNotAdvertised(string format, string kind)
	{
		const string SERVICE = "[Service]\nWorkingDirectory=/opt/example\nExecStart=/opt/example/custom\n";

		using var directory = new MigrationTestDirectory();
		directory.Write("example.dll", "application fixture");

		var arguments = kind switch
		{
			"existing" => new[] { "--daemon:example.service", "--listen:8069" },
			"disabled" => ["--daemon:none", "--listen:8069"],
			_ => [],
		};

		if(kind == "existing")
			directory.Write("example.service", SERVICE);

		var path = await Execute(directory.Path, format, arguments);
		Assert.Null(ReadListener(path, format));

		if(kind == "existing")
			Assert.Equal(SERVICE.ReplaceLineEndings("\r\n"), Encoding.UTF8.GetString(Assert.Single(PackageArtifactTest.ReadArchive(path, format), entry => entry.Name.EndsWith("example.service", StringComparison.Ordinal)).Content));
	}
	#endregion

	#region 辅助方法
	private static async Task<string> Execute(string source, string format, params string[] options)
	{
		CommandBase<CommandContext> command = format switch { "tar" => new TarCommand(), "deb" => new DebCommand(), _ => new RpmCommand() };
		var arguments = new[] { "--name:example", "--version:1.0.0", "--source:" + source, "--output:out", "--platform:Linux", "--architecture:X64", "--install-path:/opt/example" }.Concat(options).ToArray();
		var terminalField = typeof(Terminal).GetField("_default", BindingFlags.NonPublic | BindingFlags.Static);
		var terminal = (ITerminal)terminalField.GetValue(null);

		try
		{
			Terminal.Default = DispatchProxy.Create<ITerminal, MigratorPackageTest.RecordingTerminal>();
			var executor = new CommandExecutor();
			executor.Root.Children.Add(command);
			return Assert.IsType<string>(await executor.ExecuteAsync(Utility.FormatCommand(format, arguments), cancellation: TestContext.Current.CancellationToken));
		}
		finally { Terminal.Default = terminal; }
	}

	private static string ReadListener(string path, string format)
	{
		if(format == "deb")
			return PackageArtifactTest.ReadControl(path, "control").Split('\n').SingleOrDefault(line => line.StartsWith("Listen: ", StringComparison.Ordinal))?[8..];

		if(format == "tar")
		{
			using var stream = File.OpenRead(path);
			using var gzip = new GZipStream(stream, CompressionMode.Decompress);
			using var reader = new TarReader(gzip);
			return Assert.IsType<PaxGlobalExtendedAttributesTarEntry>(reader.GetNextEntry()).GlobalExtendedAttributes.GetValueOrDefault("Listen");
		}

		var bytes = File.ReadAllBytes(path);
		var signatureLength = 16 + ReadInt(104) * 16 + ReadInt(108);
		var header = 96 + ((signatureLength + 7) & ~7);
		var count = ReadInt(header + 8);
		var indices = Enumerable.Range(0, count).Select(index => header + 16 + index * 16).Where(offset => ReadInt(offset) == 1000001).ToArray();

		if(indices.Length == 0)
			return null;

		var field = Assert.Single(indices);
		Assert.Equal(6, ReadInt(field + 4));
		Assert.Equal(1, ReadInt(field + 12));

		var start = header + 16 + count * 16 + ReadInt(field + 8);
		return Encoding.UTF8.GetString(bytes, start, Array.IndexOf(bytes, (byte)0, start) - start);

		int ReadInt(int offset) => BinaryPrimitives.ReadInt32BigEndian(bytes.AsSpan(offset, 4));
	}
	#endregion
}
