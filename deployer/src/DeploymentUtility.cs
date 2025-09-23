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
 * Copyright (C) 2015-2024 Zongsoft Corporation <http://www.zongsoft.com>
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

namespace Zongsoft.Tools.Deployer
{
	public static class DeploymentUtility
	{
		public static IEnumerable<PathToken> GetFiles(string filePath, IDictionary<string, string> variables)
		{
			if(string.IsNullOrEmpty(filePath))
				yield break;

			var directoryName = Path.GetDirectoryName(filePath);
			var fileName = Path.GetFileName(filePath);

			if(string.IsNullOrEmpty(fileName))
				yield break;

			foreach(var directory in GetDirectories(directoryName, variables.ContainsKey(Deployer.EXPANSION_OPTION)))
			{
				//对当前目录路径进行修正和调整
				if(DirectoryRegulator.Regulate(directory.Path, variables, out var result))
					directory.Path = result;

				if(fileName.Contains('*') || fileName.Contains('?'))
				{
					//如果指定目录不存在则跳过，否则后面的代码会引发系统IO异常
					if(!Directory.Exists(directory.Path))
						continue;

					foreach(var file in Directory.GetFiles(directory.Path, fileName))
						yield return new PathToken(file, directory.Suffix);
				}
				else
				{
					directory.Combine(fileName);
					yield return directory;
				}
			}
		}

		public static IEnumerable<PathToken> GetDirectories(string directory, bool expansion)
		{
			const int Asterisk1 = 1;
			const int Asterisk2 = 2;

			if(string.IsNullOrEmpty(directory))
				return Array.Empty<PathToken>();

			directory = Path.GetFullPath(directory);
			var parts = Common.StringExtension.Slice(directory, Utility.PATH_SEPARATORS).ToArray();
			List<PathToken> directories = null;
			var flags = 0;

			for(int i = 0; i < parts.Length; i++)
			{
				if(parts[i] == "**")
				{
					if(directories == null)
						directories = new List<PathToken>();

					flags |= Asterisk2;
					var origin = Path.Combine(parts.Take(i).ToArray());

					//如果指定目录不存在则跳过，否则后面的代码会引发系统IO异常
					if(!Directory.Exists(origin))
						continue;

					directories.AddRange(Directory.GetDirectories(origin, "*", SearchOption.AllDirectories).Select(p => PathToken.Create(p, origin)));
				}
				else if(parts[i].Contains('*') || parts.Contains("?"))
				{
					if(directories == null)
						directories = new List<PathToken>();

					flags |= Asterisk1;
					var origin = Path.Combine(parts.Take(i).ToArray());

					//如果指定目录不存在则跳过，否则后面的代码会引发系统IO异常
					if(!Directory.Exists(origin))
						continue;

					directories.AddRange(Directory.GetDirectories(origin, parts[i], SearchOption.TopDirectoryOnly).Select(p => PathToken.Create(p, origin)));
				}
				else if(directories != null && directories.Count > 0)
				{
					if((flags & Asterisk2) == Asterisk2)
					{
						for(int j = 0; j < directories.Count; j++)
						{
							if(!directories[j].Suffix.EndsWith("/" + parts[i]))
								directories[j].Deprecate();
						}
					}
					else
					{
						for(int j = 0; j < directories.Count; j++)
						{
							directories[j].Combine(parts[i]);

							if(expansion)
								directories[j].AppendSuffix(parts[i]);
						}
					}
				}
			}

			if(directories == null || directories.Count == 0)
				return new[] { new PathToken(directory) };

			return directories.Where(token => !string.IsNullOrEmpty(token.Path));
		}

		public static bool CopyFile(string source, string destination, Overwrite overwrite)
		{
			if(string.IsNullOrWhiteSpace(source) || string.IsNullOrWhiteSpace(destination))
				return false;

			var copyRequired = true;

			if(File.Exists(destination))
			{
				copyRequired = overwrite switch
				{
					Overwrite.Alway => true,
					Overwrite.Never => false,
					Overwrite.Newest => File.GetLastWriteTime(source) >= File.GetLastWriteTime(destination),
					_ => true,
				};
			}

			if(copyRequired)
			{
				var directory = Path.GetDirectoryName(destination);
				if(!Directory.Exists(directory))
					Directory.CreateDirectory(directory);

				File.Copy(source, destination, true);
			}

			return copyRequired;
		}

		#region 嵌套子类
		public class PathToken
		{
			public PathToken(string path, string suffix = null)
			{
				this.Path = path;
				this.Suffix = string.IsNullOrEmpty(suffix) ? null : suffix.Trim(Utility.PATH_SEPARATORS);
			}

			public string Path;
			public string Suffix;

			public bool Exists() => !string.IsNullOrEmpty(this.Path) && File.Exists(this.Path);
			public void Deprecate() => this.Path = null;
			public void Combine(string path) => this.Path = System.IO.Path.Combine(this.Path, path);
			public void AppendSuffix(string value)
			{
				if(string.IsNullOrEmpty(value) || Utility.IsDirectory(value))
					return;

				if(string.IsNullOrEmpty(this.Suffix))
					this.Suffix = value;
				else
					this.Suffix = $"{this.Suffix}/{value}";
			}

			public static PathToken Create(string fullPath, string prefix)
			{
				if(prefix != null && prefix.Length > 0 && fullPath.StartsWith(prefix))
					return new PathToken(fullPath, fullPath.Substring(prefix.Length));

				return new PathToken(fullPath);
			}

			public override string ToString() => string.IsNullOrEmpty(this.Suffix) ? this.Path : $"{this.Path}?{this.Suffix}";
		}
		#endregion
	}
}