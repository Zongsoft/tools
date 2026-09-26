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

namespace Zongsoft.Tools.Packager.Web;

partial class Definition
{
	/// <summary>通过 Profile 导入回调收集声明及来源，保留覆盖和段落顺序。</summary>
	private sealed class Loader
	{
		#region 成员字段
		private readonly Dictionary<string, Scope> _scopes = new(StringComparer.OrdinalIgnoreCase);
		private readonly HashSet<ProfileEntry> _seen = new(ReferenceEqualityComparer.Instance);
		private readonly Dictionary<Profile, Dictionary<string, bool>> _serverKinds = new(ReferenceEqualityComparer.Instance);
		#endregion

		#region 加载方法
		internal Definition Load(string filePath)
		{
			filePath = Path.GetFullPath(filePath);
			_scopes.Add(string.Empty, new(string.Empty, 0, new(filePath)));
			Profile profile;

			try
			{
				profile = Profile.Load(filePath, new ProfileOptions(false)
				{
					RequireImports = true,
					Importing = context => this.Collect(context.Referer),
					Imported = context => this.Collect(context.Profile),
				});
			}
			catch(DefinitionException) { throw; }
			catch(Exception exception) when(exception is IOException or UnauthorizedAccessException or ProfileException or ArgumentException)
			{
				throw DefinitionException.Create("Load", new(filePath), filePath, exception);
			}

			this.Collect(profile);

			//以 Core 的章节首次出现顺序建立层级，覆盖声明不会改变路由的优先顺序。
			var root = _scopes[string.Empty];
			this.Arrange(profile.Sections, root);

			return new(filePath, root, root.Children.AsReadOnly());
		}
		#endregion

		#region 声明收集
		private void Arrange(ProfileSectionCollection sections, Scope parent)
		{
			foreach(var section in sections)
			{
				var scope = this.GetScope(section.FullName, new(section.Profile.FilePath, section.FullName, Line: section.LineNumber + 1));
				parent.Children.Add(scope);
				this.Arrange(section.Sections, scope);
			}
		}

		private Scope GetScope(string name, Diagnostic.Location location)
		{
			if(_scopes.TryGetValue(name, out var scope))
				return scope;

			var depth = name.Split(' ').Length;
			if(depth > 2)
				throw DefinitionException.Create("Scope", location, name);

			scope = new(name, depth, location);
			_scopes.Add(name, scope);
			return scope;
		}

		private void Collect(Profile profile)
		{
			var entries = new List<ProfileEntry>();
			this.CollectEntries(profile.Entries, profile, entries);
			this.CollectSections(profile.Sections, profile, entries);

			foreach(var entry in entries.OrderBy(item => item.LineNumber))
			{
				var section = entry.Section?.FullName ?? string.Empty;
				var location = new Diagnostic.Location(profile.FilePath, section, entry.Name, entry.LineNumber + 1);
				var scope = this.GetScope(section, location);
				var declaration = new Declaration(entry.Name, entry.Value, location, profile);

				Validate(declaration, scope.Depth);

				if(declaration.IsServer)
				{
					if(!_serverKinds.TryGetValue(profile, out var kinds))
						_serverKinds.Add(profile, kinds = new(StringComparer.OrdinalIgnoreCase));

					var pool = declaration.Name.Contains('!');
					if(kinds.TryGetValue(section, out var previous) && previous != pool)
						throw DefinitionException.Create("MixedServers", location, section);

					kinds[section] = pool;
				}

				scope.Declarations.Add(declaration);
			}
		}

		private void CollectSections(ProfileSectionCollection sections, Profile owner, List<ProfileEntry> entries)
		{
			foreach(var section in sections)
			{
				this.GetScope(section.FullName, new(owner.FilePath, section.FullName, Line: section.LineNumber + 1));
				this.CollectEntries(section.Entries, owner, entries);
				this.CollectSections(section.Sections, owner, entries);
			}
		}

		private void CollectEntries(ProfileEntryCollection entries, Profile owner, List<ProfileEntry> result)
		{
			foreach(var entry in entries)
			{
				if(ReferenceEquals(entry.Profile, owner) && _seen.Add(entry))
					result.Add(entry);
			}
		}
		#endregion

		#region 结构校验
		private static void Validate(Declaration declaration, int depth)
		{
			var key = declaration.Name;
			var colon = key.IndexOf(':');

			if(colon >= 0)
			{
				var hoster = key[..colon];
				if(!hoster.Equals("nginx", StringComparison.OrdinalIgnoreCase) && !hoster.Equals("iis", StringComparison.OrdinalIgnoreCase))
					throw DefinitionException.Create("Hoster", declaration.Source, hoster);

				if(depth == 0 || colon == key.Length - 1)
					throw DefinitionException.Create("Scope", declaration.Source, key);

				return;
			}

			var bang = key.IndexOf('!');
			var field = (bang < 0 ? key : key[..bang]).ToLowerInvariant();
			var named = field is "bind" or "header" or "server-health-header";

			if(named && (bang < 0 || bang == key.Length - 1) || bang >= 0 && (bang == key.Length - 1 || field is not ("bind" or "header" or "server-health-header" or "server")))
				throw DefinitionException.Create("Field", declaration.Source, key);

			var allowed = field switch
			{
				"bind" or "host" or "certificate" or "certificate-key" => depth == 1,
				"path" or "match" => depth == 2,
				"server" or "header" or "websocket" or "forwarded" or
				"server-balance" or "server-failure-count" or "server-failure-timeout" or
				"server-retry" or "server-retry-count" or "server-affinity" or "server-affinity-cookie" or
				"server-tls-verify" or "server-tls-trust" or "server-tls-name" or
				"server-health" or "server-health-header" or "server-health-interval" or
				"server-health-connect-timeout" or "server-health-send-timeout" or "server-health-read-timeout" or
				"server-health-failure-count" or "server-health-recovery-count" or "server-health-status" => true,
				_ => throw DefinitionException.Create("Field", declaration.Source, key),
			};

			if(!allowed)
				throw DefinitionException.Create("Scope", declaration.Source, key);
		}
		#endregion
	}
}
