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
using System.Text.RegularExpressions;
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
		private readonly Dictionary<string, Entry> _entries = new(StringComparer.Ordinal);

		public int Count => _entries.Count;
		public bool Contains(string name) => name != null && _entries.ContainsKey(name);

		internal void Add(string source, string argument)
		{
			if(!Normalizer.TryNormalize(argument, out var text))
				return;

			var index = text.LastIndexOf(':');

			if(OperatingSystem.IsWindows() && index == 1)
				index = -1;

			var path = index > 0 ? text[..index].Trim() : text;
			var alias = index > 0 ? text[(index + 1)..].Trim() : null;

			this.AddEntry(source, path, alias, _package.EntryPrefix);
		}

		internal void Load(string source, IReadOnlyCollection<string> arguments, IEnumerable<string> exclusions = null)
		{
			var exclusion = EntryExclusion.Create(source, exclusions);

			if(arguments == null || arguments.Count == 0)
			{
				foreach(var file in Directory.GetFiles(source, "*", SearchOption.AllDirectories))
					this.AddEntry(source, file, Path.GetRelativePath(source, file), _package.EntryPrefix, exclusion);

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

				this.AddEntry(source, path, alias, _package.EntryPrefix, exclusion);
			}
		}

		void AddEntry(string source, string path, string alias, string prefix) => this.AddEntry(source, path, alias, prefix, null);

		void AddEntry(string source, string path, string alias, string prefix, EntryExclusion exclusion)
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
					this.AddFile(file, Path.Combine(alias, Path.GetFileName(file)), rooted ? null : prefix, rooted, exclusion);

				foreach(var directory in Directory.GetDirectories(working, pattern))
					this.AddDirectory(source, directory, Path.Combine(alias, Path.GetFileName(directory)), rooted ? null : prefix, rooted, exclusion);
			}
			else
			{
				alias ??= Path.GetRelativePath(source, path);

				if(alias == "." || alias.StartsWith(".."))
					alias = string.Empty;

				if(File.Exists(path))
					this.AddFile(path, alias, rooted ? null : prefix, rooted, exclusion);
				else if(Directory.Exists(path))
					this.AddDirectory(source, path, alias, rooted ? null : prefix, rooted, exclusion);
				else
					Dumper.PathNotExist(path);
			}

			static bool IsRootedAlias(string alias) => !string.IsNullOrEmpty(alias) && (alias[0] == '/' || alias[0] == '\\');
		}

		void AddDirectory(string source, string path, string alias, string prefix, bool rooted, EntryExclusion exclusion)
		{
			foreach(var file in Directory.GetFiles(path))
				this.AddFile(file, Path.Combine(alias, Path.GetFileName(file)), prefix, rooted, exclusion);

			foreach(var directory in Directory.GetDirectories(path))
				this.AddDirectory(source, directory, Path.Combine(alias, Path.GetFileName(directory)), prefix, rooted, exclusion);
		}

		void AddFile(string source, string entryName, string prefix, bool rooted, EntryExclusion exclusion)
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

			if(exclusion != null && exclusion.IsMatch(source, entryName))
				return;

			var key = rooted ? $"/{entryName}" : entryName;

			if(_entries.ContainsKey(key))
			{
				Dumper.PackageEntryConflicted(source, entryName);
				return;
			}

			var file = new FileInfo(source);
			_entries.Add(key, new(source, entryName, file.Length, Utility.Unix.GetTimestamp(file.LastWriteTimeUtc), Utility.Unix.GetFileMode(source), rooted));
		}

		IEnumerator IEnumerable.GetEnumerator() => this.GetEnumerator();
		public IEnumerator<Entry> GetEnumerator() => _entries.Values.GetEnumerator();

		sealed class EntryExclusion
		{
			private readonly string _source;
			private readonly Pattern[] _patterns;

			private EntryExclusion(string source, Pattern[] patterns)
			{
				_source = Utility.NormalizePath(Path.GetFullPath(source));
				_patterns = patterns;
			}

			public static EntryExclusion Create(string source, IEnumerable<string> exclusions)
			{
				if(exclusions == null)
					return null;

				var patterns = new List<Pattern>();

				foreach(var exclusion in exclusions)
				{
					if(string.IsNullOrWhiteSpace(exclusion))
						continue;

					if(!Normalizer.TryNormalize(exclusion, out var text) || string.IsNullOrWhiteSpace(text))
						continue;

					foreach(var pattern in text.Split([',', ';'], StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
						patterns.Add(new(pattern));
				}

				return patterns.Count == 0 ? null : new(source, patterns.ToArray());
			}

			public bool IsMatch(string source, string entryName)
			{
				var packagePath = Utility.NormalizePath(entryName).TrimStart('/');
				var sourcePath = Utility.NormalizePath(Path.GetFullPath(source));
				var sourceRelativePath = sourcePath.StartsWith(_source + "/", StringComparison.OrdinalIgnoreCase) ?
					sourcePath[(_source.Length + 1)..] :
					sourcePath;
				var fileName = Path.GetFileName(sourcePath);

				foreach(var pattern in _patterns)
				{
					if(pattern.IsMatch(packagePath) ||
					   pattern.IsMatch(sourceRelativePath) ||
					   pattern.IsMatch(fileName) ||
					   pattern.IsMatch(sourcePath))
						return true;
				}

				return false;
			}

			sealed class Pattern
			{
				private readonly Regex _regex;
				private readonly Regex _fileNameRegex;

				public Pattern(string text)
				{
					var pattern = Utility.NormalizePath(text.Trim());

					if(pattern.EndsWith("/", StringComparison.Ordinal))
						pattern += "**";

					pattern = pattern.TrimStart('/');
					_regex = new Regex(ToRegexPattern(pattern), RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
					_fileNameRegex = pattern.Contains('/') ? null : _regex;
				}

				public bool IsMatch(string path)
				{
					if(string.IsNullOrEmpty(path))
						return false;

					path = Utility.NormalizePath(path).TrimStart('/');

					return _regex.IsMatch(path) || (_fileNameRegex != null && _fileNameRegex.IsMatch(Path.GetFileName(path)));
				}

				static string ToRegexPattern(string pattern)
				{
					var result = new System.Text.StringBuilder();

					result.Append('^');

					for(int i = 0; i < pattern.Length; i++)
					{
						var character = pattern[i];

						if(character == '*')
						{
							if(i + 1 < pattern.Length && pattern[i + 1] == '*')
							{
								if(i + 2 < pattern.Length && pattern[i + 2] == '/')
								{
									result.Append("(?:.*/)?");
									i += 2;
								}
								else
								{
									result.Append(".*");
									i++;
								}
							}
							else
							{
								result.Append("[^/]*");
							}
						}
						else if(character == '?')
						{
							result.Append("[^/]");
						}
						else
						{
							result.Append(Regex.Escape(character.ToString()));
						}
					}

					result.Append("(?:/.*)?$");
					return result.ToString();
				}
			}
		}
	}
	#endregion
}
