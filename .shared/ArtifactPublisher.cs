/*
 *   _____                                ______
 *  /_   /  ____  ____  ____  _________  / __/ /_
 *    / /  / __ \/ __ \/ __ \/ ___/ __ \/ /_/ __/
 *   / /__/ /_/ / / / /_/ /\_ \/ /_/ / __/ /_
 *  /____/\____/_/ /_/\__  /____/\____/_/  \__/
 *                   /____/
 *
 * Authors:
 *   钟峰(Popeye Zhong) <zongsoft@gmail.com>
 *
 * The MIT License (MIT)
 *
 * Copyright (C) 2026 Zongsoft Corporation <http://www.zongsoft.com>
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

namespace Zongsoft.Tools;

/// <summary>统一暂存文件，单文件原子替换，多文件成组发布并在失败时回退。</summary>
internal sealed class ArtifactPublisher : IDisposable
{
	#region 成员字段
	private readonly string _output;
	private readonly string _staging;
	private readonly string[] _names;
	private readonly StringComparer _comparer;
	private readonly bool _overwrite;
	private bool _preserveStaging;
	#endregion

	#region 构造函数
	internal ArtifactPublisher(string output, bool overwrite, params string[] names)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(output);
		ArgumentNullException.ThrowIfNull(names);

		_comparer = OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
		if(names.Length == 0 ||
		   names.Any(name => string.IsNullOrWhiteSpace(name) || name is "." or ".." || Path.GetFileName(name) != name) ||
		   names.Distinct(_comparer).Count() != names.Length)
			throw new ArgumentException(null, nameof(names));

		_output = Path.GetFullPath(output);
		_names = [.. names];
		_overwrite = overwrite;

		this.Check();
		Directory.CreateDirectory(_output);

		_staging = Path.Combine(_output, ".zongsoft-" + Guid.NewGuid().ToString("N"));
		Directory.CreateDirectory(_staging);

		if(!OperatingSystem.IsWindows())
			File.SetUnixFileMode(_staging, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);

		Directory.CreateDirectory(Path.Combine(_staging, "new"));
		Directory.CreateDirectory(Path.Combine(_staging, "previous"));
	}
	#endregion

	#region 静态方法
	internal static void Write(string path, Action<Stream> write)
	{
		ArgumentNullException.ThrowIfNull(write);

		Publish(path, temporary =>
		{
			using var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None);
			write(stream);
			stream.Flush(true);
		});
	}

	internal static void Copy(string source, string destination) => Publish(destination, temporary => File.Copy(source, temporary));
	#endregion

	#region 实例方法
	internal string StagePath(string name)
	{
		if(!_names.Contains(name, _comparer))
			throw new ArgumentException(null, nameof(name));

		return Path.Combine(_staging, "new", name);
	}

	internal void Commit()
	{
		foreach(var name in _names)
		{
			if(!File.Exists(this.StagePath(name)))
				throw new FileNotFoundException(null, name);
		}

		this.Check();

		// 单文件可直接原子替换，无须移走旧文件进行备份。
		if(_names.Length == 1)
		{
			File.Move(this.StagePath(_names[0]), Path.Combine(_output, _names[0]), _overwrite);
			return;
		}

		var published = new List<string>();
		var backedUp = new List<string>();

		try
		{
			foreach(var name in _names)
			{
				var target = Path.Combine(_output, name);
				if(File.Exists(target))
				{
					File.Move(target, this.BackupPath(name));
					backedUp.Add(name);
				}

				File.Move(this.StagePath(name), target);
				published.Add(name);
			}
		}
		catch
		{
			try { this.Restore(published, backedUp); }
			catch
			{
				_preserveStaging = true;
				throw;
			}

			throw;
		}
	}

	public void Dispose()
	{
		if(!_preserveStaging && Directory.Exists(_staging))
			Directory.Delete(_staging, true);
	}
	#endregion

	#region 私有方法
	private static void Publish(string path, Action<string> create)
	{
		path = Path.GetFullPath(path);
		var name = Path.GetFileName(path);

		try
		{
			using var publisher = new ArtifactPublisher(Path.GetDirectoryName(path), true, name);
			create(publisher.StagePath(name));
			publisher.Commit();
		}
		catch(Exception exception) when(exception is IOException or UnauthorizedAccessException)
		{
			throw new IOException(path, exception);
		}
	}

	private string BackupPath(string name) => Path.Combine(_staging, "previous", name);
	private void Restore(List<string> published, List<string> backedUp)
	{
		for(var index = published.Count - 1; index >= 0; index--)
			File.Delete(Path.Combine(_output, published[index]));

		for(var index = backedUp.Count - 1; index >= 0; index--)
			File.Move(this.BackupPath(backedUp[index]), Path.Combine(_output, backedUp[index]));
	}

	private void Check()
	{
		foreach(var name in _names)
		{
			var path = Path.Combine(_output, name);

			if(Directory.Exists(path) || !_overwrite && File.Exists(path))
				throw new IOException(path);
		}
	}
	#endregion
}
