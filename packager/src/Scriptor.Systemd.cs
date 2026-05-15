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

namespace Zongsoft.Tools.Packager;

partial class Scriptor
{
	internal sealed class Systemd(Package package) : IScriptor
	{
		private readonly Package _package = package ?? throw new ArgumentNullException(nameof(package));

		public void Script(Argument argument)
		{
			var installing = ReadFile(argument.Source, argument.Installing);
			var installed = ReadFile(argument.Source, argument.Installed);
			var uninstalling = ReadFile(argument.Source, argument.Uninstalling);
			var uninstalled = ReadFile(argument.Source, argument.Uninstalled);

			var daemon = string.IsNullOrEmpty(argument.Daemon) ?
				_package.Name.ToLowerInvariant() : argument.Daemon;

			var fileInfo = new FileInfo(Path.GetFullPath(Path.Combine(argument.Source, daemon)));

			if(!fileInfo.Exists)
				fileInfo = GenerateService(daemon);

			_package.Entries.Add(fileInfo.FullName, fileInfo.Name, fileInfo.Length, fileInfo.LastWriteTimeUtc);

			var serviceName = fileInfo.Name;
			var servicePath = $"{_package.InstallPath}/{serviceName}";
			var serviceLink = $"/etc/systemd/system/{serviceName}";

			if(string.IsNullOrWhiteSpace(installing))
				installing = $$"""
				if command -v systemctl >/dev/null 2>&1; then
					systemctl stop '{{serviceName}}' >/dev/null 2>&1 || true
				fi
				""";

			if(string.IsNullOrWhiteSpace(installed))
				installed = $$"""
				install -d /etc/systemd/system
				ln -sfn '{{servicePath}}' '{{serviceLink}}'
				if command -v systemctl >/dev/null 2>&1; then
					systemctl daemon-reload >/dev/null 2>&1 || true
					systemctl enable '{{serviceName}}' >/dev/null 2>&1 || true
				fi
				""";

			if(string.IsNullOrWhiteSpace(uninstalling))
				uninstalling = $$"""
				if command -v systemctl >/dev/null 2>&1; then
					systemctl stop '{{serviceName}}' >/dev/null 2>&1 || true
				fi
				""";

			if(string.IsNullOrWhiteSpace(uninstalled))
				uninstalled = $$"""
				rm -f '{{serviceLink}}'
				if command -v systemctl >/dev/null 2>&1; then
					systemctl daemon-reload >/dev/null 2>&1 || true
				fi
				rm -rf '{{package.InstallPath}}'
				""";

			_package.Scripts = new(installing, installed, uninstalling, uninstalled);
		}

		private FileInfo GenerateService(string daemon)
		{
			const string SERVICE_SUFFIX = ".service";

			if(!daemon.EndsWith(SERVICE_SUFFIX))
				daemon += SERVICE_SUFFIX;

			using var stream = new FileStream(Path.Combine(Path.GetTempPath(), daemon), FileMode.Create, FileAccess.Write);
			using var writer = new StreamWriter(stream);

			return new(stream.Name);
		}

		static string ReadFile(string source, string path)
		{
			if(string.IsNullOrEmpty(path))
				return null;

			path = Path.Combine(source, path);

			if(!File.Exists(path))
				throw new FileNotFoundException($"The script file '{path}' does not exist.", path);

			return File.ReadAllText(path);
		}
	}
}
