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
using System.Collections.Generic;

using Zongsoft.Configuration.Profiles;

namespace Zongsoft.Tools.Migrator.Migration;

/// <summary>Validates each migration source and loads INI through the Core Profile parser.</summary>
internal static class MigrationProfile
{
	#region 公共方法
	public static Profile Load(string path, Action<Profile> validate = null, bool migration = false)
	{
		path = Path.GetFullPath(path);
		var paths = new Stack<string>();
		paths.Push(path);

		try
		{
			Validate(path, migration);

			var options = new ProfileOptions
			{
				Importing = context =>
				{
					paths.Push(context.FilePath);
					Validate(context.FilePath, migration);
				},
				Imported = context =>
				{
					validate?.Invoke(context.Profile);
					paths.Pop();
				},
			};

			var profile = Profile.Load(path, options);
			validate?.Invoke(profile);
			return profile;
		}
		catch(Exception ex) when(ex is ArgumentException or ProfileException)
		{
			// Parser messages can contain parameter text. Report the active source without values.
			throw new InvalidDataException(string.Format(Properties.Resources.MigrationProfileInvalid_Message, paths.Peek()));
		}
	}
	#endregion

	#region 私有方法
	private static void Validate(string path, bool migration)
	{
		if(migration && !path.EndsWith(".migration", StringComparison.OrdinalIgnoreCase))
			throw new InvalidDataException(string.Format(Properties.Resources.MigrationFileExtension_Message, path));

		var lines = File.ReadAllLines(path);
		var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

		for(var i = 0; i < lines.Length; i++)
		{
			var text = lines[i].Trim();
			if(!text.StartsWith('['))
				continue;

			if(!text.EndsWith(']') || text.Length < 3)
				throw new InvalidDataException(string.Format(Properties.Resources.MigrationSectionInvalid_Message, path, i + 1));

			var parts = text[1..^1].Split([' ', '\t'], StringSplitOptions.RemoveEmptyEntries);
			var name = string.Join(" ", parts);
			if(parts.Length == 0 || parts.Length > (migration ? 2 : 3))
				throw new InvalidDataException(string.Format(Properties.Resources.MigrationSectionInvalid_Message, path, i + 1));

			if(!names.Add(name))
				throw new InvalidDataException(string.Format(Properties.Resources.MigrationProfileSectionDuplicate_Message, name, path, i + 1));
		}
	}
	#endregion
}
