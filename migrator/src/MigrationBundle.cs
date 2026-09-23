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
using System.Buffers.Binary;
using System.Collections.Generic;

using Zongsoft.Tools.Migrator.Migration;

namespace Zongsoft.Tools.Migrator;

/// <summary>Collects the complete native migrator publication for the target runtime.</summary>
public sealed class MigrationBundle : IDisposable
{
	#region 常量定义
	private const string RUNNER = "Zongsoft.Tools.Migrator.Executor";
	#endregion

	#region 成员字段
	private readonly List<Entry> _entries = [];
	private readonly string _temporary = Path.Combine(Path.GetTempPath(), "zongsoft-migration-" + Guid.NewGuid().ToString("N"));
	#endregion

	#region 构造函数
	private MigrationBundle() => Directory.CreateDirectory(_temporary);
	#endregion

	#region 公共属性
	/// <summary>获取生成文件的只读视图；文件只在本对象释放前有效。</summary>
	public IReadOnlyList<Entry> Entries => _entries;
	public string Runtime { get; private set; }
	public string Fingerprint { get; private set; }
	#endregion

	#region 公共方法
	/// <summary>收集目标原生运行器并生成共用文件集，不连接外部服务。</summary>
	/// <param name="plan">包含已预处理 SQL 内容的执行计划。</param>
	/// <param name="defaultStateDirectory">安装时的默认状态目录；独立入口为空，必须由外部脚本传入。</param>
	/// <param name="runtimeDirectory">按 RID 组织的原生产物根目录；为空时使用程序目录或 NuGet 包 tools 目录下的 .migrator。</param>
	/// <returns>拥有临时文件的文件集，调用方使用完毕后必须释放。</returns>
	public static MigrationBundle Build(MigrationPlan plan, string defaultStateDirectory, string runtimeDirectory = null)
	{
		plan.Validate();
		var bundle = new MigrationBundle { Runtime = plan.Runtime, Fingerprint = plan.Fingerprint() };

		try
		{
			bundle.AddRuntime(plan.Runtime, runtimeDirectory ?? GetRuntimeDirectory());
			foreach(var script in plan.Steps.SelectMany(step => step.Scripts))
				bundle.AddText(script.Path, script.Content, Utility.Unix.Mode644, false);

			bundle.AddText(".migration/migration.json", plan.Serialize(), UnixFileMode.UserRead | UnixFileMode.UserWrite);
			bundle.AddText(".migration/id", plan.Fingerprint(), Utility.Unix.Mode644);
			var windows = plan.Runtime == "win-x64";
			bundle.AddText(".migration/migrate." + (windows ? "cmd" : "sh"), windows ? CreateWindowsEntry() : CreateUnixEntry(defaultStateDirectory), Utility.Unix.Mode755, false);
			return bundle;
		}
		catch { bundle.Dispose(); throw; }
	}

	#endregion

	#region 私有方法
	private static string GetRuntimeDirectory()
	{
		var directory = Path.Combine(AppContext.BaseDirectory, ".migrator");
		return Directory.Exists(directory) ? directory : Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", ".migrator"));
	}

	private void AddRuntime(string runtime, string directory)
	{
		directory = Path.Combine(directory, runtime);
		var runner = RUNNER + (runtime == "win-x64" ? ".exe" : "");
		var executable = Path.Combine(directory, runner);

		if(!File.Exists(executable))
			throw new FileNotFoundException(Properties.Resources.MigrationRuntimeMissing_Message, executable);

		using(var stream = File.OpenRead(executable))
		{
			using var reader = new BinaryReader(stream);
			var valid = false;

			if(runtime == "win-x64")
			{
				if(stream.Length >= 64 && reader.ReadUInt16() == 0x5A4D)
				{
					stream.Position = 60;
					var offset = reader.ReadInt32();
					if(offset >= 64 && offset <= stream.Length - 6)
					{
						stream.Position = offset;
						valid = reader.ReadUInt32() == 0x00004550 && reader.ReadUInt16() == 0x8664;
					}
				}
			}
			else
			{
				var header = reader.ReadBytes(20);
				valid = header.Length == 20 && header[0] == 0x7F && header[1] == 'E' && header[2] == 'L' && header[3] == 'F' &&
					header[4] == 2 && header[5] == 1 && BinaryPrimitives.ReadUInt16LittleEndian(header.AsSpan(18)) == (runtime == "linux-x64" ? 62 : 183);
			}

			if(!valid)
				throw new InvalidDataException(string.Format(Properties.Resources.MigrationRuntimeInvalid_Message, runner, runtime));
		}

		foreach(var path in Directory.EnumerateFiles(directory, "*", SearchOption.AllDirectories).OrderBy(path => path, StringComparer.Ordinal))
		{
			if(Path.GetExtension(path).ToLowerInvariant() is ".pdb" or ".dbg" or ".dwo" or ".lib" or ".exp")
				continue;

			var relative = Path.GetRelativePath(directory, path).Replace('\\', '/');
			this.AddFile(path, ".migration/" + relative, relative == runner ? Utility.Unix.Mode755 : Utility.Unix.Mode644);
		}
	}

	private void AddFile(string path, string name, UnixFileMode mode)
	{
		var file = new FileInfo(path);
		_entries.Add(new(path, name, file.Length, Utility.Unix.GetTimestamp(file.LastWriteTimeUtc), mode));
	}

	private void AddText(string name, string text, UnixFileMode mode, bool normalize = true)
	{
		var path = Path.Combine(_temporary, Guid.NewGuid().ToString("N"));
		File.WriteAllText(path, normalize ? text.ReplaceLineEndings("\n") : text);
		this.AddFile(path, name, mode);
	}

	private static string CreateUnixEntry(string state) => ($$"""
		#!/bin/sh
		set -eu
		BASE_DIR=$(CDPATH= cd -- "$(dirname -- "$0")" && pwd)
		STATE_DIR=${2:-{{Quote(state ?? "")}}}
		if [ -z "$STATE_DIR" ]; then exit 2; fi
		if [ "${1:-apply}" = check ]; then
			if cmp -s "$STATE_DIR/ready" "$BASE_DIR/id"; then exit 0; fi
			{{MessageScript(() => Properties.Resources.MigrationNotCompleted)}}
			exit 1
		fi
		exec "$BASE_DIR/{{RUNNER}}" "${1:-apply}" "$BASE_DIR/migration.json" "$STATE_DIR"
		""" + "\n").ReplaceLineEndings("\n");

	private static string CreateWindowsEntry() => ($$"""
		@echo off
		setlocal DisableDelayedExpansion
		set "ACTION=%~1"
		if not defined ACTION set "ACTION=apply"
		if "%~2"=="" exit /b 2
		"%~dp0{{RUNNER}}.exe" "%ACTION%" "%~dp0migration.json" "%~2"
		exit /b %errorlevel%
		""" + "\n").ReplaceLineEndings("\r\n");

	#endregion

	#region 脚本方法
	internal static string Quote(string value) => "'" + value.Replace("'", "'\"'\"'") + "'";
	private static string MessageScript(Func<string> message)
	{
		var culture = System.Globalization.CultureInfo.CurrentUICulture;
		try
		{
			//同步读取两种资源后恢复当前执行上下文，不修改生成资源类的全局 Culture。
			System.Globalization.CultureInfo.CurrentUICulture = System.Globalization.CultureInfo.GetCultureInfo("zh-Hans");
			var chinese = message();
			System.Globalization.CultureInfo.CurrentUICulture = System.Globalization.CultureInfo.InvariantCulture;
			return $$$$"""
				case "${LC_ALL:-${LC_MESSAGES:-${LANG:-}}}" in
					zh*) printf '%s\n' {{{{Quote(chinese)}}}} ;;
					*) printf '%s\n' {{{{Quote(message())}}}} ;;
				esac >&2
				""";
		}
		finally { System.Globalization.CultureInfo.CurrentUICulture = culture; }
	}
	#endregion

	#region 释放资源
	public void Dispose()
	{
		if(Directory.Exists(_temporary))
			Directory.Delete(_temporary, true);
	}
	#endregion

	#region 嵌套类型
	public readonly record struct Entry(string Source, string EntryName, long FileSize, long ModifiedTime, UnixFileMode Mode)
	{
		public Stream OpenRead() => File.OpenRead(this.Source);
	}
	#endregion
}
