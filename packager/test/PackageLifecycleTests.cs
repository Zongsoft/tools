using System;
using System.Linq;
using System.Text;
using System.IO;
using System.IO.Compression;
using System.Formats.Tar;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Runtime.InteropServices;

using Xunit;

[assembly: CollectionBehavior(DisableTestParallelization = true)]

namespace Zongsoft.Tools.Packager.Tests;

public sealed class PackageLifecycleTests
{
	private const string INSTALL_PATH = "/opt/zongsoft/lifecycle-test";
	private const string UNINSTALLING_MARKER = "echo uninstalling-lifecycle-marker";
	private const string UNINSTALLED_MARKER = "echo uninstalled-lifecycle-marker";

	[Theory]
	[InlineData("tar")]
	[InlineData("deb")]
	[InlineData("rpm")]
	public void Package_Provenance_RecordsGeneratorAndPreservesApplicationMetadata(string format)
	{
		using var directory = new TemporaryDirectory();
		InitializeNormalizer(directory.Path);
		var version = new Version(1, 2, 3);
		Package package = format switch
		{
			"tar" => new Package.Tar("zongsoft.daemon", null, version, Platform.Linux, Architecture.X64),
			"deb" => new Package.Deb("zongsoft.daemon", null, version, Platform.Linux, Architecture.X64),
			_ => new Package.Rpm("zongsoft.daemon", null, version, Platform.Linux, Architecture.X64),
		};
		package.InstallPath = INSTALL_PATH;
		package.Maintainer = "Hosting Maintainer";
		package.Scripts = new(":", ":", ":", ":");

		package.Pack(directory.Path, true);

		var path = Path.Combine(directory.Path, package.FileName);
		var expected = $"Zongsoft.Tools.Packager@{typeof(Package).Assembly.GetName().Version.ToString(3)}";
		switch(format)
		{
			case "tar":
				using(var stream = File.OpenRead(path))
				using(var gzip = new GZipStream(stream, CompressionMode.Decompress))
				using(var reader = new TarReader(gzip))
				{
					var metadata = Assert.IsType<PaxGlobalExtendedAttributesTarEntry>(reader.GetNextEntry());
					Assert.Equal(expected, metadata.GlobalExtendedAttributes["Packager"]);
					Assert.Null(metadata.DataStream);
					Assert.Equal("install.sh", reader.GetNextEntry().Name);
					Assert.Equal("uninstall.sh", reader.GetNextEntry().Name);
					Assert.Null(reader.GetNextEntry());
				}
				break;
			case "deb":
				var control = PackageReader.ReadDebianControlScript(path, "control");
				Assert.Contains($"\nPackager: {expected}\n", control);
				Assert.Contains("\nMaintainer: Hosting Maintainer\n", control);
				Assert.Contains("\nVersion: 1.2.3\n", control);
				break;
			case "rpm":
				var tags = PackageReader.ReadRpmStringTags(path, 1001, 1015, 1064);
				Assert.Equal(expected, tags[1064]);
				Assert.Equal("Hosting Maintainer", tags[1015]);
				Assert.Equal("1.2.3", tags[1001]);
				break;
		}
	}

	[Theory]
	[InlineData("upgrade")]
	[InlineData("failed-upgrade")]
	[InlineData("abort-install")]
	[InlineData("abort-upgrade")]
	[InlineData("disappear")]
	public void DebianPostRemove_NonRemovalActions_SkipUninstalledScript(string action)
	{
		using var directory = new TemporaryDirectory();
		var script = GenerateDebianScript(directory.Path, "postrm");
		var actions = GetDebianLifecycleActions(script);

		Assert.Contains(UNINSTALLED_MARKER, script);
		Assert.Equal(new[] { "remove", "purge" }, actions);
		Assert.DoesNotContain(action, actions);
	}

	[Theory]
	[InlineData("remove")]
	[InlineData("purge")]
	public void DebianPostRemove_RemoveAndPurge_RunUninstalledScript(string action)
	{
		using var directory = new TemporaryDirectory();
		var script = GenerateDebianScript(directory.Path, "postrm");
		var actions = GetDebianLifecycleActions(script);

		Assert.Contains(UNINSTALLED_MARKER, script);
		Assert.Equal(new[] { "remove", "purge" }, actions);
		Assert.Contains(action, actions);
	}

	[Fact]
	public void DebianPreRemove_Upgrade_SkipsUninstallingScript()
	{
		using var directory = new TemporaryDirectory();
		var script = GenerateDebianScript(directory.Path, "prerm");
		var actions = GetDebianLifecycleActions(script);

		Assert.Contains(UNINSTALLING_MARKER, script);
		Assert.Equal(new[] { "remove", "deconfigure" }, actions);
		Assert.DoesNotContain("upgrade", actions);
		Assert.Contains("remove", actions);
	}

	[Theory]
	[InlineData(1)]
	[InlineData(2)]
	public void RpmUninstallScripts_Upgrade_SkipLifecycleScripts(int remainingInstances)
	{
		using var directory = new TemporaryDirectory();
		var scripts = GenerateRpmUninstallScripts(directory.Path);

		Assert.Equal(UNINSTALLING_MARKER, GetRpmLifecycleBody(scripts.PreUninstall));
		Assert.Equal(UNINSTALLED_MARKER, GetRpmLifecycleBody(scripts.PostUninstall));
		Assert.False(RpmLifecycleRunsFor(scripts.PreUninstall, remainingInstances));
		Assert.False(RpmLifecycleRunsFor(scripts.PostUninstall, remainingInstances));
	}

	[Fact]
	public void RpmUninstallScripts_FinalRemoval_RunsLifecycleScripts()
	{
		using var directory = new TemporaryDirectory();
		var scripts = GenerateRpmUninstallScripts(directory.Path);

		Assert.Equal(UNINSTALLING_MARKER, GetRpmLifecycleBody(scripts.PreUninstall));
		Assert.Equal(UNINSTALLED_MARKER, GetRpmLifecycleBody(scripts.PostUninstall));
		Assert.True(RpmLifecycleRunsFor(scripts.PreUninstall, 0));
		Assert.True(RpmLifecycleRunsFor(scripts.PostUninstall, 0));
	}

	[Fact]
	public void TarUninstallScript_ExplicitUninstall_DeletesOnlyResolvedTarget()
	{
		using var directory = new TemporaryDirectory();
		InitializeNormalizer(directory.Path);

		var package = new Package.Tar("lifecycle-test", null, new Version(1, 0, 0), Platform.Linux, Architecture.X64)
		{
			InstallPath = INSTALL_PATH,
		};

		package.Scriptor.Script();
		package.Pack(directory.Path, true);

		var archive = Directory.GetFiles(directory.Path, "*.tar.gz").Single();
		var script = PackageReader.ReadTarEntry(archive, "uninstall.sh");

		Assert.Contains("rm -rf \"$TARGET\"", script);
		Assert.DoesNotContain($"rm -rf '{INSTALL_PATH}'", script);
		Assert.Equal(1, CountOccurrences(script, "rm -rf"));
		Assert.DoesNotContain("${1:-0}", script);
	}

	private static string GenerateDebianScript(string output, string name)
	{
		InitializeNormalizer(output);

		var package = new Package.Deb("lifecycle-test", null, new Version(1, 0, 0), Platform.Linux, Architecture.X64)
		{
			InstallPath = INSTALL_PATH,
			Scripts = new(":", ":", UNINSTALLING_MARKER, UNINSTALLED_MARKER),
		};

		package.Pack(output, true);
		return PackageReader.ReadDebianControlScript(Directory.GetFiles(output, "*.deb").Single(), name);
	}

	private static (string PreUninstall, string PostUninstall) GenerateRpmUninstallScripts(string output)
	{
		InitializeNormalizer(output);

		var package = new Package.Rpm("lifecycle-test", null, new Version(1, 0, 0), Platform.Linux, Architecture.X64)
		{
			InstallPath = INSTALL_PATH,
			Scripts = new(":", ":", UNINSTALLING_MARKER, UNINSTALLED_MARKER),
		};

		package.Pack(output, true);
		var scripts = PackageReader.ReadRpmStringTags(Directory.GetFiles(output, "*.rpm").Single(), 1025, 1026);

		return (scripts[1025], scripts[1026]);
	}

	private static void InitializeNormalizer(string source)
	{
		Normalizer.Initialize(new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
		{
			["framework"] = "net10.0",
			["source"] = source,
			["daemon"] = "disabled",
		});
	}

	private static string[] GetDebianLifecycleActions(string script)
	{
		const string prefix = "case \"${1:-}\" in\n";
		var guard = script.IndexOf(prefix, StringComparison.Ordinal);

		Assert.True(guard >= 0, "The Debian lifecycle script has no action guard.");

		var patternsStart = guard + prefix.Length;
		var patternsEnd = script.IndexOf(')', patternsStart);
		Assert.True(patternsEnd > patternsStart, "The Debian lifecycle guard has no action pattern.");

		return script[patternsStart..patternsEnd]
			.Trim()
			.Split('|', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
	}

	private static bool RpmLifecycleRunsFor(string script, int remainingInstances)
	{
		const string guard = "if [ \"${1:-0}\" -eq 0 ]; then";

		if(!script.Contains(guard, StringComparison.Ordinal))
			return true;

		return remainingInstances == 0;
	}

	private static string GetRpmLifecycleBody(string script)
	{
		const string prefix = "#!/bin/sh\nset -e\nif [ \"${1:-0}\" -eq 0 ]; then\n";
		const string suffix = "\nfi\n";

		Assert.StartsWith(prefix, script, StringComparison.Ordinal);
		Assert.EndsWith(suffix, script, StringComparison.Ordinal);

		return script[prefix.Length..^suffix.Length];
	}

	private static int CountOccurrences(string text, string value)
	{
		var count = 0;
		var index = 0;

		while((index = text.IndexOf(value, index, StringComparison.Ordinal)) >= 0)
		{
			count++;
			index += value.Length;
		}

		return count;
	}

	private sealed class TemporaryDirectory : IDisposable
	{
		public TemporaryDirectory()
		{
			this.Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), nameof(PackageLifecycleTests), Guid.NewGuid().ToString("N"));
			Directory.CreateDirectory(this.Path);
		}

		public string Path { get; }

		public void Dispose()
		{
			if(Directory.Exists(this.Path))
				Directory.Delete(this.Path, true);
		}
	}

	private static class PackageReader
	{
		public static string ReadDebianControlScript(string packagePath, string name)
		{
			var bytes = File.ReadAllBytes(packagePath);
			Assert.Equal("!<arch>\n", Encoding.ASCII.GetString(bytes, 0, 8));

			var offset = 8;

			while(offset + 60 <= bytes.Length)
			{
				var entryName = Encoding.ASCII.GetString(bytes, offset, 16).Trim().TrimEnd('/');
				var sizeText = Encoding.ASCII.GetString(bytes, offset + 48, 10).Trim();
				var size = int.Parse(sizeText, System.Globalization.CultureInfo.InvariantCulture);
				var dataOffset = offset + 60;

				if(entryName == "control.tar.gz")
				{
					using var compressed = new MemoryStream(bytes, dataOffset, size, false);
					using var gzip = new GZipStream(compressed, CompressionMode.Decompress);
					return ReadTarEntry(gzip, name);
				}

				offset = dataOffset + size + (size & 1);
			}

			throw new InvalidDataException("The Debian control archive was not found.");
		}

		public static string ReadTarEntry(string archivePath, string name)
		{
			using var stream = File.OpenRead(archivePath);
			using var gzip = new GZipStream(stream, CompressionMode.Decompress);
			return ReadTarEntry(gzip, name);
		}

		public static Dictionary<int, string> ReadRpmStringTags(string packagePath, params int[] tags)
		{
			var bytes = File.ReadAllBytes(packagePath);
			var signatureOffset = 96;
			var headerOffset = SkipRpmHeader(bytes, signatureOffset);
			var indexCount = ReadInt32(bytes, headerOffset + 8);
			var storeOffset = headerOffset + 16 + indexCount * 16;
			var requested = tags.ToHashSet();
			var result = new Dictionary<int, string>();

			for(var index = 0; index < indexCount; index++)
			{
				var entryOffset = headerOffset + 16 + index * 16;
				var tag = ReadInt32(bytes, entryOffset);
				var type = ReadInt32(bytes, entryOffset + 4);
				var valueOffset = ReadInt32(bytes, entryOffset + 8);

				if(!requested.Contains(tag) || type is not (6 or 9))
					continue;

				var start = storeOffset + valueOffset;
				var end = Array.IndexOf(bytes, (byte)0, start);
				Assert.True(end >= start, $"RPM tag {tag} has no string terminator.");
				result[tag] = Encoding.UTF8.GetString(bytes, start, end - start);
			}

			Assert.Equal(requested.Order(), result.Keys.Order());
			return result;
		}

		private static string ReadTarEntry(Stream stream, string name)
		{
			using var reader = new TarReader(stream);
			TarEntry entry;

			while((entry = reader.GetNextEntry()) != null)
			{
				if(entry.Name != name)
					continue;

				Assert.NotNull(entry.DataStream);
				using var text = new StreamReader(entry.DataStream, Encoding.UTF8, false, leaveOpen: true);
				return text.ReadToEnd();
			}

			throw new InvalidDataException($"The tar entry '{name}' was not found.");
		}

		private static int SkipRpmHeader(byte[] bytes, int offset)
		{
			Assert.Equal(new byte[] { 0x8e, 0xad, 0xe8, 0x01 }, bytes[offset..(offset + 4)]);

			var indexCount = ReadInt32(bytes, offset + 8);
			var storeSize = ReadInt32(bytes, offset + 12);
			var size = 16 + indexCount * 16 + storeSize;

			return offset + (size + 7 & ~7);
		}

		private static int ReadInt32(byte[] bytes, int offset) => BinaryPrimitives.ReadInt32BigEndian(bytes.AsSpan(offset, 4));
	}
}
