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
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Collections.Generic;

using Zongsoft.Tools.Containerizer.Protocol;

namespace Zongsoft.Tools.Containerizer;

internal sealed partial class RunContext
{
	private sealed class ImageCache(ContainerEngine engine, DeliveryPlan plan, string session)
	{
		#region 常量定义
		private const string LABEL = "org.zongsoft.containerizer.run-cache";
		internal const string CLEAN_MARKER_PATH = "/var/lib/docker/.containerizer-clean";
		private const string PROFILE_LABEL = "org.zongsoft.containerizer.run-profile";
		#endregion

		#region 成员字段
		private readonly string _ownerHash = Files.HashText(plan.Name);
		private readonly string _profileHash = Files.HashText($"{GetBaseImageRecipe(plan.Distribution)}\n{plan.Architecture}\n{plan.Bootstrap.EngineVersion}");
		private bool _isOwned;
		private bool _isClean;
		#endregion

		#region 公共属性
		public string Name => $"containerizer-run-images-{_ownerHash[..24]}";
		#endregion

		#region 公共方法
		public async Task PrepareAsync(string image, CancellationToken cancellation)
		{
			var volumes = SplitLines(await engine.RunAsync(["volume", "ls", "--filter", $"name={this.Name}", "--format", "{{.Name}}"], null, cancellation));

			if(volumes.Contains(this.Name, StringComparer.Ordinal))
			{
				using var json = JsonDocument.Parse(await engine.RunAsync(["volume", "inspect", this.Name], null, cancellation));
				var labels = json.RootElement[0].GetProperty("Labels");
				this.ValidateOwnership(labels);

				var attached = await engine.RunAsync(["ps", "--all", "--filter", $"volume={this.Name}", "--format", "{{.ID}}"], null, cancellation);
				if(!string.IsNullOrWhiteSpace(attached))
					throw new ContainerizationException(3, string.Format(Properties.Resources.Run_CacheConflict_Message, this.Name));

				_isOwned = true;
				if(labels.TryGetProperty(PROFILE_LABEL, out var profile) && profile.GetString() == _profileHash)
				{
					var marker = await engine.RunAsync(["run", "--rm", "--label", $"{OWNER_LABEL}={session}",
						"--volume", $"{this.Name}:/cache:ro", "--entrypoint", "sh", image,
						"-c", "cat /cache/.containerizer-clean 2>/dev/null || true"], null, cancellation);

					if(marker.Trim() == _profileHash)
						return;
				}

				// A crash or a different engine profile invalidates the entire isolated cache.
				await engine.RunAsync(["volume", "rm", this.Name], null, cancellation);
			}

			// Register before invoking the CLI: an interrupted create may still create the volume.
			_isOwned = true;
			await engine.RunAsync(["volume", "create", "--label", $"{LABEL}={_ownerHash}", "--label", $"{PROFILE_LABEL}={_profileHash}", this.Name], null, cancellation);
		}

		public async Task CleanAsync(string container, CancellationToken cancellation)
		{
			await this.RemoveInnerResourcesAsync(container, ["ps", "--all", "--quiet"], ["rm", "--force", "--volumes"], cancellation);
			await this.RemoveInnerResourcesAsync(container, ["volume", "ls", "--quiet"], ["volume", "rm"], cancellation);
			await this.RemoveInnerResourcesAsync(container, ["network", "ls", "--filter", "type=custom", "--quiet"], ["network", "rm"], cancellation);

			var retained = plan.Services.Where(service => service.Kind != "application").Select(service => service.Image.Id).ToHashSet(StringComparer.Ordinal);
			var images = SplitLines(await this.RunInnerEngineAsync(container, ["image", "ls", "--all", "--no-trunc", "--quiet"], cancellation));

			foreach(var id in images.Distinct(StringComparer.Ordinal))
			{
				if(!retained.Contains(id))
					await this.RunInnerEngineAsync(container, ["image", "rm", "--force", id], cancellation);
			}

			// Image tags from older releases are unnecessary; keep one stable reference per retained ID.
			foreach(var id in images.Where(retained.Contains).Distinct(StringComparer.Ordinal))
			{
				var tag = $"containerizer/run-cache:{id[7..]}";
				await this.RunInnerEngineAsync(container, ["tag", id, tag], cancellation);
				using var json = JsonDocument.Parse(await this.RunInnerEngineAsync(container, ["image", "inspect", id], cancellation));

				if(json.RootElement[0].TryGetProperty("RepoTags", out var tags) && tags.ValueKind == JsonValueKind.Array)
				{
					foreach(var reference in tags.EnumerateArray().Select(item => item.GetString()).Where(reference => reference != tag))
						await this.RunInnerEngineAsync(container, ["image", "rm", reference], cancellation);
				}
			}

			// Flush the daemon before recording that no containers, volumes or application images remain.
			await engine.RunAsync(["exec", container, "systemctl", "stop", "docker.socket", "docker.service", "containerd.service"], null, cancellation, 30);
			if(!images.Any(retained.Contains))
				return;

			await engine.RunAsync(["exec", container, "sh", "-c", $"printf '%s' '{_profileHash}' > {CLEAN_MARKER_PATH}"], null, cancellation, 10);
			_isClean = true;
		}

		public async Task ReleaseAsync(CancellationToken cancellation)
		{
			if(_isOwned && !_isClean)
			{
				var volumes = SplitLines(await engine.RunAsync(["volume", "ls", "--filter", $"name={this.Name}", "--format", "{{.Name}}"], null, cancellation, 20));
				if(volumes.Contains(this.Name, StringComparer.Ordinal))
				{
					using var json = JsonDocument.Parse(await engine.RunAsync(["volume", "inspect", this.Name], null, cancellation, 20));
					this.ValidateOwnership(json.RootElement[0].GetProperty("Labels"));
					await engine.RunAsync(["volume", "rm", this.Name], null, cancellation, 60);
				}
			}
		}
		#endregion

		#region 私有方法
		private void ValidateOwnership(JsonElement labels)
		{
			if(labels.ValueKind != JsonValueKind.Object || !labels.TryGetProperty(LABEL, out var owner) || owner.GetString() != _ownerHash)
				throw new ContainerizationException(4, string.Format(Properties.Resources.Run_CacheConflict_Message, this.Name));
		}

		private Task<string> RunInnerEngineAsync(string container, IReadOnlyList<string> arguments, CancellationToken cancellation) =>
			engine.RunAsync(["exec", container, BootstrapPlan.ENGINE, .. arguments], null, cancellation, 30);

		private async Task RemoveInnerResourcesAsync(string container, string[] query, string[] remove, CancellationToken cancellation)
		{
			var resources = SplitLines(await this.RunInnerEngineAsync(container, query, cancellation));
			if(resources.Length != 0)
				await this.RunInnerEngineAsync(container, [.. remove, .. resources], cancellation);
		}

		private static string[] SplitLines(string text) => text.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
		#endregion
	}
}
