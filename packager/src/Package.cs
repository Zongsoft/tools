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
		this.Url = "https://github.com/Zongsoft";
		this.Maintainer = "Zongsoft";
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
	public Entry[] Entries { get; set; }
	public InstallScripts Scripts { get; set; }
	#endregion

	#region 内部属性
	internal abstract string FileName { get; }
	internal virtual string EntryPrefix => this.InstallPath.TrimStart('/');
	#endregion

	#region 公共方法
	public abstract void Pack(string output);
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
	public readonly struct Entry(string source, string entryName, long size, long modifiedTime, int mode)
	{
		public readonly string Source = source;
		public readonly string EntryName = entryName;
		public readonly long Size = size;
		public readonly long ModifiedTime = modifiedTime;
		public readonly int Mode = mode;

		public override string ToString() => string.IsNullOrEmpty(this.Source) ?
			$"{this.EntryName}({this.Size})" :
			$"[{this.Source}] {this.EntryName}({this.Size})";
	}

	public readonly record struct InstallScripts(
		string Installing,
		string Installed,
		string Uninstalling,
		string Uninstalled);
	#endregion
}
