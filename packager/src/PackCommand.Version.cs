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
using System.Text;

using Zongsoft.Services;

namespace Zongsoft.Tools.Packager;

public abstract partial class PackCommand<TPackage>
{
	#region 嵌套子类
	internal sealed class VersionFile
	{
		#region 成员字段
		private readonly string _path;
		private readonly byte[] _content;
		private readonly byte[] _identifier;
		#endregion

		#region 构造函数
		private VersionFile(string path, ApplicationIdentifier identifier, byte[] content, byte[] version = null)
		{
			_path = path;
			_content = content;
			_identifier = version;
			this.Identifier = identifier;
		}
		#endregion

		#region 公共属性
		public ApplicationIdentifier Identifier { get; }
		#endregion

		#region 公共方法
		public static VersionFile Load(string source, string name, string edition, Version version)
		{
			var manifestPath = Path.GetFullPath(Path.Combine(source, ".edition"));
			var versionPath = Path.GetFullPath(Path.Combine(source, ".version"));
			var hasManifest = TryLoad(manifestPath, ApplicationManifest.Load, out ApplicationManifest application);
			var identifier = default(ApplicationIdentifier);
			var hasIdentifier = !hasManifest && TryLoad(versionPath, LoadIdentifier, out identifier);
			var path = hasManifest || !hasIdentifier ? manifestPath : versionPath;
			var sourceName = application?.Name ?? identifier.Name;

			if(string.IsNullOrWhiteSpace(sourceName) && string.IsNullOrWhiteSpace(name))
				throw new InvalidOperationException(string.Format(Properties.Resources.SourceVersionNameRequired_Message, path));

			if(!string.IsNullOrWhiteSpace(sourceName) && !string.IsNullOrWhiteSpace(name) &&
				!string.Equals(name.Trim(), sourceName, StringComparison.OrdinalIgnoreCase))
				throw new InvalidOperationException(string.Format(Properties.Resources.SourceVersionNameMismatch_Message, path, name, sourceName));

			name = sourceName ?? name.Trim();
			edition = string.IsNullOrWhiteSpace(edition) ? null : edition.Trim();

			if(hasManifest)
			{
				if(edition == null)
				{
					edition = application.Editions.Current.Name;
					if(edition == null && application.Editions.Count > 1)
						throw new InvalidOperationException(string.Format(Properties.Resources.SourceVersionEditionRequired_Message, path));

					if(edition == null && application.Editions.Count == 1)
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
			else if(hasIdentifier)
			{
				edition ??= identifier.Edition;
				version ??= identifier.Version;
			}

			if(version.IsZero())
				throw new InvalidOperationException(string.Format(Properties.Resources.SourceVersionInvalid_Message, path));

			try
			{
				// 在制包前验证将要保存的身份，防止格式分隔符改变名称或发行版含义。
				if(name.IndexOfAny(['@', '=', '[', ']', '\r', '\n', '\0']) >= 0 ||
					edition != null && edition.IndexOfAny(['@', '=', '[', ']', '\r', '\n', '\0']) >= 0)
					throw new ArgumentException(null, nameof(name));

				identifier = new(name, edition, version);
				using var identity = new MemoryStream();
				identifier.Save(identity);

				if(hasIdentifier)
					return new(path, identifier, identity.ToArray());

				application ??= new(name);
				if(edition == null)
					application.Version = version;
				else
				{
					var selected = new ApplicationManifest.Edition(edition, version);
					if(application.Editions.TryGetValue(edition, out var previous))
						application.Editions[application.Editions.IndexOf(previous)] = selected;
					else
						application.Editions.Add(selected);
					application.Editions.Current = edition;
				}

				using var content = new MemoryStream();
				using(var writer = new StreamWriter(content, new UTF8Encoding(false), leaveOpen: true) { NewLine = "\r\n" })
					application.Save(writer);

				return new(path, identifier, content.ToArray(), hasManifest ? null : identity.ToArray());
			}
			catch(Exception exception) when(exception is ArgumentException or FormatException or InvalidOperationException)
			{
				throw new InvalidOperationException(string.Format(Properties.Resources.SourceVersionIdentityInvalid_Message, path), exception);
			}
		}

		public void Save(string packagePath)
		{
			var path = _path;
			try
			{
				if(_identifier == null)
					ArtifactPublisher.Write(path, stream => stream.Write(_content));
				else
				{
					var directory = Path.GetDirectoryName(_path);
					path = $"{_path}; {Path.Combine(directory, ".version")}";
					using var publisher = new ArtifactPublisher(directory, false, ".edition", ".version");
					Write(publisher.StagePath(".edition"), _content);
					Write(publisher.StagePath(".version"), _identifier);
					publisher.Commit();
				}
			}
			catch(Exception exception) when(exception is IOException or UnauthorizedAccessException)
			{
				throw new IOException(string.Format(Properties.Resources.SourceVersionSaveFailed_Message, packagePath, path), exception);
			}
		}
		#endregion

		#region 私有方法
		private static bool TryLoad<T>(string path, Func<Stream, T> load, out T value)
		{
			try
			{
				using var stream = File.OpenRead(path);
				value = load(stream);
				return true;
			}
			catch(FileNotFoundException)
			{
				value = default;
				return false;
			}
			catch(Exception exception) when(exception is IOException or UnauthorizedAccessException or FormatException)
			{
				throw new InvalidDataException(string.Format(Properties.Resources.SourceVersionLoadFailed_Message, path), exception);
			}
		}

		private static ApplicationIdentifier LoadIdentifier(Stream stream)
		{
			var identifier = ApplicationIdentifier.Load(stream);
			return string.IsNullOrWhiteSpace(identifier.Name) ? throw new FormatException() : identifier;
		}

		private static void Write(string path, byte[] content)
		{
			using var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None);
			stream.Write(content);
			stream.Flush(true);
		}
		#endregion
	}
	#endregion
}
