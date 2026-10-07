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
using System.Collections.Generic;
using System.Globalization;

using Zongsoft.Common;
using Zongsoft.Terminals;
using Zongsoft.Tools.Containerizer.Protocol;

namespace Zongsoft.Tools.Containerizer;

internal sealed class ContainerEngine(string executable, IProcessRunner runner, Action<string> error = null)
{
	#region 常量定义
	internal const string AUTO = "auto";
	internal const string DOCKER = BootstrapPlan.ENGINE;
	internal const string PODMAN = "podman";
	internal const string OPTIONS = $"{AUTO}|{DOCKER}|{PODMAN}";
	#endregion

	#region 公共属性
	public string Executable { get; } = executable;
	public RegistryMirrors Mirrors { get; set; } = new();
	#endregion

	#region 公共方法
	public Task<int> StreamAsync(IReadOnlyList<string> arguments, string directory, CancellationToken cancellation) =>
		runner.StreamAsync(this.Executable, arguments, directory, cancellation);

	internal Task<ProcessResult> RunProcessAsync(IReadOnlyList<string> arguments, string directory, CancellationToken cancellation, int timeoutSeconds = 900) =>
		runner.RunAsync(this.Executable, arguments, directory, cancellation, timeoutSeconds);

	public async Task<string> RunAsync(IReadOnlyList<string> arguments, string directory, CancellationToken cancellation, int timeoutSeconds = 1800)
	{
		var result = await this.RunProcessAsync(arguments, directory, cancellation, timeoutSeconds);

		if(result.ExitCode != 0)
		{
			var message = string.Format(Properties.Resources.Engine_1_Message, this.Executable, arguments[0], result.ExitCode);
			var output = string.IsNullOrWhiteSpace(result.Output) ? "" : $"{Environment.NewLine}{result.Output[Math.Max(0, result.Output.Length - 65536)..].Trim()}";
			var error = string.IsNullOrWhiteSpace(result.Error) ? "" : $"{Environment.NewLine}{result.Error.Trim()}";

			throw new ContainerizationException(4, $"{message}{output}{error}");
		}

		return result.Output;
	}

	public static async Task<ContainerEngine> ConnectAsync(string choice, IProcessRunner runner, CancellationToken cancellation, bool requireCompose = true, Action<string> error = null)
	{
		var failures = new List<string>();
		string[] candidates = choice == AUTO ? [DOCKER, PODMAN] : [choice];

		foreach(var candidate in candidates)
		{
			var operation = "info";

			try
			{
				var result = await runner.RunAsync(candidate, ["info"], null, cancellation, 30);

				if(result.ExitCode == 0 && requireCompose)
				{
					operation = "compose version";
					result = await runner.RunAsync(candidate, ["compose", "version"], null, cancellation, 30);
				}

				if(result.ExitCode != 0)
				{
					failures.Add($"{candidate} {operation}: {(string.IsNullOrWhiteSpace(result.Error) ? result.Output : result.Error).Trim()} ({result.ExitCode})");
					continue;
				}

				return new ContainerEngine(candidate, runner, error);
			}
			catch(ContainerizationException exception) { failures.Add($"{candidate} {operation}: {exception.Message}"); }
			catch(OperationCanceledException) when(!cancellation.IsCancellationRequested) { failures.Add($"{candidate} {operation}: {new TimeoutException().Message}"); }
		}

		var guidance = string.Join(Environment.NewLine, candidates.Select(candidate => candidate == DOCKER ?
			string.Format(Properties.Resources.Engine_Docker_Guidance, candidate, "Docker Desktop") :
			string.Format(Properties.Resources.Engine_Podman_Guidance, candidate)));

		if(requireCompose)
			guidance = $"{Properties.Resources.Engine_Compose_Requirement}{Environment.NewLine}{guidance}";

		throw new ContainerizationException(3, string.Format(Properties.Resources.Engine_2_Message, string.Join(" / ", candidates), guidance, string.Join(Environment.NewLine, failures)));
	}

	public async Task<ImagePlan> ResolveAsync(string reference, string digest, string sourceTag, string architecture, CancellationToken cancellation, bool refresh = false)
	{
		var repository = ImageReference.GetRepository(reference);
		digest ??= reference.Contains('@') ? reference.Split('@', 2)[1] : null;
		sourceTag ??= reference.LastIndexOf(':') > reference.LastIndexOf('/') ? reference[(reference.LastIndexOf(':') + 1)..] : "latest";

		ImageReference.ValidateTag(sourceTag);
		var expected = default(Checksum);

		if(digest != null && !ImageReference.TryParseDigest(digest, out expected))
			throw new ContainerizationException(2, Properties.Resources.Engine_5_Message);

		var sourceReference = $"{repository}{(digest == null ? $":{sourceTag}" : $"@{digest}")}";
		var platform = architecture == "arm64" ? "linux/arm64" : "linux/amd64";
		var cached = refresh ? null : await this.InspectOrDefaultAsync(sourceReference, platform, cancellation);
		var actual = cached.HasValue && MatchesPlatform(cached.Value, platform) ? GetLocalDigest(cached.Value, repository, architecture, expected) : default;
		var index = default(Checksum);
		var sourceRepository = repository;

		if(!refresh && actual.IsEmpty && !expected.IsEmpty)
		{
			cached = await this.InspectOrDefaultAsync($"{GetCachePrefix(repository, architecture)}{FormatDigest(expected)[7..]}", platform, cancellation);
			actual = cached.HasValue && MatchesPlatform(cached.Value, platform) ? GetLocalDigest(cached.Value, repository, architecture, expected) : default;
		}

		if(!refresh && actual.IsEmpty)
		{
			foreach(var candidate in this.Mirrors.GetRepositories(repository).Where(value => value != repository))
			{
				cached = await this.InspectOrDefaultAsync($"{candidate}{(digest == null ? $":{sourceTag}" : $"@{digest}")}", platform, cancellation);
				actual = cached.HasValue && MatchesPlatform(cached.Value, platform) ? GetLocalDigest(cached.Value, candidate, architecture, expected) : default;

				if(!actual.IsEmpty)
				{
					sourceRepository = candidate;
					break;
				}
			}
		}

		if(actual.IsEmpty)
		{
			(Checksum Digest, Checksum Index, Checksum Config)? resolution = null;
			cached = await this.Mirrors.ExecuteAsync(repository, async candidate =>
			{
				if(resolution == null)
				{
					var resolved = await this.ResolveManifestAsync($"{candidate}{(digest == null ? $":{sourceTag}" : $"@{digest}")}", platform, cancellation);
					if(!expected.IsEmpty && resolved.Digest != expected)
						throw new ContainerizationException(4, Properties.Resources.Engine_3_Message);

					resolution = resolved;
					actual = resolved.Digest;
					index = resolved.Index;
				}

				// Keep the first resolved identity when falling back to another download endpoint.
				var fixedReference = actual.IsEmpty ? $"{candidate}:{sourceTag}" : $"{candidate}@{FormatDigest(actual)}";
				await this.RunAsync(["pull", "--platform", platform, fixedReference], null, cancellation);
				var pulled = await this.InspectOrDefaultAsync(fixedReference, platform, cancellation);

				if(!pulled.HasValue || !MatchesPlatform(pulled.Value, platform))
					throw new ContainerizationException(4, Properties.Resources.Engine_4_Message);

				if(actual.IsEmpty)
				{
					// Raw Podman single manifests have no digest; bind their config before accepting the pulled manifest.
					if(resolution.Value.Config.IsEmpty || resolution.Value.Config != GetImageId(pulled.Value))
						throw new ContainerizationException(4, Properties.Resources.Engine_3_Message);

					actual = GetLocalDigest(pulled.Value, candidate, architecture, default);
				}

				if(actual.IsEmpty || !HasRepositoryDigest(pulled.Value, candidate, actual))
					throw new ContainerizationException(4, Properties.Resources.Engine_5_Message);

				sourceRepository = candidate;
				return pulled.Value;
			}, cancellation, (candidate, exception) => (error ?? Terminal.Default.Error.WriteLine)(string.Format(Properties.Resources.Mirrors_SourceFailed, candidate, exception.Message)));
		}
		else if(sourceRepository == repository && !HasRepositoryDigest(cached.Value, repository, actual))
		{
			var suffix = $"@{FormatDigest(actual)}";
			sourceRepository = cached.Value.GetProperty("RepoDigests")
				.EnumerateArray()
				.Select(value => value.GetString())
				.First(value => value.EndsWith(suffix, StringComparison.Ordinal))[..^suffix.Length];
		}

		if(actual.IsEmpty)
			throw new ContainerizationException(4, Properties.Resources.Engine_5_Message);

		var image = cached.Value;
		var id = FormatDigest(GetImageId(image));

		if(digest == null)
			await this.RunAsync(["tag", id, $"{repository}:{sourceTag}"], null, cancellation);

		// A native cache tag binds the verified child manifest to this image, including on Docker's classic store.
		var cacheTag = $"{GetCachePrefix(repository, architecture)}{FormatDigest(actual)[7..]}";
		await this.RunAsync(["tag", id, cacheTag], null, cancellation);

		return new()
		{
			Id = id,
			Reference = cacheTag,
			Digest = FormatDigest(actual),
			Platform = platform,
			SourceTag = sourceTag,
			SourceReference = sourceReference,
			Repository = repository,
			SourceRepository = sourceRepository,
			IndexDigest = FormatDigest(index),
			Timestamp = DateTimeOffset.TryParse(GetString(image, "Created"), CultureInfo.InvariantCulture, DateTimeStyles.None, out var created) && created.Year > 1 ? created.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture) : null,
			Size = image.TryGetProperty("Size", out var size) && size.ValueKind == JsonValueKind.Number && size.TryGetInt64(out var length) && length >= 0 ? length : null,
		};
	}

	public string GetBuildReference(ImagePlan image) => this.Executable == PODMAN && !this.Mirrors.IsEmpty ? image.Id : $"{image.Repository}@{image.Digest}";
	public async Task BuildAsync(string directory, string platform, string reference, CancellationToken cancellation, string target = null)
	{
		await using var resources = new BuildResources(this, error);
		List<string> arguments;
		string configuration = null;

		try
		{
			if(this.Executable == PODMAN)
			{
				// --no-cache alone still creates cache images; disable intermediate layers.
				arguments = ["build", "--layers=false", "--force-rm"];

				if(!this.Mirrors.IsEmpty)
					arguments.Add("--pull=never");
			}
			else
			{
				// BuildKit caches belong to a private builder, removed even after a failed build.
				var builder = resources.RegisterBuilder();
				List<string> create = ["buildx", "create", "--name", builder, "--driver", "docker-container"];

				if(!this.Mirrors.IsEmpty)
				{
					configuration = Path.Combine(Path.GetTempPath(), $"containerizer-buildkit-{Guid.NewGuid():N}");
					Files.CreatePrivateDirectory(configuration);
					var path = Path.Combine(configuration, "buildkitd.toml");
					Files.Write(path, string.Join('\n', this.Mirrors.Registries.Select(pair => $"[registry.\"{pair.Key}\"]\n  mirrors = [{string.Join(", ", pair.Value.Select(location => $"\"{location}\""))}]\n")), true);
					var host = (await this.RunAsync(["info", "--format", "{{.OSType}}/{{.Architecture}}"], null, cancellation, 30)).Trim();
					var architecture = host.EndsWith("/arm64", StringComparison.Ordinal) || host.EndsWith("/aarch64", StringComparison.Ordinal) ? "arm64" : "x64";
					var toolkit = await this.ResolveAsync("docker.io/moby/buildkit:buildx-stable-1", null, null, architecture, cancellation);
					create.AddRange(["--buildkitd-config", path, "--driver-opt", $"image={toolkit.SourceRepository}@{toolkit.Digest}"]);
				}

				await this.RunAsync(create, directory, cancellation);
				arguments = ["buildx", "build", "--builder", builder, "--load"];
			}

			arguments.AddRange(["--platform", platform, "-t", reference]);

			if(target != null)
				arguments.AddRange(["--target", target]);

			arguments.Add(directory);
			await this.RunAsync(arguments, directory, cancellation);
		}
		finally
		{
			if(configuration != null && Directory.Exists(configuration))
				Directory.Delete(configuration, true);
		}
	}

	public async Task ExportAsync(string reference, string path, CancellationToken cancellation)
	{
		Directory.CreateDirectory(Path.GetDirectoryName(path));
		await this.RunAsync(this.Executable == PODMAN ? ["save", "--format", "docker-archive", "--output", path, reference] : ["image", "save", "--output", path, reference], null, cancellation);
	}

	public Task<string> InspectAsync(string image, CancellationToken cancellation) => this.RunAsync(["image", "inspect", image], null, cancellation);
	#endregion

	#region 私有方法
	private async Task<(Checksum Digest, Checksum Index, Checksum Config)> ResolveManifestAsync(string reference, string platform, CancellationToken cancellation)
	{
		var output = await this.RunAsync(
			this.Executable == PODMAN ?
			["manifest", "inspect", reference] :
			["buildx", "imagetools", "inspect", reference, "--format", "{{json .Manifest}}"], null, cancellation, 60);

		using var json = JsonDocument.Parse(output);

		if(json.RootElement.TryGetProperty("manifests", out var manifests))
		{
			var selected = manifests.EnumerateArray()
				.Where(item => item.TryGetProperty("platform", out var target) && GetString(target, "os") == "linux" && GetString(target, "architecture") == platform[6..])
				.ToArray();

			if(selected.Length != 1)
				throw new ContainerizationException(4, Properties.Resources.Engine_7_Message);

			if(!ImageReference.TryParseDigest(GetString(selected[0], "digest"), out var child))
				throw new ContainerizationException(4, Properties.Resources.Engine_5_Message);

			return (child, ParseDigest(GetString(json.RootElement, "digest")), default);
		}

		var digest = reference.Contains('@') ? reference.Split('@')[1] : GetString(json.RootElement, "digest");
		return (ParseDigest(digest), default, json.RootElement.TryGetProperty("config", out var config) ? ParseDigest(GetString(config, "digest")) : default);
	}

	private async Task<JsonElement?> InspectOrDefaultAsync(string reference, string platform, CancellationToken cancellation)
	{
		var result = await runner.RunAsync(this.Executable, this.Executable == DOCKER ? ["image", "inspect", "--platform", platform, reference] : ["image", "inspect", reference], null, cancellation);

		if(result.ExitCode != 0 && this.Executable == DOCKER && (result.Error ?? "").Contains("platform", StringComparison.OrdinalIgnoreCase))
			result = await runner.RunAsync(this.Executable, ["image", "inspect", reference], null, cancellation);
		if(result.ExitCode != 0)
			return null;

		using var json = JsonDocument.Parse(result.Output);
		return json.RootElement.GetArrayLength() == 1 ? json.RootElement[0].Clone() : null;
	}

	internal static bool MatchesPlatform(JsonElement image, string platform) => GetString(image, "Os") == "linux" && GetString(image, "Architecture") == platform[6..];
	private static string GetCachePrefix(string repository, string architecture) => $"localhost/containerizer/cache/{Files.HashText(repository)}:{architecture}-";
	private static string FormatDigest(Checksum checksum) => checksum.IsEmpty ? null : checksum.ToString().ToLowerInvariant();
	private static Checksum ParseDigest(string text) => text == null ? default : ImageReference.TryParseDigest(text, out var checksum) ? checksum : throw new ContainerizationException(4, Properties.Resources.Engine_5_Message);

	private static Checksum GetImageId(JsonElement image)
	{
		var id = GetString(image, "Id");
		if(!string.IsNullOrEmpty(id) && ImageReference.TryParseDigest(id.Contains(':') ? id : $"sha256:{id}", out var checksum))
			return checksum;

		throw new ContainerizationException(4, Properties.Resources.Engine_5_Message);
	}

	private static bool HasRepositoryDigest(JsonElement image, string repository, Checksum digest) => image.TryGetProperty("RepoDigests", out var values) && values.ValueKind == JsonValueKind.Array && values.EnumerateArray().Any(value =>
	{
		var reference = value.GetString();
		var prefix = $"{repository}@";
		return reference?.StartsWith(prefix, StringComparison.Ordinal) == true && ImageReference.TryParseDigest(reference[prefix.Length..], out var checksum) && checksum == digest;
	});

	private static Checksum GetLocalDigest(JsonElement image, string repository, string architecture, Checksum expected)
	{
		if(ImageReference.TryParseDigest(GetString(image, "Digest"), out var candidate) && MatchesDigest(candidate))
			return candidate;

		if(image.TryGetProperty("Descriptor", out var descriptor) && GetString(descriptor, "mediaType") is "application/vnd.oci.image.manifest.v1+json" or "application/vnd.docker.distribution.manifest.v2+json")
		{
			if(ImageReference.TryParseDigest(GetString(descriptor, "digest"), out candidate) && MatchesDigest(candidate))
				return candidate;
		}

		var prefix = GetCachePrefix(repository, architecture);
		if(image.TryGetProperty("RepoTags", out var tags) && tags.ValueKind == JsonValueKind.Array)
		{
			var identities = tags.EnumerateArray().Select(value => value.GetString()).Where(value => value?.StartsWith(prefix, StringComparison.Ordinal) == true)
				.Select(value => ImageReference.TryParseDigest($"sha256:{value[prefix.Length..]}", out var checksum) ? checksum : default)
				.Where(value => !value.IsEmpty && MatchesDigest(value)).Distinct().ToArray();

			if(identities.Length == 1)
				return identities[0];
		}

		return default;

		bool MatchesDigest(Checksum checksum)
		{
			if(!expected.IsEmpty && expected != checksum)
				return false;
			if(HasRepositoryDigest(image, repository, checksum))
				return true;

			// A previously verified logical cache tag survives a mirror-address change.
			var cacheTag = $"{GetCachePrefix(repository, architecture)}{FormatDigest(checksum)[7..]}";
			return image.TryGetProperty("RepoTags", out var references) && references.ValueKind == JsonValueKind.Array &&
				references.EnumerateArray().Any(value => value.GetString() == cacheTag) &&
				image.TryGetProperty("RepoDigests", out var digests) && digests.ValueKind == JsonValueKind.Array &&
				digests.EnumerateArray().Any(value => value.GetString()?.EndsWith($"@{FormatDigest(checksum)}", StringComparison.Ordinal) == true);
		}
	}

	private static string GetString(JsonElement value, string key) => value.TryGetProperty(key, out var property) && property.ValueKind == JsonValueKind.String ? property.GetString() : null;
	#endregion
}
