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
using System.Formats.Tar;
using System.IO.Compression;
using System.Collections.Generic;

namespace Zongsoft.Tools.Migrator;

internal static class Generator
{
	#region 升迁归档
	internal static void CheckMigrationOutputs(string output, string archive, string launcher, bool overwrite)
	{
		foreach(var name in new[] { archive, launcher })
		{
			var path = Path.Combine(output, name);

			if(Directory.Exists(path) || !overwrite && File.Exists(path))
				throw new IOException(string.Format(Properties.Resources.MigrateOutputExists_Message, path));
		}
	}

	internal static void Migrate(MigrationBundle bundle, string output, string archive, string launcher, string name, bool windows, bool overwrite)
	{
		CheckMigrationOutputs(output, archive, launcher, overwrite);
		using var publisher = new ArtifactPublisher(output, overwrite, archive, launcher);
		using(var stream = File.Create(publisher.StagePath(archive)))
		using(var gzip = new GZipStream(stream, CompressionLevel.Optimal))
		using(var writer = new TarWriter(gzip, TarEntryFormat.Pax, false))
		{
			writer.WriteEntry(new PaxGlobalExtendedAttributesTarEntry([new KeyValuePair<string, string>("Migrator", GetIdentity()), new KeyValuePair<string, string>("Runtime", bundle.Runtime)]));

			foreach(var directory in bundle.Entries.SelectMany(entry => GetDirectories(entry.EntryName)).Distinct(StringComparer.Ordinal).OrderBy(path => path.Count(character => character == '/')).ThenBy(path => path, StringComparer.Ordinal))
				writer.WriteEntry(new PaxTarEntry(TarEntryType.Directory, directory) { Mode = Utility.Unix.Mode755 });

			foreach(var entry in bundle.Entries)
				WriteTarEntry(writer, entry);
		}

		var script = windows ? CreateMigrationWindowsLauncher(archive, name, bundle.Fingerprint) : CreateMigrationUnixLauncher(archive, name, bundle.Fingerprint);
		File.WriteAllText(publisher.StagePath(launcher), script);

		if(!OperatingSystem.IsWindows())
		{
			File.SetUnixFileMode(publisher.StagePath(launcher), Utility.Unix.Mode755);
			File.SetUnixFileMode(publisher.StagePath(archive), UnixFileMode.UserRead | UnixFileMode.UserWrite);
		}

		publisher.Commit();
	}
	#endregion

	#region 归档方法
	private static IEnumerable<string> GetDirectories(string path)
	{
		for(var index = path.LastIndexOf('/'); index > 0; index = path.LastIndexOf('/'))
			yield return path = path[..index];
	}

	private static string GetIdentity()
	{
		var assembly = typeof(Generator).Assembly.GetName();
		return assembly.Name + "@" + assembly.Version;
	}

	private static void WriteTarEntry(TarWriter writer, MigrationBundle.Entry item)
	{
		using var stream = item.OpenRead();
		writer.WriteEntry(new PaxTarEntry(TarEntryType.RegularFile, item.EntryName)
		{
			Mode = item.Mode,
			ModificationTime = DateTimeOffset.FromUnixTimeSeconds(item.ModifiedTime),
			DataStream = stream,
		});
	}
	#endregion

	#region 启动脚本
	private static string CreateMigrationUnixLauncher(string archive, string name, string fingerprint) => ($$"""
		#!/bin/sh
		set -eu
		ACTION=${1:-apply}
		case "$ACTION" in apply|status|check) ;; *) exit 2 ;; esac
		BASE_DIR=$(CDPATH= cd -- "$(dirname -- "$0")" && pwd)
		STATE_DIR=${2:-"$BASE_DIR/.migration/"{{MigrationBundle.Quote(name)}}}
		if [ "$ACTION" = check ]; then
			[ -f "$STATE_DIR/ready" ] && printf '%s' '{{fingerprint}}' | cmp -s - "$STATE_DIR/ready"
			exit $?
		fi
		WORK_DIR=$(mktemp -d)
		cleanup() { rm -rf -- "$WORK_DIR"; }
		trap cleanup 0
		trap 'exit 130' INT
		trap 'exit 143' HUP TERM
		tar -xzf "$BASE_DIR/"{{MigrationBundle.Quote(archive)}} -C "$WORK_DIR"
		set +e
		sh "$WORK_DIR/.migration/migrate.sh" "$ACTION" "$STATE_DIR"
		RESULT=$?
		exit "$RESULT"
		""" + "\n").ReplaceLineEndings("\n");

	private static string CreateMigrationWindowsLauncher(string archive, string name, string fingerprint) => ($$"""
		@echo off
		setlocal DisableDelayedExpansion
		set "MIGRATION_BASE=%~dp0"
		set "MIGRATION_ACTION=%~1"
		set "MIGRATION_STATE=%~2"
		powershell.exe -NoLogo -NoProfile -NonInteractive -Command "$ErrorActionPreference='Stop'; $action=$env:MIGRATION_ACTION; if(-not $action){$action='apply'}; if($action -cnotin @('apply','status','check')){exit 2}; $state=$env:MIGRATION_STATE; if(-not $state){$state=Join-Path $env:MIGRATION_BASE '.migration\{{name}}'}; if($action -eq 'check'){$ready=Join-Path $state 'ready'; if((Test-Path -LiteralPath $ready) -and [IO.File]::ReadAllText($ready) -ceq '{{fingerprint}}'){exit 0}; exit 1}; $work=Join-Path ([IO.Path]::GetTempPath()) ('zongsoft-migrate-'+[guid]::NewGuid().ToString('N')); $result=1; try { New-Item -ItemType Directory -Path $work | Out-Null; & tar.exe -xzf (Join-Path $env:MIGRATION_BASE '{{archive}}') -C $work; if($LASTEXITCODE -ne 0){exit $LASTEXITCODE}; & (Join-Path $work '.migration\migrate.cmd') $action $state; $result=$LASTEXITCODE } catch { [Console]::Error.WriteLine($_.Exception.Message) } finally { if(Test-Path -LiteralPath $work){Remove-Item -LiteralPath $work -Recurse -Force} }; exit $result"
		exit /b %errorlevel%
		""" + "\n").ReplaceLineEndings("\r\n");
	#endregion
}
