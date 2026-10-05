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
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Collections.Generic;

using Zongsoft.Terminals;

namespace Zongsoft.Tools.Containerizer;

internal sealed class BuildResources(ContainerEngine engine) : IAsyncDisposable
{
	private readonly List<(ResourceKind Kind, string Name)> _resources = [];

	public string Image() => this.Add(ResourceKind.Image);
	public string Container() => this.Add(ResourceKind.Container);
	public string Builder() => this.Add(ResourceKind.Builder);

	public async ValueTask DisposeAsync()
	{
		for(int index = _resources.Count - 1; index >= 0; index--)
		{
			var resource = _resources[index];
			var timeout = resource.Kind == ResourceKind.Builder ? 120 : 30;

			try
			{
				await engine.RunAsync(resource.Kind switch
				{
					ResourceKind.Image => ["image", "rm", resource.Name],
					ResourceKind.Container => ["container", "rm", resource.Name],
					_ => ["buildx", "rm", resource.Name],
				}, null, CancellationToken.None, timeout);
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

				Terminal.Default.Error.WriteLine(string.Format(Properties.Resources.BuildResources_Cleanup_Message, resource.Name, engine.Executable, exception.Message));
			}
		}

		_resources.Clear();
	}

	private string Add(ResourceKind kind)
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
