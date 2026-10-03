using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Reflection;
using System.Formats.Tar;
using System.IO.Compression;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Runtime.InteropServices;

using Xunit;

using Zongsoft.Services;

namespace Zongsoft.Tools.Packager.Tests;

public sealed class PackageMetadataTest
{
	#region 元数据
	[Theory]
	[InlineData(Architecture.X64, "x64", "amd64", "x86_64")]
	[InlineData(Architecture.X86, "x86", "i386", "i386")]
	[InlineData(Architecture.Arm64, "arm64", "arm64", "aarch64")]
	[InlineData(Architecture.Arm, "arm", "armhf", "armv7hl")]
	public void AllFormats_RecordCompleteApplicationMetadata(Architecture architecture, string tarArchitecture, string debArchitecture, string rpmArchitecture)
	{
		using var directory = new MigrationTestDirectory();
		directory.Write("settings.conf", "配置载荷");

		var architectures = new Dictionary<string, string>
		{
			["tar"] = tarArchitecture,
			["deb"] = debArchitecture,
			["rpm"] = rpmArchitecture
		};

		foreach(var format in new[] { "tar", "deb", "rpm" })
		{
			var package = Create(format, directory.Path, architecture);

			package.Manufacturer = "软件厂家";
			package.Maintainer = "Maintainer <maintainer@example.test>";
			package.License = "MIT";
			package.Homepage = "https://example.test/product";
			package.Category = "web";
			package.Summary = "应用摘要";
			package.Description = "第一段\\path\r\n\r\n  保留缩进\r\n最后一段";
			package.Dependencies = ["runtime:[10.0,11.0) | alternative:[9.0)", "libssl:[3.0]"];
			package.Host = new(ApplicationHost.HostKind.Generated, Listen: "http://127.0.0.1:8069");
			package.Entries.AddGeneratedContent("payload.txt", "生成载荷", Utility.Unix.Mode644);
			package.Entries.Add(directory.Path, "settings.conf:/etc/example/settings.conf");
			package.Entries.SetVersion(new(package.Name, package.Edition, package.Version));
			package.Pack(directory.Path, false);

			var path = Path.Combine(directory.Path, package.FileName);
			var metadata = ReadMetadata(path, format);

			Assert.Equal($"Zongsoft.Tools.Packager@{typeof(Package).Assembly.GetName().Version}", metadata["Packager"]);
			Assert.Equal("example-enterprise", metadata["PackageName"]);
			Assert.Equal("1.2.3.4", metadata["Version"]);
			Assert.Equal(architectures[format], metadata["Architecture"]);
			Assert.Equal("软件厂家", metadata["Manufacturer"]);
			Assert.Equal(package.Maintainer, metadata["Maintainer"]);
			Assert.Equal("MIT", metadata["License"]);
			Assert.Equal(package.Homepage, metadata["Homepage"]);
			Assert.Equal("web", metadata["Category"]);
			Assert.Equal("应用摘要", metadata["Summary"]);
			Assert.Equal(package.Description.ReplaceLineEndings("\n"), metadata["Description"].ReplaceLineEndings("\n"));
			Assert.Equal(package.InstallPath, metadata["InstallPath"]);
			Assert.Equal(package.Listen, metadata["Listen"]);

			var entries = PackageArtifactTest.ReadArchive(path, format).Where(entry => !entry.IsDirectory && entry.Name is not ("install.sh" or "uninstall.sh")).ToArray();
			Assert.Equal(entries.Sum(entry => entry.Size).ToString(System.Globalization.CultureInfo.InvariantCulture), metadata["PackageSize"]);
			Assert.Equal(3, entries.Length);

			using var version = new MemoryStream(Assert.Single(entries, entry => entry.Name.EndsWith(".version", StringComparison.Ordinal)).Content);
			Assert.Equal(new ApplicationIdentifier(package.Name, package.Edition, package.Version), ApplicationIdentifier.Load(version));

			Assert.Equal(format switch
			{
				"tar" => "runtime:[10.0,11.0) | alternative:[9.0); libssl:[3.0]",
				"deb" => "runtime (>= 10.0) | alternative (>= 9.0), runtime (<< 11.0) | alternative (>= 9.0), libssl (= 3.0)",
				_ => "((runtime >= 10.0 with runtime < 11.0) or alternative >= 9.0); libssl",
			}, metadata["Dependencies"]);
		}
	}

	[Theory]
	[InlineData(null, null, null, "example", "example")]
	[InlineData("", " ", "\r\n", "example", "example")]
	[InlineData(null, "标题", null, "标题", "标题")]
	[InlineData(null, "标题", "完整描述", "标题", "完整描述")]
	[InlineData("摘要", "标题", null, "摘要", "摘要")]
	[InlineData("摘要", "标题", "完整描述", "摘要", "完整描述")]
	[InlineData("摘要\r\n第二行", null, "完整描述", "摘要\r\n第二行", "完整描述")]
	public void AllFormats_UseSameTextFallbacks(string summary, string title, string description, string expectedSummary, string expectedDescription)
	{
		using var directory = new MigrationTestDirectory();

		foreach(var format in new[] { "tar", "deb", "rpm" })
		{
			var package = Create(format, directory.Path);
			package.Summary = summary;
			package.Title = title;
			package.Description = description;
			package.Pack(directory.Path, false);

			var metadata = ReadMetadata(Path.Combine(directory.Path, package.FileName), format);
			Assert.Equal(format == "deb" ? expectedSummary.Replace("\r", "").Replace("\n", " ") : expectedSummary, metadata["Summary"]);
			Assert.Equal(expectedDescription, metadata["Description"]);
		}
	}

	[Theory]
	[InlineData(null)]
	[InlineData("")]
	[InlineData("   ")]
	public void AllFormats_OmitBlankOptionalFieldsAndRetainNativeDefaults(string value)
	{
		using var directory = new MigrationTestDirectory();

		foreach(var format in new[] { "tar", "deb", "rpm" })
		{
			var package = Create(format, directory.Path);
			package.License = value;
			package.Homepage = value;
			package.Maintainer = value;
			package.Category = value;
			package.Dependencies = value == null ? null : [value];
			package.Pack(directory.Path, false);

			var metadata = ReadMetadata(Path.Combine(directory.Path, package.FileName), format);
			Assert.False(metadata.ContainsKey("License"));
			Assert.False(metadata.ContainsKey("Homepage"));
			Assert.False(metadata.ContainsKey("Listen"));
			Assert.Equal("0", metadata["PackageSize"]);
			Assert.Equal(format switch { "deb" => "Unknown", _ => null }, metadata.GetValueOrDefault("Maintainer"));
			Assert.Equal(format switch { "deb" => "utils", "rpm" => "Applications/System", _ => null }, metadata.GetValueOrDefault("Category"));
			Assert.False(metadata.ContainsKey("Dependencies"));
		}
	}
	#endregion

	#region 大小边界
	[Theory]
	[InlineData(0L, 1009, 4)]
	[InlineData(2147483648L, 1009, 4)]
	[InlineData(4294967295L, 1009, 4)]
	[InlineData(4294967296L, 5009, 5)]
	[InlineData(1099511627776L, 5009, 5)]
	public void Rpm_SizeMetadata_PreservesUnsigned32BitAnd64BitValues(long size, int expectedTag, int expectedType)
	{
		var type = typeof(Generator).GetNestedType("RpmHeaderBuilder", BindingFlags.NonPublic);
		var builder = Activator.CreateInstance(type, true);
		type.GetMethod("AddSize").Invoke(builder, [1009, 5009, size]);

		var bytes = Assert.IsType<byte[]>(type.GetMethod("Build").Invoke(builder, [false]));
		var field = Assert.Single(ReadRpmFields(bytes, 0), field => field.Tag == expectedTag);

		Assert.Equal(expectedType, field.Type);
		Assert.Equal(0, field.Offset % (expectedType == 5 ? 8 : 4));
		Assert.Equal(size, expectedType == 5 ? BinaryPrimitives.ReadInt64BigEndian(bytes.AsSpan(field.Offset, 8)) : BinaryPrimitives.ReadUInt32BigEndian(bytes.AsSpan(field.Offset, 4)));
	}

	[Fact]
	public void Rpm_Newc_RejectsUnrepresentableFileSizeBeforeWritingHeader()
	{
		using var stream = new MemoryStream();
		var method = typeof(Generator).GetMethod("WriteCpioEntry", BindingFlags.NonPublic | BindingFlags.Static);
		var exception = Assert.Throws<TargetInvocationException>(() => method.Invoke(null, [stream, 1, "./payload", 0x8000, Utility.Unix.Mode644, (long)uint.MaxValue + 1, 0L, null]));

		Assert.IsType<OverflowException>(exception.InnerException);
		Assert.Equal(0, stream.Length);
	}
	#endregion

	#region 辅助方法
	private static Package Create(string format, string source, Architecture architecture = Architecture.X64)
	{
		var variables = new Variables(new Dictionary<string, string>
		{
			["source"] = source,
			["daemon"] = "example.service"
		});

		Package package = format switch
		{
			"tar" => new Package.Tar("example", "enterprise", new Version(1, 2, 3, 4), Platform.Linux, architecture, variables),
			"deb" => new Package.Deb("example", "enterprise", new Version(1, 2, 3, 4), Platform.Linux, architecture, variables),
			_ => new Package.Rpm("example", "enterprise", new Version(1, 2, 3, 4), Platform.Linux, architecture, variables),
		};

		package.InstallPath = "/opt/example";
		package.Scripts = new(":", ":", ":", ":");

		return package;
	}

	private static Dictionary<string, string> ReadMetadata(string path, string format)
	{
		if(format == "tar")
		{
			using var stream = File.OpenRead(path);
			using var gzip = new GZipStream(stream, CompressionMode.Decompress);
			using var reader = new TarReader(gzip);

			var result = Assert.IsType<PaxGlobalExtendedAttributesTarEntry>(reader.GetNextEntry()).GlobalExtendedAttributes.ToDictionary();
			result["Summary"] = System.Text.RegularExpressions.Regex.Unescape(result["Summary"]);
			result["Description"] = System.Text.RegularExpressions.Regex.Unescape(result["Description"]);

			return result;
		}

		if(format == "deb")
		{
			var result = new Dictionary<string, string>();
			string name = null;

			foreach(var line in PackageArtifactTest.ReadControl(path, "control").Split('\n'))
			{
				if(line.StartsWith(' '))
					result[name] += "\n" + (line == " ." ? "" : line[1..]);
				else if(line.Contains(':'))
				{
					var index = line.IndexOf(':');
					name = line[..index];
					result.Add(name, line[(index + 2)..]);
				}
			}

			result["PackageName"] = result["Package"];
			result["Category"] = result["Section"];

			if(result.TryGetValue("Depends", out var depends))
				result["Dependencies"] = depends;

			var description = result["Description"].Split('\n', 2);
			result["Summary"] = description[0];
			result["Description"] = description.Length == 1 ? description[0] : description[1];

			return result;
		}

		var bytes = File.ReadAllBytes(path);
		var signatureLength = 16 + ReadInt(bytes, 104) * 16 + ReadInt(bytes, 108);
		var fields = ReadRpmFields(bytes, 96 + ((signatureLength + 7) & ~7));

		var tags = new Dictionary<int, string>
		{
			[1000] = "PackageName",
			[1001] = "Version",
			[1004] = "Summary",
			[1005] = "Description",
			[1011] = "Manufacturer",
			[1014] = "License",
			[1015] = "Maintainer",
			[1016] = "Category",
			[1020] = "Homepage",
			[1022] = "Architecture",
			[1000002] = "InstallPath",
			[1064] = "Packager",
			[1000001] = "Listen",
		};

		var metadata = new Dictionary<string, string>();

		foreach(var field in fields.Where(field => tags.ContainsKey(field.Tag)))
			metadata[tags[field.Tag]] = Encoding.UTF8.GetString(bytes, field.Offset, Array.IndexOf(bytes, (byte)0, field.Offset) - field.Offset);

		var installPath = Assert.Single(fields, field => field.Tag == 1000002);
		Assert.Equal(6, installPath.Type);
		Assert.Equal(1, installPath.Count);

		var legacyPath = Assert.Single(fields, field => field.Tag == 1056);
		Assert.Equal(metadata["InstallPath"], Encoding.UTF8.GetString(bytes, legacyPath.Offset, Array.IndexOf(bytes, (byte)0, legacyPath.Offset) - legacyPath.Offset));

		var size = Assert.Single(fields, field => field.Tag is 1009 or 5009);
		metadata["PackageSize"] = (size.Tag == 5009 ? BinaryPrimitives.ReadInt64BigEndian(bytes.AsSpan(size.Offset, 8)) : BinaryPrimitives.ReadUInt32BigEndian(bytes.AsSpan(size.Offset, 4))).ToString(System.Globalization.CultureInfo.InvariantCulture);
		var requires = fields.Single(field => field.Tag == 1049);
		var names = new List<string>();
		var offset = requires.Offset;

		for(int i = 0; i < requires.Count; i++)
		{
			var end = Array.IndexOf(bytes, (byte)0, offset);
			var name = Encoding.UTF8.GetString(bytes, offset, end - offset);

			if(!name.StartsWith("rpmlib(", StringComparison.Ordinal))
				names.Add(name);

			offset = end + 1;
		}

		if(names.Count > 0)
			metadata["Dependencies"] = string.Join("; ", names);

		return metadata;
	}

	private static List<RpmField> ReadRpmFields(byte[] bytes, int header)
	{
		var count = ReadInt(bytes, header + 8);

		return Enumerable.Range(0, count)
			.Select(index => header + 16 + index * 16)
			.Select(offset =>
				new RpmField(
					ReadInt(bytes, offset),
					ReadInt(bytes, offset + 4),
					header + 16 + count * 16 + ReadInt(bytes, offset + 8),
					ReadInt(bytes, offset + 12)
				)
			)
			.ToList();
	}

	private static int ReadInt(byte[] bytes, int offset) => BinaryPrimitives.ReadInt32BigEndian(bytes.AsSpan(offset, 4));
	private sealed record RpmField(int Tag, int Type, int Offset, int Count);
	#endregion
}
