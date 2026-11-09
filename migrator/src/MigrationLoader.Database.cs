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
using System.Text;
using System.Collections.Generic;
using System.Security.Cryptography;

namespace Zongsoft.Tools.Migrator.Migration;

partial class MigrationLoader
{
	private sealed partial class Database
	{
		#region 成员字段
		private readonly MigrationPlan.Step _step;
		private readonly string _directory;
		private readonly Dictionary<string, int> _indexes;
		private readonly HashSet<string> _selected;
		#endregion

		#region 构造函数
		public Database(MigrationPlan.Step step, string directory, Dictionary<string, int> indexes, HashSet<string> selected)
		{
			_step = step;
			_directory = directory;
			_indexes = indexes;
			_selected = selected;
		}
		#endregion

		#region 公共方法
		public void Add(string name, string value)
		{
			if(!string.IsNullOrEmpty(value))
				throw new InvalidDataException(Properties.Resources.MigrationDatabaseEntryInvalid_Message);

			foreach(var sql in GetFiles(_directory, name))
			{
				if(!_selected.Add(sql))
					continue;
				if(!sql.EndsWith(".sql", StringComparison.OrdinalIgnoreCase))
					throw new InvalidDataException(Properties.Resources.MigrationSqlExtension_Message);

				foreach(var content in Read(File.ReadAllText(sql), _step.Provider))
				{
					var index = _indexes.GetValueOrDefault(_step.Provider) + 1;
					_indexes[_step.Provider] = index;
					_step.Scripts.Add(new()
					{
						Source = sql,
						Content = content,
						Path = $".artifacts/{_step.Provider}/{index.ToString(System.Globalization.CultureInfo.InvariantCulture)}.sql",
						Checksum = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(content))),
					});
				}
			}
		}
		#endregion

		#region 私有方法
		private static IEnumerable<string> GetFiles(string directory, string pattern)
		{
			var path = Path.GetFullPath(Path.Combine(directory, pattern));
			var files = Utility.Search(path, files: true, sourceDirectory: directory).Select(match => match.Path).ToArray();

			if(files.Length == 0)
				throw new FileNotFoundException(string.Format(Properties.Resources.MigrationScriptsMissing_Message, path));

			return files;
		}
		#endregion
	}
}
