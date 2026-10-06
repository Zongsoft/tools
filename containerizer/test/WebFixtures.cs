using System;
using System.IO;
using System.Text;
using System.Formats.Tar;
using System.IO.Compression;
using System.Collections.Generic;

namespace Zongsoft.Tools.Containerizer.Tests;

internal static class WebFixtures
{
	internal static string WritePackage(string path, string name, string bindings, string template, Dictionary<string, string> files = null)
	{
		var entries = new Dictionary<string, string>
		{
			[$".root/etc/systemd/system/{name}.service"] = $"[Service]\nWorkingDirectory=/opt/{name}\nExecStart=/opt/{name}/{name} --urls http://127.0.0.1:8069\n",
			[$".web/nginx/{name}.conf"] = "server { listen 8080; }\n",
		};

		if(bindings != null)
			entries[".web/nginx/.bindings"] = bindings;
		if(template != null)
			entries[$".web/nginx/{name}.conf.template"] = template;

		if(files != null)
		{
			foreach(var pair in files)
				entries[pair.Key] = pair.Value;
		}

		using(var output = File.Create(path))
		using(var gzip = new GZipStream(output, CompressionLevel.Optimal))
		using(var writer = new TarWriter(gzip))
		{
			writer.WriteEntry(new PaxGlobalExtendedAttributesTarEntry(new Dictionary<string, string>
			{
				["PackageName"] = name,
				["Architecture"] = "x64",
				["Version"] = "1.0.0",
				["InstallPath"] = "/opt/" + name,
				["Listen"] = "http://127.0.0.1:8069",
			}));

			foreach(var pair in entries)
			{
				using var content = new MemoryStream(Encoding.UTF8.GetBytes(pair.Value));
				writer.WriteEntry(new PaxTarEntry(TarEntryType.RegularFile, pair.Key) { DataStream = content });
			}
		}

		File.WriteAllText(path[..^7] + ".sh", "#!/bin/sh\nexit 0\n");
		return path;
	}
}
