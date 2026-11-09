/*
 *   _____                                ______
 *  /_   /  ____  ____  ____  _________  / __/ /_
 *    / /  / __ \/ __ \/ __ \/ ___/ __ \/ /_/ __/
 *   / /__/ /_/ / / / / /_/ /\_ \/ /_/ / __/ /_
 *  /____/\____/_/ /_/\__  /____/\____/_/  \__/
 *                   /____/
 *
 * Authors:
 *   钟峰(Popeye Zhong) <zongsoft@qq.com>
 *
 * Copyright (c) Zongsoft Studio. All rights reserved.
 * The above copyright notice and this permission notice shall be included in all
 * AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
 *
 *
 * Licensed under the MIT License (see LICENSE).
 */

using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;

namespace Zongsoft.Tools.Packager;

partial class Generator
{
	#region 私有方法
	private static Package.Entry[] GetPackageDirectories(IEnumerable<Package.Entry> entries, bool separateRooted = false)
	{
		var directories = new Dictionary<string, Package.Entry>(StringComparer.Ordinal);
		var files = new HashSet<string>(StringComparer.Ordinal);

		foreach(var entry in entries)
		{
			var key = (separateRooted && entry.Rooted ? "/" : "") + entry.EntryName;
			if(entry.IsDirectory)
				directories[key] = entry;
			else
				files.Add(key);

			var path = entry.EntryName;
			var index = path.LastIndexOf('/');

			while(index > 0)
			{
				path = path[..index];
				key = (separateRooted && entry.Rooted ? "/" : "") + path;
				directories.TryAdd(key, new(null, path, 0, entry.ModifiedTime, Utility.Unix.Mode755, entry.Rooted, true));
				index = path.LastIndexOf('/');
			}
		}

		foreach(var path in directories.Keys)
		{
			if(files.Contains(path))
				throw new InvalidOperationException(string.Format(Properties.Resources.PackageEntryTypeConflict_Message, path));
		}

		return directories.Values.OrderBy(entry => entry.EntryName.Count(character => character == '/')).ThenBy(entry => entry.EntryName, StringComparer.Ordinal).ToArray();
	}
	#endregion

	#region 嵌套类型
	private sealed class Buffer() : FileStream(Path.Combine(Path.GetTempPath(), "zongsoft-packager-" + Path.GetRandomFileName()), CreateOptions())
	{
		private static FileStreamOptions CreateOptions()
		{
			var options = new FileStreamOptions
			{
				Mode = FileMode.CreateNew,
				Access = FileAccess.ReadWrite,
				Share = FileShare.None,
				BufferSize = 65536,
				Options = FileOptions.DeleteOnClose | FileOptions.SequentialScan,
			};

			if(!OperatingSystem.IsWindows())
				options.UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;

			return options;
		}
	}
	#endregion
}
