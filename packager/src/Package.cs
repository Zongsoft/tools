/*
 *   _____                                ______
 *  /_   /  ____  ____  ____  _________  / __/ /_
 *    / /  / __ \/ __ \/ __ \/ ___/ __ \/ /_/ __/
 *   / /__/ /_/ / / / / /_/ /\_ \/ /_/ / __/ /_
 *  /____/\____/_/ /_/\__  /____/\____/_/  \__/
 *                   /____/
 *
 * Authors:
 *   钟峰(Popeye Zhong) <zongsoft@gmail.com>
 *
 * The MIT License (MIT)
 * 
 * Copyright (C) 2020-2026 Zongsoft Corporation <http://www.zongsoft.com>
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
using System.Collections;
using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace Zongsoft.Tools.Packager;

public abstract partial class Package
{
	#region 构造函数
	protected Package(string name, string edition, Version version, Platform platform, Architecture architecture)
	{
		ArgumentException.ThrowIfNullOrEmpty(name);

		if(version.IsZero())
			throw new ArgumentOutOfRangeException(nameof(version));

		this.Name = name;
		this.Edition = edition;
		this.Version = version;
		this.Platform = platform;
		this.Architecture = architecture;
		this.Runtime = Utility.GetRuntimeIdentifier(platform, architecture);
		this.PackageName = GetPackageName(name, edition);
		this.Framework = Normalizer.Variables.Framework;
		this.Title = Normalizer.Variables.Title;
		this.Summary = Normalizer.Variables.Summary;
		this.Description = Normalizer.Variables.Description;
		this.Url = Normalizer.Variables.Url;
		this.Category = Normalizer.Variables.Category;
		this.License = Normalizer.Variables.License;
		this.Maintainer = Normalizer.Variables.Maintainer;
		this.Dependencies = Normalizer.Variables.Dependencies;
		this.Entries = new(this);
	}
	#endregion

	#region 公共属性
	public string Name { get; }
	public string PackageName { get; }
	public string Edition { get; }
	public Version Version { get; }
	public string Runtime { get; }
	public Platform Platform { get; }
	public Architecture Architecture { get; }
	public string Framework { get; set; }
	public string Title { get; set; }
	public string Summary { get; set; }
	public string Description { get; set; }
	public string Maintainer { get; set; }
	public string License { get; set; }
	public string Url { get; set; }
	public string Category { get; set; }
	public string InstallPath { get; set; }
	public string[] Dependencies { get; set; }
	public EntryCollection Entries { get; }
	public InstallScripts Scripts { get; set; }
	#endregion

	#region 内部属性
	internal abstract string FileName { get; }
	public IScriptor Scriptor { get; protected set; }
	internal virtual string EntryPrefix => this.InstallPath.TrimStart('/');
	#endregion

	#region 公共方法
	public abstract void Pack(string output, bool overwrite);
	#endregion

	#region 重写方法
	public override string ToString() => string.IsNullOrEmpty(this.Edition) ?
		$"{this.Name}@{this.Version}_{this.Runtime}":
		$"{this.Name}-{this.Edition}@{this.Version}_{this.Runtime}";
	#endregion

	#region 内部方法
	internal long GetPackageSize()
	{
		long result = 0;

		foreach(var entry in this.Entries)
			result += entry.Size;

		return result;
	}

	internal static string GetPackageName(string name, string edition) => string.IsNullOrEmpty(edition) ? name : $"{name}-{edition}";
	protected string GetFileName(string extension)
	{
		var name = string.IsNullOrEmpty(this.Edition) ?
			$"{this.Name}@{this.Version}_{this.Runtime}" :
			$"{this.Name}-{this.Edition}@{this.Version}_{this.Runtime}";

		if(string.IsNullOrEmpty(extension) || extension == ".")
			return name;

		return extension[0] == '.' ? $"{name}{extension}" : $"{name}.{extension}";
	}
	#endregion

	#region 嵌套结构
	public readonly record struct InstallScripts(
		string Installing,
		string Installed,
		string Uninstalling,
		string Uninstalled);

	public readonly struct Entry(string source, string entryName, long size, long modifiedTime, int mode, bool rooted)
	{
		public readonly string Source = source;
		public readonly string EntryName = entryName;
		public readonly long Size = size;
		public readonly long ModifiedTime = modifiedTime;
		public readonly int Mode = mode;
		public readonly bool Rooted = rooted;

		public override string ToString() => string.IsNullOrEmpty(this.Source) ?
			$"{this.EntryName}({this.Size})" :
			$"[{this.Source}] {this.EntryName}({this.Size})";
	}

	public sealed class EntryCollection(Package package) : IReadOnlyCollection<Entry>
	{
		private readonly Package _package = package;
		private readonly List<Entry> _entries = new();

		public int Count => _entries.Count;

		internal void Add(string source, string argument)
		{
			if(!Normalizer.TryNormalize(argument, out var text))
				return;

			var index = text.LastIndexOf(':');

			if(OperatingSystem.IsWindows() && index == 1)
				index = -1;

			var path = index > 0 ? text[..index].Trim() : text;
			var alias = index > 0 ? text[(index + 1)..].Trim() : null;

			AddEntry(_entries, null, source, path, alias, _package.EntryPrefix);
		}

		internal void Load(string source, IReadOnlyCollection<string> arguments)
		{
			var names = new HashSet<string>(StringComparer.Ordinal);

			if(arguments == null || arguments.Count == 0)
			{
				foreach(var file in Directory.GetFiles(source, "*", SearchOption.AllDirectories))
					AddEntry(_entries, names, source, file, Path.GetRelativePath(source, file), _package.EntryPrefix);

				return;
			}

			foreach(var argument in arguments)
			{
				if(!Normalizer.TryNormalize(argument, out var text))
					continue;

				var index = text.LastIndexOf(':');

				if(OperatingSystem.IsWindows() && index == 1)
					index = -1;

				var path = index > 0 ? text[..index].Trim() : text;
				var alias = index > 0 ? text[(index + 1)..].Trim() : null;

				AddEntry(_entries, names, source, path, alias, _package.EntryPrefix);
			}
		}

		static void AddEntry(List<Package.Entry> entries, ISet<string> names, string source, string path, string alias, string prefix)
		{
			var rooted = IsRootedAlias(alias);

			if(alias != null)
				alias = alias
					.Trim('~')
					.Trim(Path.DirectorySeparatorChar)
					.Trim(Path.AltDirectorySeparatorChar);
			else if(Utility.IsExternal(source, path))
				alias = string.Empty;

			if(!Path.IsPathFullyQualified(path))
				path = Path.Combine(source, path);

			if(path.Contains('*') || path.Contains('?'))
			{
				var working = Path.GetDirectoryName(path);
				var pattern = Path.GetFileName(path);

				if(string.IsNullOrEmpty(working) || !Directory.Exists(working))
				{
					Dumper.PathNotExist(path);
					return;
				}

				alias ??= Path.GetRelativePath(source, working);

				if(alias == "." || alias.StartsWith(".."))
					alias = string.Empty;

				foreach(var file in Directory.GetFiles(working, pattern))
					AddFile(entries, names, file, Path.Combine(alias, Path.GetFileName(file)), rooted ? null : prefix, rooted);

				foreach(var directory in Directory.GetDirectories(working, pattern))
					AddDirectory(entries, names, source, directory, Path.Combine(alias, Path.GetFileName(directory)), rooted ? null : prefix, rooted);
			}
			else
			{
				alias ??= Path.GetRelativePath(source, path);

				if(alias == "." || alias.StartsWith(".."))
					alias = string.Empty;

				if(File.Exists(path))
					AddFile(entries, names, path, alias, rooted ? null : prefix, rooted);
				else if(Directory.Exists(path))
					AddDirectory(entries, names, source, path, alias, rooted ? null : prefix, rooted);
				else
					Dumper.PathNotExist(path);
			}
		}

		static bool IsRootedAlias(string alias) => !string.IsNullOrEmpty(alias) && (alias[0] == '/' || alias[0] == '\\');

		static void AddDirectory(List<Package.Entry> entries, ISet<string> names, string source, string path, string alias, string prefix, bool rooted)
		{
			foreach(var file in Directory.GetFiles(path))
				AddFile(entries, names, file, Path.Combine(alias, Path.GetFileName(file)), prefix, rooted);

			foreach(var directory in Directory.GetDirectories(path))
				AddDirectory(entries, names, source, directory, Path.Combine(alias, Path.GetFileName(directory)), prefix, rooted);
		}

		static void AddFile(List<Package.Entry> entries, ISet<string> names, string source, string entryName, string prefix, bool rooted)
		{
			if(string.IsNullOrEmpty(entryName))
				entryName = Path.GetFileName(source);
			else
			{
				var filename = Path.GetFileName(entryName);

				if(string.IsNullOrEmpty(filename) || filename == ".")
					entryName = Path.Combine(Path.GetDirectoryName(entryName), Path.GetFileName(source));
			}

			entryName = Utility.NormalizePath(Path.Combine(prefix ?? string.Empty, entryName));
			var key = rooted ? $"/{entryName}" : entryName;

			if(names != null && !names.Add(key))
			{
				Dumper.PackageEntryConflicted(source, entryName);
				return;
			}

			var file = new FileInfo(source);
			entries.Add(new Package.Entry(source, entryName, file.Length, Utility.Unix.GetTimestamp(file.LastWriteTimeUtc), Utility.Unix.GetFileMode(source), rooted));
		}

		IEnumerator IEnumerable.GetEnumerator() => this.GetEnumerator();
		public IEnumerator<Entry> GetEnumerator() => _entries.GetEnumerator();
	}
	#endregion
}
