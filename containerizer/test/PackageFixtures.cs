using System;
using System.IO;
using System.Text;
using System.Formats.Tar;
using System.Globalization;
using System.IO.Compression;
using System.Buffers.Binary;
using System.Collections.Generic;

namespace Zongsoft.Tools.Containerizer.Tests;

internal static class PackageFixtures
{
	internal static string Write(string path, string name, Dictionary<string, string> entries)
	{
		using var output = File.Create(path);
		if(path.EndsWith(".tar.gz", StringComparison.Ordinal))
		{
			output.Write(Tar(entries, new() { ["PackageName"] = name, ["Architecture"] = "x64", ["Version"] = "1.0.0", ["InstallPath"] = "/opt/" + name }));
			File.WriteAllText(path[..^7] + ".sh", "#!/bin/sh\nexit 0\n");
		}
		else
		{
			var files = new Dictionary<string, string>();
			foreach(var pair in entries)
				files[pair.Key.StartsWith(".root/", StringComparison.Ordinal) ? pair.Key[6..] : "opt/" + name + "/" + pair.Key] = pair.Value;

			if(path.EndsWith(".deb", StringComparison.Ordinal))
			{
				output.Write("!<arch>\n"u8);
				Ar(output, "debian-binary", "2.0\n"u8.ToArray());
				Ar(output, "control.tar.gz", Tar(new() { ["control"] = $"Package: {name}\nVersion: 1.0.0\nArchitecture: amd64\nInstallPath: /opt/{name}\n" }));
				Ar(output, "data.tar.gz", Tar(files));
			}
			else
			{
				var lead = new byte[96];
				new byte[] { 0xed, 0xab, 0xee, 0xdb }.CopyTo(lead, 0);
				output.Write(lead);
				Header(output, []);
				Header(output, new() { [1000] = name, [1001] = "1.0.0", [1022] = "x86_64", [1125] = "gzip", [1000003] = "/opt/" + name });
				using var gzip = new GZipStream(output, CompressionLevel.Optimal, true);

				foreach(var pair in files)
					Cpio(gzip, pair.Key, Encoding.UTF8.GetBytes(pair.Value));
				Cpio(gzip, "TRAILER!!!", []);
			}
		}

		return path;
	}

	private static byte[] Tar(Dictionary<string, string> files, Dictionary<string, string> metadata = null)
	{
		using var output = new MemoryStream();
		using(var gzip = new GZipStream(output, CompressionLevel.Optimal, true))
		using(var writer = new TarWriter(gzip))
		{
			if(metadata != null)
				writer.WriteEntry(new PaxGlobalExtendedAttributesTarEntry(metadata));

			foreach(var pair in files)
			{
				using var content = new MemoryStream(Encoding.UTF8.GetBytes(pair.Value));
				writer.WriteEntry(new PaxTarEntry(TarEntryType.RegularFile, pair.Key) { DataStream = content });
			}
		}

		return output.ToArray();
	}

	private static void Ar(Stream output, string name, byte[] content)
	{
		var header = name.PadRight(16) + "0".PadRight(12) + "0".PadRight(6) + "0".PadRight(6) + "100644".PadRight(8) + content.Length.ToString(CultureInfo.InvariantCulture).PadRight(10) + "`\n";
		output.Write(Encoding.ASCII.GetBytes(header));
		output.Write(content);

		if(content.Length % 2 != 0)
			output.WriteByte(10);
	}

	private static void Header(Stream output, Dictionary<int, string> fields)
	{
		using var data = new MemoryStream();
		using var indices = new MemoryStream();

		foreach(var pair in fields)
		{
			var entry = new byte[16];
			BinaryPrimitives.WriteInt32BigEndian(entry, pair.Key);
			BinaryPrimitives.WriteInt32BigEndian(entry.AsSpan(4), 6);
			BinaryPrimitives.WriteInt32BigEndian(entry.AsSpan(8), (int)data.Length);
			BinaryPrimitives.WriteInt32BigEndian(entry.AsSpan(12), 1);
			indices.Write(entry);
			data.Write(Encoding.UTF8.GetBytes(pair.Value + '\0'));
		}

		var header = new byte[16];
		new byte[] { 0x8e, 0xad, 0xe8, 1 }.CopyTo(header, 0);
		BinaryPrimitives.WriteInt32BigEndian(header.AsSpan(8), fields.Count);
		BinaryPrimitives.WriteInt32BigEndian(header.AsSpan(12), (int)data.Length);
		output.Write(header);
		output.Write(indices.ToArray());
		output.Write(data.ToArray());
	}

	private static void Cpio(Stream output, string name, byte[] content)
	{
		var bytes = Encoding.UTF8.GetBytes(name + '\0');
		var fields = new uint[] { 1, 0x81a4, 0, 0, 1, 0, (uint)content.Length, 0, 0, 0, 0, (uint)bytes.Length, 0 };
		var header = new StringBuilder("070701");

		foreach(var field in fields)
			header.Append(field.ToString("x8", CultureInfo.InvariantCulture));

		output.Write(Encoding.ASCII.GetBytes(header.ToString()));
		output.Write(bytes);
		output.Write(new byte[(4 - (110 + bytes.Length) % 4) % 4]);
		output.Write(content);
		output.Write(new byte[(4 - content.Length % 4) % 4]);
	}
}
