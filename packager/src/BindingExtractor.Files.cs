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
using System.Collections;
using System.Collections.Generic;

using Microsoft.Extensions.Primitives;
using Microsoft.Extensions.FileProviders;

namespace Zongsoft.Tools.Packager;

partial class BindingExtractor
{
	#region 私有方法
	private static string NormalizePath(string path, string directory = "")
	{
		if(string.IsNullOrEmpty(path))
			return "";

		if(path.Contains(':') || path.StartsWith("//", StringComparison.Ordinal) || path.StartsWith('\\') || path.Any(char.IsControl))
			throw new FormatException();

		path = path.Replace('\\', '/');
		var combined = path.StartsWith('/') ? path : directory + path;
		var segments = new List<string>();

		foreach(var part in combined.Split('/', StringSplitOptions.RemoveEmptyEntries))
		{
			if(part == ".")
				continue;

			if(part == "..")
			{
				if(segments.Count == 0)
					throw new FormatException();

				segments.RemoveAt(segments.Count - 1);
			}
			else
				segments.Add(part);
		}

		return string.Join('/', segments);
	}
	#endregion

	#region 嵌套类型
	/// <summary>只暴露最终安装路径及其条目流，不提供制作机物理路径。</summary>
	private sealed class PackageFiles : IFileProvider
	{
		#region 成员字段
		private readonly Dictionary<string, PackageFile> _files = new(StringComparer.Ordinal);
		#endregion

		#region 构造函数
		public PackageFiles(Package package)
		{
			foreach(var entry in package.Entries)
			{
				var name = entry.Rooted || !string.IsNullOrEmpty(package.EntryPrefix) ?
					entry.EntryName : package.InstallPath.TrimEnd('/') + "/" + entry.EntryName;

				try
				{
					name = NormalizePath(name);
				}
				catch(FormatException)
				{
					throw Error(entry.EntryName, "payload", "virtual-path");
				}

				if(_files.TryGetValue(name, out var existing) && (!entry.IsDirectory || !existing.IsDirectory))
					throw Error(name, "payload", "duplicate-path");

				_files[name] = new(name, entry);
			}

			foreach(var path in _files.Keys.ToArray())
			{
				var index = path.LastIndexOf('/');

				while(index >= 0)
				{
					var directory = path[..index];
					if(_files.TryGetValue(directory, out var existing) && !existing.IsDirectory)
						throw Error(path, "payload", "directory-conflict");

					_files.TryAdd(directory, new(directory));
					index = directory.LastIndexOf('/');
				}
			}

			_files.TryAdd("", new(""));
		}
		#endregion

		#region 公共属性
		public IEnumerable<string> Options => _files
			.Where(item => !item.Value.IsDirectory && item.Key.EndsWith(".option", StringComparison.OrdinalIgnoreCase))
			.Select(item => item.Key).Order(StringComparer.Ordinal);
		#endregion

		#region 公共方法
		public IFileInfo GetFileInfo(string subpath) => _files.TryGetValue(NormalizePath(subpath), out var file) ? file : new NotFoundFileInfo(subpath);
		public IDirectoryContents GetDirectoryContents(string subpath)
		{
			var path = NormalizePath(subpath);
			if(!_files.TryGetValue(path, out var directory) || !directory.IsDirectory)
				return NotFoundDirectoryContents.Singleton;

			var prefix = path.Length == 0 ? "" : path + "/";
			return new DirectoryContents(_files.Where(item => item.Key.StartsWith(prefix, StringComparison.Ordinal) && item.Key.Length > prefix.Length && !item.Key[prefix.Length..].Contains('/'))
				.Select(item => (IFileInfo)item.Value).OrderBy(item => item.Name, StringComparer.Ordinal).ToArray());
		}

		public IChangeToken Watch(string filter) => NullChangeToken.Singleton;
		#endregion

		#region 嵌套类型
		private sealed class PackageFile(string path, Package.Entry? entry = null) : IFileInfo
		{
			public bool Exists => true;
			public long Length => entry?.Size ?? -1;
			public string PhysicalPath => null;
			public string Name => path[(path.LastIndexOf('/') + 1)..];
			public DateTimeOffset LastModified => DateTimeOffset.FromUnixTimeSeconds(entry?.ModifiedTime ?? 0);
			public bool IsDirectory => !entry.HasValue || entry.Value.IsDirectory;

			//Core 会将 FileStream 识别为物理来源，包装流以保持所有导入位于虚拟视图
			public Stream CreateReadStream() => this.IsDirectory ? throw Error(path, "import.path", "directory") : new BufferedStream(entry.Value.OpenRead());
		}

		private sealed class DirectoryContents(IFileInfo[] entries) : IDirectoryContents
		{
			public bool Exists => true;
			public IEnumerator<IFileInfo> GetEnumerator() => ((IEnumerable<IFileInfo>)entries).GetEnumerator();
			IEnumerator IEnumerable.GetEnumerator() => this.GetEnumerator();
		}
		#endregion
	}
	#endregion
}
