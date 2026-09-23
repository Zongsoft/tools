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
using System.Linq;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Zongsoft.Tools.Migrator.Migration;

partial class MigrationPlan
{
	public sealed class Database
	{
		#region 公共属性
		public string Name { get; set; }
		public string Provider { get; set; }
		public Dictionary<string, string> Settings { get; set; } = new(StringComparer.OrdinalIgnoreCase);
		public Dictionary<string, string> Options { get; set; } = new(StringComparer.OrdinalIgnoreCase);
		public List<User> Users { get; set; } = [];

		[JsonIgnore]
		public string ServerKey => this.Provider + "\0" + (this.Provider is "sqlite" or "duckdb" ? "" :
			this.Settings.Get("Server", "").Trim().ToLowerInvariant() + "\0" + this.Settings.Get("Port", ""));

		[JsonIgnore]
		public string TargetKey => this.ServerKey + "\0" + (this.Provider is "sqlite" or "duckdb" ? this.FileKey : this.Name);
		#endregion

		#region  私有属性
		internal bool WindowsPath { get; set; }
		private string FileKey => this.WindowsPath ? this.Options.Get("Path")?.Replace('\\', '/').ToUpperInvariant() : this.Options.Get("Path");
		#endregion

		#region 公共方法
		public bool Equivalent(Database other) => other != null && this.Provider == other.Provider && this.TargetKey == other.TargetKey &&
			Equal(this.Settings, other.Settings) && Equal(this.Options, other.Options, this.Provider is "sqlite" or "duckdb" ? "Path" : null) &&
			this.Users.Count == other.Users.Count && this.Users.Zip(other.Users).All(pair =>
				pair.First.Name == pair.Second.Name && pair.First.Password == pair.Second.Password &&
				pair.First.Permission == pair.Second.Permission && pair.First.Host == pair.Second.Host &&
				pair.First.Privileges.SequenceEqual(pair.Second.Privileges) && pair.First.Roles.SequenceEqual(pair.Second.Roles));
		#endregion

		#region 私有方法
		private static bool Equal(Dictionary<string, string> first, Dictionary<string, string> second, string ignored = null) =>
			first.Count == second.Count &&
			first.All(item => item.Key.Equals(ignored, StringComparison.OrdinalIgnoreCase) || second.TryGetValue(item.Key, out var value) && value == item.Value);
		#endregion
	}

	public sealed class User
	{
		#region 公共属性
		public string Name { get; set; }
		public string Password { get; set; }
		public string Permission { get; set; } = "readwrite";
		public string[] Privileges { get; set; } = [];
		public string[] Roles { get; set; } = [];
		public string Host { get; set; }
		#endregion
	}
}
