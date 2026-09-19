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
using System.Threading;
using System.Threading.Tasks;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;

using Zongsoft.Terminals;
using Zongsoft.Components;
using Zongsoft.Tools.Migrator.Migration;

namespace Zongsoft.Tools.Migrator;

[CommandOption("name", typeof(string), Required = true)]
[CommandOption("version", typeof(string))]
[CommandOption("platform", typeof(string), Required = true)]
[CommandOption("edition", typeof(string))]
[CommandOption("architecture", typeof(Architecture), Architecture.X64)]
[CommandOption("output", typeof(string))]
[CommandOption("overwrite", typeof(bool), false)]
[CommandOption("title", typeof(string))]
[CommandOption("summary", typeof(string))]
[CommandOption("description", typeof(string))]
/// <summary>制作独立的升迁执行包和启动脚本，不执行目标数据库或存储操作。</summary>
public sealed partial class MigrateCommand : CommandBase<CommandContext>
{
	#region 执行方法
	protected override ValueTask<object> OnExecuteAsync(CommandContext context, CancellationToken cancellation)
	{
		var values = Variables.From(context);
		values[Variables.SOURCE] = Environment.CurrentDirectory;
		values.Remove(Variables.VERSION);
		var selected = VersionSource.Load(context.Options.GetValue<string>(Variables.VERSION), new Variables(values));
		values[Variables.VERSION] = selected.Version.ToString();
		values[Variables.EDITION] = selected.Edition;
		Normalizer.Initialize(values);
		var variables = Normalizer.Variables;
		var name = variables.Name;
		var edition = variables.Edition;
		var version = selected.Version;

		if(string.IsNullOrWhiteSpace(name) || !Regex.IsMatch(name, @"^[A-Za-z0-9][A-Za-z0-9._+-]*$") ||
			(!string.IsNullOrWhiteSpace(edition) && !Regex.IsMatch(edition, @"^[A-Za-z0-9][A-Za-z0-9._+-]*$")))
			throw new InvalidOperationException(Properties.Resources.MigrationIdentityInvalid_Message);

		var platform = variables[Variables.PLATFORM]?.ToLowerInvariant();
		platform = platform switch
		{
			"win" or "windows" => "win",
			"linux" => "linux",
			"osx" or "xos" or "macos" => throw new InvalidOperationException(Properties.Resources.MigrateMacUnavailable_Message),
			"unix" => throw new InvalidOperationException(Properties.Resources.MigrateUnixAmbiguous_Message),
			_ => throw new InvalidOperationException(Properties.Resources.MigrationPlatformInvalid_Message),
		};
		var runtime = platform + "-" + variables.Architecture.ToString().ToLowerInvariant();
		MigrationRuntime.Validate(runtime);
		if(context.Arguments.Count == 0)
			throw new InvalidOperationException(Properties.Resources.MigrationPathsRequired_Message);

		var output = Path.GetFullPath(Path.Combine(Environment.CurrentDirectory, variables.Output ?? "."));
		var suffixed = HasSuffix(name);
		var migrationName = (suffixed ? name : name + "-migrate") + (string.IsNullOrWhiteSpace(edition) ? "" : "-" + edition);
		var prefix = migrationName + "@" + version + "_" + runtime;
		var archive = prefix + ".tar.gz";
		var launcher = prefix + (platform == "win" ? ".cmd" : ".sh");
		var overwrite = context.Options.Switch("overwrite");
		Generator.CheckMigrationOutputs(output, archive, launcher, overwrite);

		var plan = new MigrationLoader(value =>
		{
			var result = Normalizer.Normalize(value, variables);
			if(!result.Succeed)
				throw new InvalidOperationException(string.Format(Properties.Resources.MigrationVariableUndefined_Message, result.Value));
			return result.Value;
		}).Load(context.Arguments, Environment.CurrentDirectory, migrationName, version.ToString(), runtime);
		if(plan == null)
			throw new InvalidOperationException(Properties.Resources.MigrateInputsMissing_Message);

		plan.Title = variables.Title ?? name;
		plan.Summary = variables.Summary;
		plan.Description = variables.Description;
		using var bundle = MigrationBundle.Build(plan, null);
		Generator.Migrate(bundle, output, archive, launcher, migrationName, platform == "win", overwrite);
		Terminal.WriteLine(CommandOutletColor.DarkGreen, string.Format(Properties.Resources.MigrateGenerated, Path.Combine(output, archive), Path.Combine(output, launcher)));
		return ValueTask.FromResult<object>(Path.Combine(output, archive));
	}
	#endregion

	#region 私有方法
	private static bool HasSuffix(string name) =>
		name.EndsWith("-migrate", StringComparison.OrdinalIgnoreCase) || name.EndsWith("-migration", StringComparison.OrdinalIgnoreCase) ||
		name.EndsWith(".migrate", StringComparison.OrdinalIgnoreCase) || name.EndsWith(".migration", StringComparison.OrdinalIgnoreCase);
	#endregion
}
