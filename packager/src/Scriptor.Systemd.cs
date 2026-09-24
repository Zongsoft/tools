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
using System.Text.RegularExpressions;

namespace Zongsoft.Tools.Packager;

partial class Scriptor
{
	internal sealed class Systemd(Package package) : IScriptor
	{
		private readonly Package _package = package ?? throw new ArgumentNullException(nameof(package));

		public void Script()
		{
			var source = _package.Variables.Source;
			var scripts = _package.Variables.Script;
			var installing = TextSource.Read(source, scripts.Installing, _package.Variables);
			var installed = TextSource.Read(source, scripts.Installed, _package.Variables);
			var uninstalling = TextSource.Read(source, scripts.Uninstalling, _package.Variables);
			var uninstalled = TextSource.Read(source, scripts.Uninstalled, _package.Variables);
			var daemon = _package.Variables.Daemon;

			if(daemon.Disabled)
			{
				if(string.IsNullOrWhiteSpace(installing))
					installing = ":";

				if(string.IsNullOrWhiteSpace(installed))
					installed = ":";

				if(string.IsNullOrWhiteSpace(uninstalling))
					uninstalling = ":";

				if(string.IsNullOrWhiteSpace(uninstalled))
					uninstalled = _package is Package.Tar ? ":" : $$"""
					rm -rf '{{_package.InstallPath}}'
					""";

				_package.Scripts = new(
					Combine(Migrator.ContextScript(_package), this.ReadFiles(source, scripts.PreInstalling), installing, Migrator.InvalidateScript(_package), this.ReadFiles(source, scripts.PostInstalling)),
					Combine(Migrator.ContextScript(_package), Migrator.InvalidateScript(_package), this.ReadFiles(source, scripts.PreInstalled), Migrator.ApplyScript(_package), installed, this.ReadFiles(source, scripts.PostInstalled)),
					Combine(this.ReadFiles(source, scripts.PreUninstalling), uninstalling, this.ReadFiles(source, scripts.PostUninstalling)),
					Combine(this.ReadFiles(source, scripts.PreUninstalled), uninstalled, this.ReadFiles(source, scripts.PostUninstalled)));

				return;
			}

			var identifier = string.IsNullOrEmpty(daemon.Identifier) ? _package.Name.ToLowerInvariant() : daemon.Identifier;
			var fileInfo = new FileInfo(Path.GetFullPath(Path.Combine(source, identifier)));
			string serviceName;

			if(fileInfo.Exists)
			{
				serviceName = fileInfo.Name;
				if(!IsServiceName(serviceName))
					throw new InvalidDataException(string.Format(Properties.Resources.PackageEntryTypeConflict_Message, serviceName));

				_package.Entries.Add(source, fileInfo.FullName);
			}
			else
			{
				var generated = GenerateDaemon(identifier, _package);
				if(generated == null)
				{
					if(_package.Migrator != null)
						throw new InvalidOperationException(Properties.Resources.MigrationHostRequired_Message);

					return;
				}

				serviceName = generated.Value.Name;
				if(!IsServiceName(serviceName))
					throw new InvalidDataException(string.Format(Properties.Resources.PackageEntryTypeConflict_Message, serviceName));

				_package.Entries.AddGeneratedContent(serviceName, generated.Value.Content, Utility.Unix.Mode644);
			}

			var servicePath = $"{_package.InstallPath}/{serviceName}";
			var serviceLink = $"/etc/systemd/system/{serviceName}";

			if(string.IsNullOrWhiteSpace(installing))
				installing = $$"""
				if command -v systemctl >/dev/null 2>&1; then
					systemctl stop '{{serviceName}}' >/dev/null 2>&1 || true
				fi
				""";
			if(_package.Migrator != null && string.IsNullOrWhiteSpace(scripts.Installing))
				installing = $$"""
				if command -v systemctl >/dev/null 2>&1 && [ -d /run/systemd/system ]; then
					if [ "$(systemctl show --property=LoadState --value '{{serviceName}}')" != not-found ]; then
						systemctl stop '{{serviceName}}'
					fi
				fi
				""";

			if(string.IsNullOrWhiteSpace(installed))
				installed = $$"""
				install -d /etc/systemd/system
				ln -sfn '{{servicePath}}' '{{serviceLink}}'
				if command -v systemctl >/dev/null 2>&1; then
					systemctl daemon-reload >/dev/null 2>&1 || true
					systemctl enable '{{serviceName}}' >/dev/null 2>&1 || true
					systemctl start '{{serviceName}}' >/dev/null 2>&1 || true
				fi
				""";

			string migrationPreparation = null;
			if(_package.Migrator != null)
			{
				migrationPreparation = $$"""
				install -d '/etc/systemd/system/{{serviceName}}.d'
				printf '[Service]\nExecStartPre=/bin/sh "%s/.migration/{{Path.GetFileName(_package.Migrator.Script)}}" check "{{Migrator.StateDirectory(_package)}}"\n' "$PACK_INSTALL_PATH" > '/etc/systemd/system/{{serviceName}}.d/20-packager-migration.conf'
				chmod 0644 '/etc/systemd/system/{{serviceName}}.d/20-packager-migration.conf'
				ln -sfn "$PACK_INSTALL_PATH/{{serviceName}}" '{{serviceLink}}'
				if command -v systemctl >/dev/null 2>&1 && [ -d /run/systemd/system ]; then
					systemctl daemon-reload
				fi
				""";
				if(string.IsNullOrWhiteSpace(scripts.Installed))
					installed = $$"""
					if command -v systemctl >/dev/null 2>&1 && [ -d /run/systemd/system ]; then
						systemctl enable '{{serviceName}}'
						systemctl start '{{serviceName}}'
					fi
					""";
			}

			if(string.IsNullOrWhiteSpace(uninstalling))
				uninstalling = $$"""
				if command -v systemctl >/dev/null 2>&1; then
					systemctl disable '{{serviceName}}' >/dev/null 2>&1 || true
					systemctl stop '{{serviceName}}' >/dev/null 2>&1 || true
				fi
				""";

			if(string.IsNullOrWhiteSpace(uninstalled))
				uninstalled = _package is Package.Tar ? $$"""
					rm -f '{{serviceLink}}'
					if command -v systemctl >/dev/null 2>&1; then
						systemctl daemon-reload >/dev/null 2>&1 || true
					fi
					""" : $$"""
					rm -f '{{serviceLink}}'
					if command -v systemctl >/dev/null 2>&1; then
						systemctl daemon-reload >/dev/null 2>&1 || true
					fi
					rm -rf '{{_package.InstallPath}}'
					""";

			_package.Scripts = new(
				Combine(Migrator.ContextScript(_package), this.ReadFiles(source, scripts.PreInstalling), installing, Migrator.InvalidateScript(_package), this.ReadFiles(source, scripts.PostInstalling)),
				Combine(Migrator.ContextScript(_package), Migrator.InvalidateScript(_package), this.ReadFiles(source, scripts.PreInstalled), migrationPreparation, Migrator.ApplyScript(_package), installed, this.ReadFiles(source, scripts.PostInstalled)),
				Combine(this.ReadFiles(source, scripts.PreUninstalling), uninstalling, this.ReadFiles(source, scripts.PostUninstalling)),
				Combine(this.ReadFiles(source, scripts.PreUninstalled), _package.Migrator == null ? null : $"rm -f '/etc/systemd/system/{serviceName}.d/20-packager-migration.conf'", uninstalled, this.ReadFiles(source, scripts.PostUninstalled)));
		}

		static bool IsServiceName(string value) => Regex.IsMatch(value, @"^[A-Za-z0-9][A-Za-z0-9._@-]*\.service$");
		string[] ReadFiles(string source, string paths)
		{
			if(string.IsNullOrWhiteSpace(paths))
				return [];

			return paths
				.Split([';', '|'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
				.Select(path => TextSource.Read(source, path, _package.Variables, true))
				.Where(script => !string.IsNullOrWhiteSpace(script))
				.ToArray();
		}

		static string Combine(params object[] scripts)
		{
			if(scripts == null || scripts.Length == 0)
				return null;

			var items = scripts
				.SelectMany(script => script switch
				{
					null => [],
					string text => [text],
					string[] values => values,
					IEnumerable<string> values => values,
					_ => [script.ToString()],
				})
				.Where(script => !string.IsNullOrWhiteSpace(script))
				.Select(script => script.Trim())
				.ToArray();

			return items.Length == 0 ? null : string.Join(Environment.NewLine + Environment.NewLine, items);
		}

		static string GetHostFile(string source, Package package)
		{
			if(string.IsNullOrWhiteSpace(source) || !Directory.Exists(source))
				return null;

			var path = Path.Combine(source, package.Name + ".dll");
			if(File.Exists(path))
				return Path.GetFileName(path);

			var files = Directory.GetFiles(source, "*.exe", SearchOption.TopDirectoryOnly);
			if(files != null && files.Length == 1)
				return Path.GetFileNameWithoutExtension(files[0]) + ".dll";

			if(string.IsNullOrWhiteSpace(package.Variables.Compilation) || string.IsNullOrWhiteSpace(package.Variables.Framework))
				return null;

			var directory = Path.Combine(source, "bin", package.Variables.Compilation, package.Variables.Framework);
			path = Path.Combine(directory, package.Name + ".dll");
			if(File.Exists(path))
				return Path.GetFileName(path);

			files = Directory.Exists(directory) ? Directory.GetFiles(directory, "*.exe", SearchOption.TopDirectoryOnly) : [];
			if(files != null && files.Length == 1)
				return Path.GetFileNameWithoutExtension(files[0]) + ".dll";

			return null;
		}

		static (string Name, string Content)? GenerateDaemon(string daemon, Package package)
		{
			const string SERVICE_SUFFIX = ".service";

			if(daemon.IndexOfAny(['/', '\\']) >= 0)
				throw new InvalidDataException(string.Format(Properties.Resources.PackageEntryTypeConflict_Message, daemon));

			if(!daemon.EndsWith(SERVICE_SUFFIX))
				daemon += SERVICE_SUFFIX;

			if(package.Variables.Daemon.Disabled)
				return null;

			var source = package.Variables.Source;
			var listen = package.Variables.Listen;
			var host = GetHostFile(source, package);

			if(string.IsNullOrEmpty(host))
			{
				if(package.Migrator != null)
					throw new InvalidOperationException(Properties.Resources.MigrationHostRequired_Message);

				Dumper.HostLocateFailed();
				return null;
			}

			var environments = new string[package.Variables.Daemon.Environments.Length];

			for(int i = 0; i < environments.Length; i++)
			{
				var name = package.Variables.Daemon.Environments[i];
				var value = package.Variables[name];

				if(value != null)
					environments[i] = $"Environment={name}={value}";
			}

			using var writer = new StringWriter();

			if(string.IsNullOrEmpty(listen))
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
					SyslogIdentifier={package.PackageIdentity}
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
				if(ushort.TryParse(listen, out var port))
					listen = $"http://127.0.0.1:{port}";

				writer.Write($"""
					[Unit]
					Description={(string.IsNullOrEmpty(package.Title) ? package.Name : package.Title)}

					[Service]
					Type=simple
					WorkingDirectory={package.InstallPath}
					ExecStartPre=mkdir -p {package.InstallPath}/logs
					ExecStart=dotnet {package.InstallPath}/{host} --urls {listen}
					Restart=on-failure
					RestartSec=10
					KillSignal=SIGINT
					SyslogIdentifier={package.PackageIdentity}
					DynamicUser=no
					PrivateTmp=no
					ReadWritePaths={package.InstallPath} {package.InstallPath}/logs /tmp

					Environment=DOTNET_NOLOGO=true
					{string.Join(Environment.NewLine, environments)}

					[Install]
					WantedBy=multi-user.target
					""");
			}

			return (daemon, writer.ToString().ReplaceLineEndings("\n"));
		}
	}
}
