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
using System.Text.Json;
using System.Formats.Tar;
using System.IO.Compression;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text.Json.Serialization.Metadata;

#if CONTAINERIZER
using Zongsoft.Common;
#endif

namespace Zongsoft.Tools.Containerizer.Protocol;

internal static class Files
{
	#region 公共方法
	public static string Hash(string path)
	{
		using var stream = File.OpenRead(path);

		#if CONTAINERIZER
		return global::System.Convert.ToHexString(Checksum.Compute("SHA256", stream).Value.Span).ToLowerInvariant();
		#else
		return global::System.Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
		#endif
	}

	#if CONTAINERIZER
	public static string HashText(string text) => global::System.Convert.ToHexString(Checksum.Compute("SHA256", Encoding.UTF8.GetBytes(text)).Value.Span).ToLowerInvariant();
	#else
	public static string HashText(string text) => global::System.Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text))).ToLowerInvariant();
	#endif

	public static void Write(string path, string text, bool shell = false) => ArtifactPublisher.Write(path, stream =>
	{
		using var writer = new StreamWriter(stream, new UTF8Encoding(false), leaveOpen: true);
		writer.Write(text.ReplaceLineEndings(shell ? "\n" : "\r\n"));
	});

	public static void Save<T>(string path, T value, JsonTypeInfo<T> type) => Write(path, JsonSerializer.Serialize(value, type));
	public static T Load<T>(string path, JsonTypeInfo<T> type)
	{
		using var stream = File.OpenRead(path);
		return JsonSerializer.Deserialize(stream, type) ?? throw new ContainerizationException(4, string.Format(Properties.Resources.Files_1_Message, path));
	}

	public static bool IsLinuxPath(string path) =>
		!string.IsNullOrWhiteSpace(path) && path.StartsWith('/') && path != "/" &&
		!path.Contains('\\') && !path.Contains(':') &&
		!path.Split('/').Any(part => part is ".." or ".");

	public static string NormalizeLinuxPath(string value)
	{
		if(!IsLinuxPath(value))
			throw new ContainerizationException(2, Properties.Resources.ServiceSource_11_Message);

		return value.TrimEnd('/');
	}

	public static string ResolveRelativePath(string root, string relative)
	{
		if(string.IsNullOrWhiteSpace(relative) || relative.StartsWith('/') ||
			relative.Contains('\\') || relative.Contains(':') ||
			relative.Split('/').Any(part => part is ".." or "."))
			throw new ContainerizationException(4, string.Format(Properties.Resources.Files_2_Message, relative));

		var path = Path.GetFullPath(Path.Combine(root, relative));
		var prefix = $"{Path.TrimEndingDirectorySeparator(Path.GetFullPath(root))}{Path.DirectorySeparatorChar}";

		if(!path.StartsWith(prefix, OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal))
			throw new ContainerizationException(4, string.Format(Properties.Resources.Files_3_Message, relative));

		EnsureNoLinks(path);
		return path;
	}

	public static void EnsureNoLinks(string path)
	{
		for(var current = new FileInfo(Path.GetFullPath(path)); current != null; current = current.Directory == null ? null : new FileInfo(current.Directory.FullName))
		{
			if((File.Exists(current.FullName) || Directory.Exists(current.FullName)) && (File.GetAttributes(current.FullName) & FileAttributes.ReparsePoint) != 0)
				throw new ContainerizationException(4, string.Format(Properties.Resources.Files_4_Message, current.FullName));
		}
	}

	public static void Extract(string archive, string destination, long maximum = 100L * 1024 * 1024 * 1024)
	{
		Directory.CreateDirectory(destination);

		using var input = File.OpenRead(archive);
		using var gzip = new GZipStream(input, CompressionMode.Decompress);
		using var reader = new TarReader(gzip);

		long total = 0;
		var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

		while(reader.GetNextEntry() is { } entry)
		{
			if(entry is PaxGlobalExtendedAttributesTarEntry)
				continue;

			var name = entry.Name.TrimEnd('/');
			if(name.Length == 0 || !names.Add(name) || entry.EntryType is not (TarEntryType.Directory or TarEntryType.RegularFile or TarEntryType.V7RegularFile))
				throw new ContainerizationException(4, string.Format(Properties.Resources.Files_5_Message, entry.Name));

			total = checked(total + entry.Length);
			if(total > maximum)
				throw new ContainerizationException(4, Properties.Resources.Files_6_Message);

			var path = ResolveRelativePath(destination, name);

			if(entry.EntryType == TarEntryType.Directory)
				Directory.CreateDirectory(path);
			else
			{
				Directory.CreateDirectory(Path.GetDirectoryName(path));

				using(var output = new FileStream(path, FileMode.CreateNew))
					entry.DataStream?.CopyTo(output);

				if(!OperatingSystem.IsWindows())
					File.SetUnixFileMode(path,
						entry.Mode &
						(
							UnixFileMode.UserRead |
							UnixFileMode.UserWrite |
							UnixFileMode.UserExecute |
							UnixFileMode.GroupRead |
							UnixFileMode.GroupExecute |
							UnixFileMode.OtherRead |
							UnixFileMode.OtherExecute
						));
			}
		}
	}

	public static void Archive(string source, string path)
	{
		ArtifactPublisher.Write(path, output =>
		{
			using var gzip = new GZipStream(output, CompressionLevel.Fastest, true);
			using var writer = new TarWriter(gzip, TarEntryFormat.Pax, true);

			foreach(var file in EnumerateFiles(source).Order(StringComparer.Ordinal))
			{
				EnsureNoLinks(file);
				var relative = Path.GetRelativePath(source, file).Replace('\\', '/');

				using var stream = File.OpenRead(file);
				writer.WriteEntry(new PaxTarEntry(TarEntryType.RegularFile, relative)
				{
					DataStream = stream,
					Mode = relative.EndsWith(".sh", StringComparison.Ordinal) || relative == "containerizer" ? (UnixFileMode)493 : (UnixFileMode)384,
				});
			}
		});
	}

	public static void CopyTree(string source, string target)
	{
		EnsureNoLinks(source);
		EnsureNoLinks(target);
		Directory.CreateDirectory(target);

		foreach(var path in Directory.EnumerateFileSystemEntries(source))
		{
			EnsureNoLinks(path);
			var destination = Path.Combine(target, Path.GetFileName(path));

			if(Directory.Exists(path))
				CopyTree(path, destination);
			else
				File.Copy(path, destination, false);
		}
	}

	public static void CreatePrivateDirectory(string path)
	{
		EnsureNoLinks(path);
		Directory.CreateDirectory(path);

		if(!OperatingSystem.IsWindows())
			File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
	}

	public static void ValidateTree(string directory)
	{
		EnsureNoLinks(directory);

		foreach(var path in Directory.EnumerateFileSystemEntries(directory))
		{
			EnsureNoLinks(path);

			if(Directory.Exists(path))
				ValidateTree(path);
		}
	}

	public static IEnumerable<string> EnumerateFiles(string directory)
	{
		EnsureNoLinks(directory);

		foreach(var path in Directory.EnumerateFileSystemEntries(directory))
		{
			EnsureNoLinks(path);
			if(Directory.Exists(path))
			{
				foreach(var file in EnumerateFiles(path))
					yield return file;
			}
			else
				yield return path;
		}
	}
	#endregion
}
