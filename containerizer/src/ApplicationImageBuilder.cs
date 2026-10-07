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
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

using Zongsoft.Components;
using Zongsoft.Tools.Containerizer.Protocol;

namespace Zongsoft.Tools.Containerizer;

internal sealed partial class ApplicationImageBuilder(ContainerEngine engine, BuildResources buildResources, RuntimeEnvironmentCache environments, Action<CommandOutletContent> output, Action<string> error)
{
	#region 公共方法
	public async Task BuildAsync(ServiceBuildContext source, ContainerManifest manifest, string workspace, string delivery, string imageReference, CancellationToken cancellation)
	{
		var component = source.Component;
		var package = source.Package;

		var runtime = ApplicationPlanner.ResolveRuntime(source);
		var distribution = manifest["distribution"];
		var directory = Path.Combine(workspace, source.Plan.Id);
		var baseline = await environments.PrepareAsync(distribution, manifest["architecture"], runtime, directory, cancellation);

		using var timing = Output.Measure(output, string.Format(Properties.Resources.NodeBuilder_PrepareImage, source.Plan.Id));
		PrepareImage(component["package"], package, source, distribution, directory);

		var reference = buildResources.RegisterImage();
		await engine.BuildAsync(directory, baseline.Platform, reference, cancellation, "final");

		await using var resources = new BuildResources(engine, error);
		if(runtime != null)
			runtime = RuntimeEnvironmentCache.GetInstalledRuntime(runtime, await engine.RunAsync(["run", "--name", resources.RegisterContainer(), "--rm", "--network", "none", "--entrypoint", "dotnet", reference, "--list-runtimes"], directory, cancellation));

		using var inspected = JsonDocument.Parse(await engine.InspectAsync(reference, cancellation));
		var id = inspected.RootElement[0].GetProperty("Id").GetString();

		if(!id.StartsWith("sha256:", StringComparison.Ordinal))
			id = $"sha256:{id}";

		source.Plan.Image = new()
		{
			Id = id,
			Reference = imageReference,
			Platform = baseline.Platform,
			Mode = "offline",
			Archive = $"images/{source.Plan.Id}.tar"
		};

		source.Plan.Package = new()
		{
			Name = package.Name,
			Version = package.Version,
			Architecture = package.Architecture,
			Format = package.Format,
			Hash = Files.Hash(component["package"]),
			Runtime = runtime,
			BaseImageReference = $"{baseline.Repository}@{baseline.Digest}"
		};

		await engine.ExportAsync(source.Plan.Image.Id, Path.Combine(delivery, source.Plan.Image.Archive), cancellation);
	}
	#endregion
}
