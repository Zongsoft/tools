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
using System.Linq;
using System.Text;
using System.Collections.Generic;
using System.Security.Cryptography;

namespace Zongsoft.Tools.Packager.Migration;

partial class MigrationLoader
{
	private sealed partial class Database
	{
		#region 成员字段
		private readonly MigrationPlan.Step _task;
		private readonly string _directory;
		private readonly HashSet<string> _selected = new(OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);
		#endregion

		#region 构造函数
		public Database(MigrationPlan.Step task, string directory)
		{
			_task = task;
			_directory = directory;
		}
		#endregion

		#region 公共方法
		public void Add(string name, string value)
		{
			if(!string.IsNullOrEmpty(value))
				throw new InvalidDataException(Properties.Resources.MigrationDatabaseEntryInvalid);

			foreach(var sql in GetFiles(_directory, name))
			{
				if(!_selected.Add(sql))
					continue;
				if(!sql.EndsWith(".sql", StringComparison.OrdinalIgnoreCase))
					throw new InvalidDataException(Properties.Resources.MigrationSqlExtension);

				foreach(var content in Read(File.ReadAllText(sql), _task.Provider))
					_task.Scripts.Add(new()
					{
						Source = sql,
						Content = content,
						Path = $".migration/.artifacts/{_task.Id}/{_task.Scripts.Count + 1:D4}.sql",
						Checksum = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(content))),
					});
			}
		}
		#endregion

		#region 私有方法
		private static IEnumerable<string> GetFiles(string directory, string pattern)
		{
			var path = Path.GetFullPath(Path.Combine(directory, pattern));
			var parent = Path.GetDirectoryName(path);

			if(parent.IndexOfAny(['*', '?']) >= 0)
				throw new InvalidDataException(Properties.Resources.MigrationWildcardInvalid);

			var files = Directory.Exists(parent) ? Directory.GetFiles(parent, Path.GetFileName(path)).OrderBy(file => Path.GetRelativePath(directory, file).Replace('\\', '/'), StringComparer.Ordinal).ToArray() : [];

			if(files.Length == 0)
				throw new FileNotFoundException(string.Format(Properties.Resources.MigrationScriptsMissing, path));

			return files;
		}
		#endregion
	}
}
