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

using System.Threading;
using System.Threading.Tasks;

using Zongsoft.Components;

namespace Zongsoft.Tools.Containerizer;

public sealed class PlanCommand : ContainerizeCommand
{
	#region 重写方法
	private protected override ContainerManifest CreateManifest(CommandContext context) => ManifestFactory.Create(context, planning: true);
	private protected override ValueTask<string> BuildAsync(ContainerBundleBuilder builder, ContainerManifest manifest, CancellationToken cancellation) => ValueTask.FromResult(builder.Plan(manifest));
	#endregion
}
