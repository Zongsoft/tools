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
using Zongsoft.Configuration.Profiles;

namespace Zongsoft.Tools.Migrator.Migration;

/// <summary>解析升迁文件及参数，并在打包端生成有序 SQL 批次。</summary>
public sealed partial class MigrationLoader(Func<string, string> expand, Action<string> warning = null)
{
	#region 成员字段
	private readonly Func<string, string> _expand = expand ?? (value => value);
	private readonly Action<string> _warning = warning ?? (message => Terminal.WriteLine(CommandOutletColor.DarkYellow, message));
	#endregion

	#region 公共方法
	/// <summary>按分隔路径顺序加载升迁输入；全部缺失时返回空值。</summary>
	public MigrationPlan Load(string paths, string source, string name, string version, string runtime = "linux-x64") =>
		this.Load([paths], source, name, version, runtime);

	/// <summary>按参数位置展开路径，生成计划；存在但无效的输入抛出异常。</summary>
	/// <param name="paths">有序路径表达式，支持变量、分隔符和通配符。</param>
	/// <param name="source">相对输入路径的基准目录。</param>
	/// <param name="name">执行计划的规范升迁名称。</param>
	/// <param name="version">执行计划版本。</param>
	/// <param name="runtime">目标运行时，用于校验目标平台的数据库路径。</param>
	/// <returns>立即解析的计划；全部输入缺失时为空值并逐项警告。</returns>
	public MigrationPlan Load(IEnumerable<string> paths, string source, string name, string version, string runtime = "linux-x64")
	{
		var plan = new MigrationPlan { Name = name, Version = version, Runtime = runtime };
		var indexes = new Dictionary<string, int>(StringComparer.Ordinal);
		var found = false;

		foreach(var file in this.GetMigrationFiles(paths, source))
		{
			found = true;

			var sources = new List<Profile>();
			var profile = MigrationProfile.Load(file, item => { Validate(item); sources.Add(item); }, true);
			var selections = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
			var sections = profile.Sections.SelectMany(section => new[] { section }.Concat(section.Sections))
				.ToDictionary(section => section.FullName, StringComparer.OrdinalIgnoreCase);
			var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
			var batches = new List<(ProfileSection Section, string Path, List<ProfileEntry> Entries)>();

			foreach(var declaration in GetDeclarations(file, sources.Select(item => item.FilePath).ToHashSet(
				OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal)))
			{
				var section = sections[declaration.Name];
				if(MigrationProvider.Get(section.Section?.Name ?? section.Name).Name == "amazon.s3")
				{
					if(visited.Add(section.FullName))
						batches.Add((section, declaration.Path, []));

					continue;
				}

				if(declaration.Entry == null)
				{
					batches.Add((section, declaration.Path, []));
					continue;
				}

				if(!visited.Add(section.FullName + "\0" + declaration.Entry))
					continue;

				// Core 保留条目的首次位置，值及声明来源取最终覆盖结果。
				var entry = section.Entries[declaration.Entry];
				var previous = batches.Count == 0 ? default : batches[^1];

				if(previous.Section == section && previous.Path == entry.Profile.FilePath && previous.Entries.Count > 0)
					previous.Entries.Add(entry);
				else
					batches.Add((section, entry.Profile.FilePath, [entry]));
			}

			foreach(var batch in batches)
			{
				var section = batch.Section;
				var provider = MigrationProvider.Get(section.Section?.Name ?? section.Name).Name;

				if(provider == "amazon.s3")
					this.AddSteps(plan, section, indexes);
				else
					this.AddDatabaseSteps(plan, section, provider, section.Section == null ? null : _expand(section.Name), indexes, selections, batch.Entries, batch.Path);
			}
		}

		if(!found)
			return null;

		if(plan.Steps.Count == 0)
			throw new InvalidDataException(Properties.Resources.MigrationTasksMissing_Message);

		plan.Validate();
		return plan;
	}
	#endregion

	#region 私有方法
	private static void Validate(Profile profile)
	{
		if(profile.Entries.Count > 0)
			throw new InvalidDataException(string.Format(Properties.Resources.MigrationSectionRequired_Message, profile.FilePath));

		var providers = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

		foreach(var section in profile.Sections)
		{
			try
			{
				if(!providers.Add(MigrationProvider.Get(section.Name).Name))
					throw new InvalidDataException(Properties.Resources.MigrationSectionDuplicate_Message);

				if(section.Sections.Any(child => child.Sections.Count > 0) ||
					MigrationProvider.Get(section.Name).Name == "amazon.s3" && section.Sections.Count > 0)
					throw new InvalidDataException(Properties.Resources.MigrationSectionDuplicate_Message);
			}
			catch(Exception ex) when(ex is not OutOfMemoryException)
			{
				throw new InvalidDataException(string.Format(Properties.Resources.MigrationEntryError_Message,
					profile.FilePath, section.LineNumber + 1, section.Name, ex.Message), ex);
			}
		}
	}

	private void AddSteps(MigrationPlan plan, ProfileSection section, Dictionary<string, int> indexes)
	{
		var provider = MigrationProvider.Get(section.Name).Name;
		var selections = new Dictionary<Profile, HashSet<string>>(ReferenceEqualityComparer.Instance);

		Profile source = null;
		Action<string, string> add = null;

		foreach(var entry in section.Entries)
		{
			try
			{
				if(!ReferenceEquals(source, entry.Profile))
				{
					source = entry.Profile;

					if(!selections.TryGetValue(source, out var selected))
					{
						selected = new HashSet<string>(provider == "amazon.s3" || !OperatingSystem.IsWindows() ?
							StringComparer.Ordinal : StringComparer.OrdinalIgnoreCase);
						selections.Add(source, selected);
					}

					var step = new MigrationPlan.Step
					{
						Provider = provider,
						Settings = this.FindParameters(source.FilePath, provider, plan.Runtime),
					};

					// Split only when the declaration source changes, preserving effective entry order.
					add = provider == "amazon.s3" ? new AmazonS3(step, selected).Add :
						new Database(step, Path.GetDirectoryName(source.FilePath), indexes, selected).Add;

					plan.Steps.Add(step);
				}

				add(_expand(entry.Name), _expand(entry.Value));
			}
			catch(Exception ex) when(ex is not OutOfMemoryException)
			{
				throw new InvalidDataException(string.Format(Properties.Resources.MigrationEntryError_Message,
					entry.Profile.FilePath, entry.LineNumber + 1, section.Name, ex.Message), ex);
			}
		}
	}

	private IEnumerable<string> GetMigrationFiles(IEnumerable<string> paths, string source)
	{
		foreach(var argument in paths.SelectMany(path => (path ?? string.Empty).Split([';', '|'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)))
		{
			var path = Path.GetFullPath(Path.Combine(source, _expand(argument)));
			var files = Utility.Search(path, files: true, sourceDirectory: source).Select(match => match.Path).ToArray();

			if(files.Length == 0)
			{
				_warning(string.Format(Properties.Resources.MigrationFilesMissing, path));
				continue;
			}

			foreach(var file in files)
			{
				if(!file.EndsWith(".migration", StringComparison.OrdinalIgnoreCase))
					throw new InvalidDataException(string.Format(Properties.Resources.MigrationFileExtension_Message, file));

				yield return file;
			}
		}
	}

	private Dictionary<string, string> FindParameters(string migration, string provider, string runtime)
	{
		provider = MigrationProvider.Get(provider).Name;
		var candidates = new List<string>();

		for(var directory = new DirectoryInfo(Path.GetDirectoryName(Path.GetFullPath(migration))); directory != null; directory = directory.Parent)
		{
			foreach(var name in new[] { Path.ChangeExtension(Path.GetFileName(migration), ".ini") }.Concat(MigrationProvider.Get(provider).Aliases.Select(alias => alias + ".ini")).Distinct(StringComparer.Ordinal))
			{
				var path = Path.Combine(directory.FullName, name);
				candidates.Add(path);

				if(!File.Exists(path))
					continue;

				var profile = MigrationProfile.Load(path);
				var sections = profile.Sections.Where(section => MigrationProvider.Get(provider).Aliases.Contains(section.Name, StringComparer.OrdinalIgnoreCase)).ToArray();

				if(sections.Length > 1)
					throw new InvalidDataException(string.Format(Properties.Resources.MigrationAliasesDuplicate_Message, path));

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
						throw new InvalidDataException(string.Format(Properties.Resources.MigrationParameterUnresolved_Message,
							entry.Name, entry.Profile.FilePath, entry.LineNumber + 1));
					}
				}

				try
				{
					MigrationProvider.Get(provider).Validate(result, runtime);
				}
				catch(InvalidDataException ex)
				{
					throw new InvalidDataException(string.Format(Properties.Resources.MigrationParameterError_Message, path, provider, ex.Message));
				}

				return result;
			}
		}

		throw new FileNotFoundException(string.Format(Properties.Resources.MigrationParametersMissing_Message, provider, migration, string.Join(", ", candidates)));
	}
	#endregion
}
