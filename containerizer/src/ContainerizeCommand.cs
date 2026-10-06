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
		context.Output.WriteLine(Output.Message("{0}@{1} ({2})", manifest["name"], manifest["version"], manifest["architecture"]));
		context.Output.WriteLine(Output.Message("  distribution={0}  engine={1}  bootstrap={2}  imaging={3}", manifest["distribution"], manifest["engine"], manifest["bootstrap"], manifest["imaging"]));
		var builder = new DeliveryBuilder(new ProcessRunner(), refresh: context.Options.Switch("refresh"));
		var path = await this.BuildAsync(builder, manifest, cancellation);

		context.Output.WriteLine(CommandOutletStyles.Bold, CommandOutletColor.Green, path);

		return path;
	}
	#endregion

	#region 虚拟方法
	private protected virtual ContainerManifest CreateManifest(CommandContext context) => ContainerManifest.From(context);
	private protected virtual ValueTask<string> BuildAsync(DeliveryBuilder builder, ContainerManifest manifest, CancellationToken cancellation) => new(builder.BuildAsync(manifest, cancellation));
	#endregion
}
