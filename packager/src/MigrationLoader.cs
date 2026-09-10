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

using Zongsoft.Terminals;
using Zongsoft.Components;

namespace Zongsoft.Tools.Packager.Migration;

public sealed partial class MigrationLoader(Func<string, string> expand, Action<string> warning = null)
{
	#region 成员字段
	private readonly Func<string, string> _expand = expand ?? (value => value);
	private readonly Action<string> _warning = warning ?? (message => Terminal.WriteLine(CommandOutletColor.DarkYellow, message));
	#endregion

	#region 公共方法
	public MigrationPlan Load(string paths, string source, string package, string version)
	{
		var plan = new MigrationPlan { Package = package, Version = version };
		var found = false;

		foreach(var file in this.GetMigrationFiles(paths, source))
		{
			found = true;
			var profile = MigrationProfile.Load(file);
			if(profile.Entries.Count > 0)
				throw new InvalidDataException(string.Format(Properties.Resources.MigrationSectionRequired, file));

			var providers = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

			foreach(var section in profile.Sections)
			{
				try
				{
					var provider = MigrationProvider.Get(section.Name).Name;
					if(!providers.Add(provider))
						throw new InvalidDataException(Properties.Resources.MigrationSectionDuplicate);

					if(section.Entries.Count == 0)
						continue;

					var task = new MigrationPlan.Step
					{
						Id = $"{plan.Tasks.Count + 1:D4}-{provider}",
						Provider = provider,
						Parameters = this.FindParameters(file, provider),
					};

					Action<string, string> add = provider == "amazon.s3" ?
						new AmazonS3(task).Add : new Database(task, Path.GetDirectoryName(file)).Add;

					foreach(var entry in section.Entries)
					{
						try
						{
							add(_expand(entry.Name), _expand(entry.Value));
						}
						catch(Exception ex) when(ex is not OutOfMemoryException)
						{
							throw new InvalidDataException(string.Format(Properties.Resources.MigrationEntryError, file, entry.LineNumber + 1, provider, ex.Message), ex);
						}
					}

					plan.Tasks.Add(task);
				}
				catch(Exception ex) when(ex is not OutOfMemoryException)
				{
					throw new InvalidDataException(string.Format(Properties.Resources.MigrationEntryError, file, section.LineNumber + 1, section.Name, ex.Message), ex);
				}
			}
		}

		if(!found)
			return null;

		if(plan.Tasks.Count == 0)
			throw new InvalidDataException(Properties.Resources.MigrationTasksMissing);

		return plan;
	}

	#endregion

	#region 私有方法
	private IEnumerable<string> GetMigrationFiles(string paths, string source)
	{
		foreach(var argument in paths.Split([';', '|'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
		{
			var path = Path.GetFullPath(Path.Combine(source, _expand(argument)));
			var directory = Path.GetDirectoryName(path);
			if(directory.IndexOfAny(['*', '?']) >= 0)
				throw new InvalidDataException(Properties.Resources.MigrationWildcardInvalid);

			var name = Path.GetFileName(path);
			var files = name.IndexOfAny(['*', '?']) < 0 ?
				(File.Exists(path) ? [path] : Array.Empty<string>()) :
				(Directory.Exists(directory) ? Directory.GetFiles(directory, name).OrderBy(file => Path.GetFileName(file), StringComparer.Ordinal).ToArray() : []);

			if(files.Length == 0)
			{
				_warning(string.Format(Properties.Resources.MigrationFilesMissing, path));
				continue;
			}

			foreach(var file in files)
			{
				if(!file.EndsWith(".ini", StringComparison.OrdinalIgnoreCase))
					throw new InvalidDataException(string.Format(Properties.Resources.MigrationFileExtension, file));

				yield return file;
			}
		}
	}

	private Dictionary<string, string> FindParameters(string migration, string provider)
	{
		provider = MigrationProvider.Get(provider).Name;
		var candidates = new List<string>();

		for(var directory = new DirectoryInfo(Path.GetDirectoryName(Path.GetFullPath(migration))); directory != null; directory = directory.Parent)
		{
			foreach(var name in new[] { Path.ChangeExtension(Path.GetFileName(migration), ".env") }.Concat(MigrationProvider.Get(provider).Aliases.Select(alias => alias + ".env")).Distinct(StringComparer.Ordinal))
			{
				var path = Path.Combine(directory.FullName, name);
				candidates.Add(path);

				if(!File.Exists(path))
					continue;

				var profile = MigrationProfile.Load(path);
				var sections = profile.Sections.Where(section => MigrationProvider.Get(provider).Aliases.Contains(section.Name, StringComparer.OrdinalIgnoreCase)).ToArray();

				if(sections.Length > 1)
					throw new InvalidDataException(string.Format(Properties.Resources.MigrationAliasesDuplicate, path));

				var specialized = MigrationProvider.Get(provider).Aliases.Contains(Path.GetFileNameWithoutExtension(name), StringComparer.OrdinalIgnoreCase);
				var entries = sections.Length == 1 ? sections[0].Entries : specialized && profile.Entries.Count > 0 ? profile.Entries : null;

				if(entries == null)
					continue;

				var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
				foreach(var entry in entries)
				{
					try
					{
						result.Add(entry.Name, _expand(entry.Value));
					}
					catch
					{
						throw new InvalidDataException(string.Format(Properties.Resources.MigrationParameterUnresolved, entry.Name, path, entry.LineNumber + 1));
					}
				}

				try { MigrationProvider.Get(provider).Validate(result); }
				catch(InvalidDataException ex) { throw new InvalidDataException(string.Format(Properties.Resources.MigrationParameterError, path, provider, ex.Message)); }

				return result;
			}
		}

		throw new FileNotFoundException(string.Format(Properties.Resources.MigrationParametersMissing, provider, migration, string.Join(", ", candidates)));
	}

	#endregion
}
