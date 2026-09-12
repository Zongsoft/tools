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

using Zongsoft.Services;

namespace Zongsoft.Tools.Packager;

public abstract partial class PackCommand<TPackage>
{
	#region 嵌套子类
	internal sealed class VersionFile
	{
		#region 成员字段
		private readonly string _path;
		private readonly ApplicationVersion _application;
		#endregion

		#region 构造函数
		private VersionFile(string path, ApplicationVersion application, string edition, Version version)
		{
			_path = path;
			_application = application;
			this.Identifier = new(application.Name, edition, version);
		}
		#endregion

		#region 公共属性
		public ApplicationIdentifier Identifier { get; }
		#endregion

		#region 公共方法
		public static VersionFile Load(string source, string name, string edition, Version version)
		{
			var path = Path.GetFullPath(Path.Combine(source, ".version"));
			ApplicationVersion application = null;

			try
			{
				using var stream = File.OpenRead(path);
				application = ApplicationVersion.Load(stream);
			}
			catch(FileNotFoundException) { }
			catch(Exception exception) when(exception is IOException or UnauthorizedAccessException or FormatException)
			{
				throw new InvalidDataException(string.Format(Properties.Resources.SourceVersionLoadFailed_Message, path), exception);
			}

			if(application == null && string.IsNullOrWhiteSpace(name))
				throw new InvalidOperationException(string.Format(Properties.Resources.SourceVersionNameRequired_Message, path));

			edition = string.IsNullOrWhiteSpace(edition) ? null : edition.Trim();

			if(application != null)
			{
				if(!string.IsNullOrWhiteSpace(name) && !string.Equals(name.Trim(), application.Name, StringComparison.OrdinalIgnoreCase))
					throw new InvalidOperationException(string.Format(Properties.Resources.SourceVersionNameMismatch_Message, path, name, application.Name));

				if(edition == null)
				{
					if(application.Editions.Count > 1)
						throw new InvalidOperationException(string.Format(Properties.Resources.SourceVersionEditionRequired_Message, path));

					if(application.Editions.Count == 1)
						edition = application.Editions[0].Name;
				}

				if(edition != null)
				{
					if(!application.Editions.TryGetValue(edition, out var selected))
						throw new InvalidOperationException(string.Format(Properties.Resources.SourceVersionEditionMissing_Message, path, edition));

					edition = selected.Name;
					version ??= selected.Version;
				}
				else
					version ??= application.Version;
			}

			if(version.IsZero())
				throw new InvalidOperationException(string.Format(Properties.Resources.SourceVersionInvalid_Message, path));

			ApplicationVersion updated;

			try
			{
				updated = new ApplicationVersion(application?.Name ?? name);
				if(edition == null)
					updated.Version = version;
				else if(application == null)
					updated.Editions.Add(new(edition, version));
				else
				{
					foreach(var item in application.Editions)
						updated.Editions.Add(new(item.Name, item.Name == edition ? version : item.Version));
				}
			}
			catch(ArgumentException exception)
			{
				throw new InvalidOperationException(string.Format(Properties.Resources.SourceVersionIdentityInvalid_Message, path), exception);
			}

			return new(path, updated, edition, version);
		}

		public void Save(string packagePath)
		{
			try
			{
				using var stream = File.Create(_path);
				_application.Save(stream);
			}
			catch(Exception exception) when(exception is IOException or UnauthorizedAccessException)
			{
				throw new IOException(string.Format(Properties.Resources.SourceVersionSaveFailed_Message, packagePath, _path), exception);
			}
		}
		#endregion
	}
	#endregion
}
