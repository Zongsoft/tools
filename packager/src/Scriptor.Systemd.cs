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

		public void Script()
		{
			var installing = ReadFile(Normalizer.Variables.Source, Normalizer.Variables.Script.Installing);
			var installed = ReadFile(Normalizer.Variables.Source, Normalizer.Variables.Script.Installed);
			var uninstalling = ReadFile(Normalizer.Variables.Source, Normalizer.Variables.Script.Uninstalling);
			var uninstalled = ReadFile(Normalizer.Variables.Source, Normalizer.Variables.Script.Uninstalled);
			var daemon = Normalizer.Variables.Daemon;

			if(daemon.Disabled)
			{
				if(string.IsNullOrWhiteSpace(installing))
					installing = ":";

				if(string.IsNullOrWhiteSpace(installed))
					installed = ":";

				if(string.IsNullOrWhiteSpace(uninstalling))
					uninstalling = ":";

				if(string.IsNullOrWhiteSpace(uninstalled))
					uninstalled = $$"""
					rm -rf '{{_package.InstallPath}}'
					""";

				_package.Scripts = new(installing, installed, uninstalling, uninstalled);
				return;
			}

			var identifier = string.IsNullOrEmpty(daemon.Identifier) ?
				_package.Name.ToLowerInvariant() : daemon.Identifier;

			var fileInfo = new FileInfo(Path.GetFullPath(Path.Combine(Normalizer.Variables.Source, identifier)));

			if(!fileInfo.Exists)
			{
				fileInfo = GenerateDaemon(identifier, _package);

				if(fileInfo == null)
					return;
			}

			_package.Entries.Add(Normalizer.Variables.Source, fileInfo.FullName);

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
				rm -rf '{{_package.InstallPath}}'
				""";

			_package.Scripts = new(installing, installed, uninstalling, uninstalled);
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

		static string GetHostFile(string source, Package package)
		{
			var path = Path.Combine(source, package.Name + ".dll");
			if(File.Exists(path))
				return Path.GetFileName(path);

			path = Path.Combine(source, "bin", Normalizer.Variables.Compilation, Normalizer.Variables.Framework, package.Name + ".dll");
			if(File.Exists(path))
				return Path.GetFileName(path);

			var files = Directory.GetFiles(source, "*.exe", SearchOption.TopDirectoryOnly);
			if(files != null && files.Length == 1)
				return Path.GetFileNameWithoutExtension(files[0]) + ".dll";

			files = Directory.GetFiles(Path.Combine(source, "bin", Normalizer.Variables.Compilation, Normalizer.Variables.Framework), "*.exe", SearchOption.TopDirectoryOnly);
			if(files != null && files.Length == 1)
				return Path.GetFileNameWithoutExtension(files[0]) + ".dll";

			return null;
		}

		static FileInfo GenerateDaemon(string daemon, Package package)
		{
			const string SERVICE_SUFFIX = ".service";

			if(!daemon.EndsWith(SERVICE_SUFFIX))
				daemon += SERVICE_SUFFIX;

			if(Normalizer.Variables.Daemon.Disabled)
				return null;

			var source = Normalizer.Variables.Source;
			var bind = Normalizer.Variables.Daemon.Bind;
			var host = GetHostFile(source, package);

			if(string.IsNullOrEmpty(host))
			{
				Dumper.HostLocateFailed();
				return null;
			}

			var environments = new string[Normalizer.Variables.Daemon.Environments.Length];

			for(int i = 0; i < environments.Length; i++)
			{
				var name = Normalizer.Variables.Daemon.Environments[i];
				var value = Normalizer.Variables[name];

				if(value != null)
					environments[i] = $"Environment={name}={value}";
			}

			using var stream = new FileStream(Path.Combine(Path.GetTempPath(), daemon), FileMode.Create, FileAccess.Write);
			using var writer = new StreamWriter(stream);

			if(string.IsNullOrEmpty(bind))
				writer.Write($"""
					[Unit]
					Description={(string.IsNullOrEmpty(package.Title) ? package.Name : package.Title)}

					[Service]
					Type=simple
					WorkingDirectory={package.InstallPath}
					ExecStartPre=mkdir -p {package.InstallPath}/logs
					ExecStart=dotnet {package.InstallPath}/{host}
					Restart=on-failure
					RestartSec=10
					KillSignal=SIGINT
					SyslogIdentifier={package.Name}
					DynamicUser=no
					PrivateTmp=no
					ReadWritePaths={package.InstallPath} {package.InstallPath}/logs /tmp

					Environment=DOTNET_NOLOGO=true
					{string.Join(Environment.NewLine, environments)}

					[Install]
					WantedBy=multi-user.target
					""");
			else
			{
				if(ushort.TryParse(bind, out var port))
					bind = $"http://127.0.0.1:{port}";

				writer.Write($"""
					[Unit]
					Description={(string.IsNullOrEmpty(package.Title) ? package.Name : package.Title)}

					[Service]
					Type=simple
					WorkingDirectory={package.InstallPath}
					ExecStartPre=mkdir -p {package.InstallPath}/logs
					ExecStart=dotnet {package.InstallPath}/{host} --urls {bind}
					Restart=on-failure
					RestartSec=10
					KillSignal=SIGINT
					SyslogIdentifier={package.Name}
					DynamicUser=no
					PrivateTmp=no
					ReadWritePaths={package.InstallPath} {package.InstallPath}/logs /tmp

					Environment=DOTNET_NOLOGO=true
					{string.Join(Environment.NewLine, environments)}

					[Install]
					WantedBy=multi-user.target
					""");
			}

			return new(stream.Name);
		}
	}
}
