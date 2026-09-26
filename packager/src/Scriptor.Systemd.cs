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
			var host = _package.Host ??= ApplicationHost.Resolve(_package);
			var web = Web.Installation.CreateScripts(_package);

			if(host.Kind == ApplicationHost.HostKind.None)
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
					Combine(Migrator.ContextScript(_package), Migrator.InvalidateScript(_package), this.ReadFiles(source, scripts.PreInstalled), Migrator.ApplyScript(_package), installed, web.Activate, this.ReadFiles(source, scripts.PostInstalled)),
					Combine(this.ReadFiles(source, scripts.PreUninstalling), web.Deactivate, uninstalling, this.ReadFiles(source, scripts.PostUninstalling)),
					Combine(this.ReadFiles(source, scripts.PreUninstalled), uninstalled, web.Cleanup, this.ReadFiles(source, scripts.PostUninstalled)), web.Delivered);

				return;
			}

			var serviceName = host.ServiceName;
			if(host.Kind == ApplicationHost.HostKind.Existing)
				_package.Entries.Add(source, host.ServiceSource);
			else
				_package.Entries.AddGeneratedContent(serviceName, GenerateDaemon(host, _package), Utility.Unix.Mode644, true);

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
				Combine(Migrator.ContextScript(_package), Migrator.InvalidateScript(_package), this.ReadFiles(source, scripts.PreInstalled), migrationPreparation, Migrator.ApplyScript(_package), installed, web.Activate, this.ReadFiles(source, scripts.PostInstalled)),
				Combine(this.ReadFiles(source, scripts.PreUninstalling), web.Deactivate, uninstalling, this.ReadFiles(source, scripts.PostUninstalling)),
				Combine(this.ReadFiles(source, scripts.PreUninstalled), _package.Migrator == null ? null : $"rm -f '/etc/systemd/system/{serviceName}.d/20-packager-migration.conf'", uninstalled, web.Cleanup, this.ReadFiles(source, scripts.PostUninstalled)), web.Delivered);
		}

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

		static string GenerateDaemon(ApplicationHost application, Package package)
		{
			var listen = application.Listen;
			var host = application.Entry;
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

			return writer.ToString().ReplaceLineEndings("\n");
		}
	}
}
