/*
 *   _____                                ______
 *  /_   /  ____  ____  ____  _________  / __/ /_
 *    / /  / __ \/ __ \/ __ \/ ___/ __ \/ /_/ __/
 *   / /__/ /_/ / / / /_/ /\_ \/ /_/ / __/ /_
 *  /____/\____/_/ /_/\__  /____/\____/_/  \__/
 *                   /____/
 *
 * Authors:
 *   钟峰(Popeye Zhong) <zongsoft@gmail.com>
 *
 * The MIT License (MIT)
 *
 * Copyright (C) 2026 Zongsoft Corporation <http://www.zongsoft.com>
 *
 * Permission is hereby granted, free of charge, to any person obtaining a copy
 * of this software and associated documentation files (the "Software"), to deal
 * in the Software without restriction, including without limitation the rights
 * to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
 * copies of the Software, and to permit persons to whom the Software is
 * furnished to do so, subject to the following conditions:
 *
 * The above copyright notice and this permission notice shall be included in all
 * copies or substantial portions of the Software.
 *
 * THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
 * IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
 * FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
 * AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
 * LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
 * OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE SOFTWARE.
 */

using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Formats.Tar;
using System.Globalization;
using System.IO.Compression;
using System.Buffers.Binary;
using System.Collections.Generic;

using Zongsoft.Tools.Containerizer.Protocol;

namespace Zongsoft.Tools.Containerizer;

internal static partial class PackageReader
{
	#region 公共方法
	public static string Select(string path, string name, string distribution, string architecture)
	{
		if(File.Exists(path))
		{
			if(Read(path).Architecture != architecture)
				throw new ContainerizationException(2, string.Format(Properties.Resources.PackageReader_1_Message, path));

			return path;
		}

		if(!Directory.Exists(path))
			throw new ContainerizationException(2, string.Format(Properties.Resources.PackageReader_2_Message, path));

		var extension = Distribution.IsDebian(distribution) ? ".deb" : ".rpm";
		string[] directories = [path, Path.Combine(path, ".packages")];
		string[] formats = [extension, ".tar.gz"];

		foreach(var directory in directories)
		{
			if(!Directory.Exists(directory))
				continue;

			foreach(var format in formats)
			{
				var candidates = Directory.EnumerateFiles(directory, $"*{format}")
					.Where(file => Path.GetFileName(file).StartsWith(name, StringComparison.OrdinalIgnoreCase))
					.Where(file => format != ".tar.gz" || File.Exists($"{file[..^7]}.sh"))
					.Where(file => Read(file).Architecture == architecture).Order(StringComparer.Ordinal).ToArray();

				if(candidates.Length > 0)
					return ComponentSelector.Choose(candidates)[0];
			}
		}

		throw new ContainerizationException(2, string.Format(Properties.Resources.PackageReader_3_Message, path));
	}

	public static Descriptor Read(string path, IReadOnlyDictionary<string, string> extraction = null)
	{
		if(!File.Exists(path))
			throw new ContainerizationException(2, string.Format(Properties.Resources.PackageReader_4_Message, path));

		var result = new Descriptor { Extraction = extraction };
		using var stream = File.OpenRead(path);

		if(path.EndsWith(".tar.gz", StringComparison.OrdinalIgnoreCase))
		{
			if(!File.Exists($"{path[..^7]}.sh"))
				throw new ContainerizationException(2, string.Format(Properties.Resources.PackageReader_5_Message, path[..^7]));

			result.Format = "tar";
			ReadTar(stream, result, true);
		}
		else if(path.EndsWith(".deb", StringComparison.OrdinalIgnoreCase))
		{
			result.Format = "deb";
			ReadDeb(stream, result);
		}
		else if(path.EndsWith(".rpm", StringComparison.OrdinalIgnoreCase))
		{
			result.Format = "rpm";
			ReadRpm(stream, result);
		}
		else
			throw new ContainerizationException(2, Properties.Resources.PackageReader_6_Message);

		ContainerManifest.Identity(result.Name);
		ContainerManifest.GetVersionNumber(result.Version);
		result.Architecture = result.Architecture switch
		{
			"amd64" or "x86_64" or "x64" => "x64",
			"aarch64" or "arm64" => "arm64",
			_ => throw new ContainerizationException(2, string.Format(Properties.Resources.PackageReader_7_Message, path))
		};

		ApplicationHealth.ParseListeners(result.Listen, result.Name);
		result.Web = WebPackage.Read(result);

		return result;
	}
	#endregion

	#region 私有方法
	private static string NormalizeName(string name) => name.StartsWith("./", StringComparison.Ordinal) ? name[2..] : name;
	private static bool IsMetadata(string name) => NormalizeName(name) == "control" || name.EndsWith(".service", StringComparison.Ordinal) || name.EndsWith(".runtimeconfig.json", StringComparison.Ordinal) || name.EndsWith("/.version", StringComparison.Ordinal) || name == ".version" || name.Contains("/.web/", StringComparison.Ordinal) && (name.EndsWith("/.bindings", StringComparison.Ordinal) || name.EndsWith(".conf", StringComparison.Ordinal) || name.EndsWith(".conf.template", StringComparison.Ordinal));

	private static long Capture(Descriptor result, string name, Stream stream, long length, bool regular, bool linked = false)
	{
		name = NormalizeName(name).TrimEnd('/');
		if(name.Length == 0 || name == ".")
			return 0;

		var path = result.Format == "tar" && !name.StartsWith('/') ?
			name.StartsWith(".root/", StringComparison.Ordinal) ? name[5..] : $"{result.InstallPath}/{name}" : "/" + name.TrimStart('/');
		if(path.Contains('\\') || path.Split('/').Any(part => part is "." or ".."))
			throw WebPackage.Invalid(result.Name, path);
		if(!result.Entries.TryAdd(path, regular) && regular)
			throw WebPackage.Invalid(result.Name, path);
		if(linked)
			result.Links.Add(path);

		var metadata = IsMetadata(path) || name == "control";
		if(metadata && (!regular || length > 1024 * 1024) && !path.EndsWith("/.web/nginx", StringComparison.Ordinal))
			throw WebPackage.Invalid(result.Name, path);

		if(regular && result.Extraction != null && result.Extraction.TryGetValue(path, out var destination))
		{
			if(length > 16 * 1024 * 1024)
				throw WebPackage.Invalid(result.Name, path);

			var bytes = new byte[(int)length];
			stream.ReadExactly(bytes);
			File.WriteAllBytes(destination, bytes);

			if(!OperatingSystem.IsWindows())
				File.SetUnixFileMode(destination, UnixFileMode.UserRead | UnixFileMode.UserWrite);

			return length;
		}

		if(!regular || !metadata)
			return 0;

		var data = new byte[(int)length];
		stream.ReadExactly(data);

		var text = new UTF8Encoding(false, true).GetString(data);
		result.Texts.Add(name, text);
		result.WebTexts.Add(path, text);

		return length;
	}
	#endregion

	#region 嵌套类型
	internal sealed class Descriptor
	{
		#region 公共属性
		public string Name { get; set; }
		public string Version { get; set; }
		public string Architecture { get; set; }
		public string Format { get; set; }
		public string InstallPath { get; set; }
		public string Listen { get; set; }
		public WebPackage Web { get; set; }
		public IReadOnlyDictionary<string, string> Extraction { get; init; }
		public Dictionary<string, bool> Entries { get; } = new(StringComparer.Ordinal);
		public HashSet<string> Links { get; } = new(StringComparer.Ordinal);
		public Dictionary<string, string> WebTexts { get; } = new(StringComparer.Ordinal);
		public Dictionary<string, string> Texts { get; } = new(StringComparer.Ordinal);
		#endregion
	}
	#endregion
}
