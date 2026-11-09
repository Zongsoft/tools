/*
 *   _____                                ______
 *  /_   /  ____  ____  ____  _________  / __/ /_
 *    / /  / __ \/ __ \/ __ \/ ___/ __ \/ /_/ __/
 *   / /__/ /_/ / / / / /_/ /\_ \/ /_/ / __/ /_
 *  /____/\____/_/ /_/\__  /____/\____/_/  \__/
 *                   /____/
 *
 * Authors:
 *   钟峰(Popeye Zhong) <zongsoft@qq.com>
 *
 * Copyright (c) Zongsoft Studio. All rights reserved.
 * The above copyright notice and this permission notice shall be included in all
 * AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
 *
 *
 * Licensed under the MIT License (see LICENSE).
 */

using System;
using System.Threading;
using System.Threading.Tasks;

using Zongsoft.Components;
using Zongsoft.Tools.Containerizer.Protocol;

namespace Zongsoft.Tools.Containerizer;

[CommandOption("name", typeof(string))]
[CommandOption("tag", typeof(string))]
[CommandOption("version", typeof(string))]
[CommandOption("distribution", typeof(string))]
[CommandOption("architecture", typeof(string))]
[CommandOption("source", typeof(string))]
[CommandOption("output", typeof(string))]
[CommandOption("engine", typeof(string))]
[CommandOption("bootstrap", typeof(string))]
[CommandOption("imaging", typeof(string))]
[CommandOption("migration", typeof(string))]
[CommandOption("title", typeof(string))]
[CommandOption("description", typeof(string))]
[CommandOption("refresh", typeof(bool), false)]
public partial class ContainerizeCommand : CommandBase<CommandContext>
{
	#region 重写方法
	protected override async ValueTask<object> OnExecuteAsync(CommandContext context, CancellationToken cancellation)
	{
		var manifest = this.CreateManifest(context);
		context.Output.WriteLine(Utility.FormatOutput("{0}@{1} ({2})", manifest["name"], manifest["version"], manifest["architecture"]));
		context.Output.WriteLine(Utility.FormatOutput("  distribution={0}  engine={1}  bootstrap={2}  imaging={3}", manifest["distribution"], manifest["engine"], manifest["bootstrap"], manifest["imaging"]));
		var builder = new ContainerBundleBuilder(new ProcessRunner(), refresh: context.Options.Switch("refresh"), output: context.Output.WriteLine, error: content => Utility.WriteOutput(context.Error, content));
		var path = await this.BuildAsync(builder, manifest, cancellation);

		context.Output.WriteLine(CommandOutletStyles.Bold, CommandOutletColor.Green, path);

		return path;
	}
	#endregion

	#region 虚拟方法
	private protected virtual ContainerManifest CreateManifest(CommandContext context) => ManifestFactory.Create(context);
	private protected virtual ValueTask<string> BuildAsync(ContainerBundleBuilder builder, ContainerManifest manifest, CancellationToken cancellation) => new(builder.BuildAsync(manifest, cancellation));
	#endregion
}
