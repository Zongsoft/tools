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
using System.Text.RegularExpressions;
using System.Runtime.InteropServices;

namespace Zongsoft.Tools.Packager;

partial class Package
{
	public sealed class Deb : Package
	{
		#region 构造函数
		public Deb(string name, string edition, Version version, Platform platform, Architecture architecture) : base(name, edition, version, platform, architecture)
		{
			this.Scriptor = new Scriptor.Systemd(this);
			this.InstallPath = Utility.Unix.GetInstallPath(this.PackageIdentity);
		}

		#endregion

		#region 公共属性
		public string[] Provides { get; set; } = [];
		public string[] Replaces { get; set; } = [];
		public string[] Breaks { get; set; } = [];
		public string[] Conflicts { get; set; } = [];
		public string[] Recommends { get; set; } = [];
		public string[] Suggests { get; set; } = [];
		#endregion

		#region 内部属性
		internal override string FileName => this.GetFileName(".deb");
		#endregion

		#region 公共方法
		public override void Pack(string output, bool overwrite) => this.Deb(output, overwrite);
		#endregion

		#region 内部方法
		internal string GetRelationship(string name)
		{
			var values = name switch
			{
				"Depends" => this.Dependencies,
				"Provides" => this.Provides,
				"Replaces" => this.Replaces,
				"Breaks" => this.Breaks,
				"Conflicts" => this.Conflicts,
				"Recommends" => this.Recommends,
				"Suggests" => this.Suggests,
				_ => throw new ArgumentOutOfRangeException(nameof(name)),
			};

			if(values == null || values.Length == 0)
				return null;

			var text = string.Join(", ", values);
			if(text.IndexOfAny(['\r', '\n', '\0']) >= 0)
				throw new InvalidDataException(string.Format(Properties.Resources.DebianRelationshipInvalid, name));

			foreach(var group in text.Split(','))
			{
				var alternatives = group.Split('|');
				if(alternatives.Length > 1 && name is not ("Depends" or "Recommends" or "Suggests"))
					throw new InvalidDataException(string.Format(Properties.Resources.DebianRelationshipInvalid, name));

				foreach(var item in alternatives)
				{
					var match = Relationship.Pattern.Match(item.Trim());
					if(!match.Success || (name == "Provides" && match.Groups["operator"].Success && match.Groups["operator"].Value != "="))
						throw new InvalidDataException(string.Format(Properties.Resources.DebianRelationshipInvalid, name));
				}
			}

			return text;
		}
		#endregion

		#region 嵌套类型
		private static class Relationship
		{
			public static readonly Regex Pattern = new(@"\A[a-z0-9][a-z0-9+.-]+(?::[a-z0-9][a-z0-9-]*)?(?:\s+\((?<operator><<|<=|=|>=|>>)\s+(?:[0-9]+:)?[0-9][A-Za-z0-9.+~:-]*\))?\z", RegexOptions.CultureInvariant);
		}
		#endregion

	}
}
