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

partial class MigrationLoader
{
	#region 数据库任务
	private void AddDatabaseSteps(MigrationPlan plan, ProfileSection section, string provider, string name,
		Dictionary<string, int> indexes, Dictionary<string, HashSet<string>> selections, IReadOnlyList<ProfileEntry> entries, string declarationPath)
	{
		MigrationPlan.Step step = null;
		Profile source = null;
		Database loader = null;

		foreach(var entry in entries)
		{
			try
			{
				if(!ReferenceEquals(source, entry.Profile))
				{
					source = entry.Profile;
					step = CreateStep(source.FilePath);
					var key = source.FilePath + "\0" + plan.Databases[step.DatabaseIndex.Value].TargetKey;

					if(!selections.TryGetValue(key, out var selected))
						selections.Add(key, selected = new(OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal));

					loader = new(step, Path.GetDirectoryName(source.FilePath), indexes, selected);
				}

				loader.Add(_expand(entry.Name), _expand(entry.Value));
			}
			catch(Exception ex) when(ex is not (OutOfMemoryException or MigrationPrivilegeException))
			{
				throw new InvalidDataException(string.Format(Properties.Resources.MigrationEntryError_Message,
					entry.Profile.FilePath, entry.LineNumber + 1, section.FullName, ex.Message), ex);
			}
		}

		if(entries.Count == 0)
			CreateStep(declarationPath);

		MigrationPlan.Step CreateStep(string path)
		{
			var database = this.FindDatabase(path, provider, name, plan.Runtime);
			var index = plan.Databases.FindIndex(item => item.TargetKey == database.TargetKey);

			if(index >= 0)
			{
				if(!plan.Databases[index].Equivalent(database))
				{
					throw new InvalidDataException(
						string.Format(Properties.Resources.MigrationParameterError_Message, path, provider,
						string.Format(MigrationResources.ParameterValueInvalid_Message, "Database")));
				}
			}
			else
			{
				index = plan.Databases.Count;
				plan.Databases.Add(database);
			}

			var result = new MigrationPlan.Step { Provider = provider, DatabaseIndex = index };
			plan.Steps.Add(result);
			return result;
		}
	}

	private MigrationPlan.Database FindDatabase(string migration, string providerName, string name, string runtime)
	{
		var provider = MigrationProvider.Get(providerName);
		var candidates = new List<string>();

		for(var directory = new DirectoryInfo(Path.GetDirectoryName(Path.GetFullPath(migration))); directory != null; directory = directory.Parent)
		{
			foreach(var file in new[] { Path.ChangeExtension(Path.GetFileName(migration), ".env") }
				.Concat(provider.Aliases.Select(alias => alias + ".env")).Distinct(StringComparer.Ordinal))
			{
				var path = Path.Combine(directory.FullName, file);
				candidates.Add(path);

				if(!File.Exists(path))
					continue;

				var profile = MigrationProfile.Load(path);
				var sections = profile.Sections.Where(section => provider.Aliases.Contains(section.Name, StringComparer.OrdinalIgnoreCase)).ToArray();

				if(sections.Length > 1)
					throw new InvalidDataException(string.Format(Properties.Resources.MigrationAliasesDuplicate_Message, path));
				var section = sections.FirstOrDefault();
				var specialized = provider.Aliases.Contains(Path.GetFileNameWithoutExtension(file), StringComparer.OrdinalIgnoreCase);
				if(section == null && (!specialized || profile.Entries.Count == 0))
					continue;

				try
				{
					var parameters = this.ReadParameters(section == null ? profile.Entries : section.Entries);
					provider.Validate(parameters, runtime);

					var defaultName = parameters.Get("Database");
					var target = name ?? defaultName;

					if(string.IsNullOrWhiteSpace(target))
						throw new InvalidDataException(string.Format(MigrationResources.ParameterMissing_Message, "Database", providerName));

					var matches = (section == null ? profile.Sections : section.Sections)
						.Where(child => string.Equals(_expand(child.Name), target, StringComparison.OrdinalIgnoreCase)).ToArray();
					if(matches.Length > 1 || matches.Length == 0 && !string.Equals(defaultName, target, StringComparison.OrdinalIgnoreCase))
						throw new InvalidDataException(string.Format(MigrationResources.ParameterValueInvalid_Message, "Database"));

					var configuration = matches.FirstOrDefault();
					var database = new MigrationPlan.Database
					{
						Provider = provider.Name,
						Name = configuration == null ? target : _expand(configuration.Name),
						Settings = parameters,
						Options = configuration == null ? new(StringComparer.OrdinalIgnoreCase) : this.ReadParameters(configuration.Entries),
					};

					parameters.Remove("Database");
					if(configuration == null && provider.Name is "sqlite" or "duckdb")
						database.Options.Add("Path", target);

					if(configuration != null)
					{
						foreach(var child in configuration.Sections)
						{
							if(child.Sections.Count > 0)
								throw new InvalidDataException(string.Format(MigrationResources.ParameterValueInvalid_Message, "Users"));

							var values = this.ReadParameters(child.Entries);
							foreach(var key in values.Keys)
							{
								if(!new[] { "Password", "Permission", "Privileges", "Roles", "Host" }.Contains(key, StringComparer.OrdinalIgnoreCase))
									throw new InvalidDataException(string.Format(MigrationResources.ParameterUnknown_Message, key, provider.Name));
							}

							database.Users.Add(new()
							{
								Name = _expand(child.Name),
								Password = values.Get("Password"),
								Permission = values.Get("Permission", "readwrite"),
								Privileges = Split(values.Get("Privileges")),
								Roles = Split(values.Get("Roles")),
								Host = values.Get("Host"),
							});
						}
					}

					provider.Prepare(database, runtime);
					return database;
				}
				catch(InvalidDataException ex)
				{
					throw new InvalidDataException(string.Format(Properties.Resources.MigrationParameterError_Message, path, provider.Name, ex.Message), ex);
				}
			}
		}

		throw new FileNotFoundException(string.Format(Properties.Resources.MigrationParametersMissing_Message, provider.Name, migration, string.Join(", ", candidates)));
	}

	private Dictionary<string, string> ReadParameters(IEnumerable<ProfileEntry> entries)
	{
		var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

		foreach(var entry in entries)
		{
			try { result.Add(entry.Name, _expand(entry.Value) ?? ""); }
			catch
			{
				throw new InvalidDataException(string.Format(Properties.Resources.MigrationParameterUnresolved_Message,
					entry.Name, entry.Profile.FilePath, entry.LineNumber + 1));
			}
		}

		return result;
	}

	private static string[] Split(string value) => value == null ? [] : value.Split([',', '|'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
	private static IEnumerable<(string Name, string Path, string Entry)> GetDeclarations(string path, HashSet<string> sources)
	{
		// Core 仍解析全部条目。无导入的本地视图仅补充其内部声明表未公开的位置信息。
		var lines = File.ReadAllLines(path);
		using var stream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(string.Join("\n",
			lines.Select(line => line.TrimStart().StartsWith('#') || line.TrimStart().StartsWith(';') ? "" : line))));

		var local = Profile.Load(stream);
		var sections = local.Sections.SelectMany(section => new[] { section }.Concat(section.Sections)).ToArray();
		var entries = sections.SelectMany(section => section.Entries).ToDictionary(entry => entry.LineNumber);

		foreach(var item in lines.Select((text, index) => (Text: text.Trim(), Index: index)))
		{
			var text = item.Text;

			if(text.StartsWith('[') && text.EndsWith(']'))
			{
				var name = string.Join(" ", text[1..^1].Split([' ', '\t'], StringSplitOptions.RemoveEmptyEntries));
				var section = sections.Single(section => section.FullName.Equals(name, StringComparison.OrdinalIgnoreCase));
				if(section.Entries.Count == 0)
					yield return (name, path, null);
			}
			else if(entries.TryGetValue(item.Index, out var entry))
				yield return (entry.Section.FullName, path, entry.Name);
			else if(text.Length >= 8 && text[0] is '#' or ';' &&
				text.AsSpan(1).StartsWith("@import", StringComparison.OrdinalIgnoreCase) &&
				(text.Length == 8 || text[8] is ' ' or '\t'))
			{
				foreach(var value in text[8..].Split([' ', '\t', '|'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
				{
					var imported = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(path), value));

					if(sources.Contains(imported))
					{
						foreach(var declaration in GetDeclarations(imported, sources))
							yield return declaration;
					}
				}
			}
		}
	}
	#endregion
}
