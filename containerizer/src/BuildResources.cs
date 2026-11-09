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
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Collections.Generic;

using Zongsoft.Components;

namespace Zongsoft.Tools.Containerizer;

internal sealed class BuildResources(ContainerEngine engine, Action<CommandOutletContent> error = null) : IAsyncDisposable
{
	private readonly List<(ResourceKind Kind, string Name)> _resources = [];

	public string RegisterImage() => this.RegisterResource(ResourceKind.Image);
	public string RegisterContainer() => this.RegisterResource(ResourceKind.Container);
	public string RegisterBuilder() => this.RegisterResource(ResourceKind.Builder);

	public async ValueTask DisposeAsync()
	{
		for(int index = _resources.Count - 1; index >= 0; index--)
		{
			var resource = _resources[index];
			var timeoutSeconds = resource.Kind == ResourceKind.Builder ? 120 : 30;

			try
			{
				await engine.RunAsync(resource.Kind switch
				{
					ResourceKind.Image => ["image", "rm", resource.Name],
					ResourceKind.Container => ["container", "rm", "--volumes", resource.Name],
					_ => ["buildx", "rm", resource.Name],
				}, null, CancellationToken.None, timeoutSeconds);
			}
			catch(Exception exception)
			{
				try
				{
					var remaining = await engine.RunAsync(resource.Kind switch
					{
						ResourceKind.Image => ["image", "ls", "--quiet", "--filter", $"reference={resource.Name}"],
						ResourceKind.Container => ["container", "ls", "--all", "--quiet", "--filter", $"name={resource.Name}"],
						_ => ["buildx", "ls", "--format", "{{.Name}}"],
					}, null, CancellationToken.None, 30);

					var exists = resource.Kind == ResourceKind.Builder ?
						remaining.Split('\n', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries).Contains(resource.Name, StringComparer.Ordinal) :
						!string.IsNullOrWhiteSpace(remaining);

					if(!exists)
						continue;
				}
				catch(Exception) { }

				(error ?? Utility.WriteError)(Utility.FormatOutput(Properties.Resources.BuildResources_Cleanup_Message, CommandOutletColor.DarkRed, resource.Name, engine.Executable, exception.Message));
			}
		}

		_resources.Clear();
	}

	private string RegisterResource(ResourceKind kind)
	{
		var name = $"containerizer-build-{Guid.NewGuid().ToString("N")}";

		if(kind == ResourceKind.Image)
			name = $"localhost/{name}:latest";

		_resources.Add((kind, name));
		return name;
	}

	private enum ResourceKind
	{
		Image,
		Container,
		Builder,
	}
}
