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
using System.Text.RegularExpressions;

using Zongsoft.Common;
using Zongsoft.Terminals;
using Zongsoft.Tools.Containerizer.Protocol;

namespace Zongsoft.Tools.Containerizer;

internal sealed partial class ContainerEngine(string executable, IProcessRunner runner)
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
	public async Task<string> RunAsync(IReadOnlyList<string> arguments, string directory, CancellationToken cancellation, int timeout = 1800)
	{
		var result = await runner.RunAsync(this.Executable, arguments, directory, cancellation, timeout);

		if(result.ExitCode != 0)
		{
			var message = string.Format(Properties.Resources.Engine_1_Message, this.Executable, arguments[0], result.ExitCode);
			var output = string.IsNullOrWhiteSpace(result.Output) ? "" : $"{Environment.NewLine}{result.Output[Math.Max(0, result.Output.Length - 65536)..].Trim()}";
			var error = string.IsNullOrWhiteSpace(result.Error) ? "" : $"{Environment.NewLine}{result.Error.Trim()}";

			throw new ContainerizationException(4, $"{message}{output}{error}");
		}

		return result.Output;
	}

	public static async Task<ContainerEngine> ConnectAsync(string choice, IProcessRunner runner, CancellationToken cancellation, bool compose = true)
	{
		var failures = new List<string>();
		string[] candidates = choice == AUTO ? [DOCKER, PODMAN] : [choice];

		foreach(var candidate in candidates)
		{
			var operation = "info";

			try
			{
				var result = await runner.RunAsync(candidate, ["info"], null, cancellation, 30);

				if(result.ExitCode == 0 && compose)
				{
					operation = "compose version";
					result = await runner.RunAsync(candidate, ["compose", "version"], null, cancellation, 30);
				}

				if(result.ExitCode != 0)
				{
					failures.Add($"{candidate} {operation}: {(string.IsNullOrWhiteSpace(result.Error) ? result.Output : result.Error).Trim()} ({result.ExitCode})");
					continue;
				}

				return new ContainerEngine(candidate, runner);
			}
			catch(ContainerizationException exception) { failures.Add($"{candidate} {operation}: {exception.Message}"); }
			catch(OperationCanceledException) when(!cancellation.IsCancellationRequested) { failures.Add($"{candidate} {operation}: {new TimeoutException().Message}"); }
		}

		var guidance = string.Join(Environment.NewLine, candidates.Select(candidate => candidate == DOCKER ?
			string.Format(Properties.Resources.Engine_Docker_Guidance, candidate, "Docker Desktop") :
			string.Format(Properties.Resources.Engine_Podman_Guidance, candidate)));

		if(compose)
			guidance = $"{Properties.Resources.Engine_Compose_Requirement}{Environment.NewLine}{guidance}";

		throw new ContainerizationException(3, string.Format(Properties.Resources.Engine_2_Message, string.Join(" / ", candidates), guidance, string.Join(Environment.NewLine, failures)));
	}

	public async Task<ImagePlan> ResolveAsync(string reference, string digest, string version, string architecture, CancellationToken cancellation, bool refresh = false)
	{
		var repository = Repository(reference);
		digest ??= reference.Contains('@') ? reference.Split('@', 2)[1] : null;
		version ??= reference.LastIndexOf(':') > reference.LastIndexOf('/') ? reference[(reference.LastIndexOf(':') + 1)..] : "latest";

		ValidateTag(version);
		var expected = default(Checksum);

		if(digest != null && !TryDigest(digest, out expected))
			throw new ContainerizationException(2, Properties.Resources.Engine_5_Message);

		var selector = $"{repository}{(digest == null ? $":{version}" : $"@{digest}")}";
		var platform = architecture == "arm64" ? "linux/arm64" : "linux/amd64";
		var cached = refresh ? null : await this.TryInspectAsync(selector, platform, cancellation);
		var actual = cached.HasValue && Matches(cached.Value, platform) ? LocalDigest(cached.Value, repository, architecture, expected) : default;
		var index = default(Checksum);
		var sourceRepository = repository;

		if(!refresh && actual.IsEmpty && !expected.IsEmpty)
		{
			cached = await this.TryInspectAsync($"{CachePrefix(repository, architecture)}{FormatDigest(expected)[7..]}", platform, cancellation);
			actual = cached.HasValue && Matches(cached.Value, platform) ? LocalDigest(cached.Value, repository, architecture, expected) : default;
		}

		if(!refresh && actual.IsEmpty)
		{
			foreach(var candidate in this.Mirrors.Repositories(repository).Where(value => value != repository))
			{
				cached = await this.TryInspectAsync($"{candidate}{(digest == null ? $":{version}" : $"@{digest}")}", platform, cancellation);
				actual = cached.HasValue && Matches(cached.Value, platform) ? LocalDigest(cached.Value, candidate, architecture, expected) : default;

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
					var resolved = await this.ResolveManifestAsync($"{candidate}{(digest == null ? $":{version}" : $"@{digest}")}", platform, cancellation);
					if(!expected.IsEmpty && resolved.Digest != expected)
						throw new ContainerizationException(4, Properties.Resources.Engine_3_Message);

					resolution = resolved;
					actual = resolved.Digest;
					index = resolved.Index;
				}

				// Keep the first resolved identity when falling back to another download endpoint.
				var fixedReference = actual.IsEmpty ? $"{candidate}:{version}" : $"{candidate}@{FormatDigest(actual)}";
				await this.RunAsync(["pull", "--platform", platform, fixedReference], null, cancellation);
				var pulled = await this.TryInspectAsync(fixedReference, platform, cancellation);

				if(!pulled.HasValue || !Matches(pulled.Value, platform))
					throw new ContainerizationException(4, Properties.Resources.Engine_4_Message);

				if(actual.IsEmpty)
				{
					// Raw Podman single manifests have no digest; bind their config before accepting the pulled manifest.
					if(resolution.Value.Config.IsEmpty || resolution.Value.Config != ImageId(pulled.Value))
						throw new ContainerizationException(4, Properties.Resources.Engine_3_Message);

					actual = LocalDigest(pulled.Value, candidate, architecture, default);
				}

				if(actual.IsEmpty || !HasRepositoryDigest(pulled.Value, candidate, actual))
					throw new ContainerizationException(4, Properties.Resources.Engine_5_Message);

				sourceRepository = candidate;
				return pulled.Value;
			}, cancellation, (candidate, exception) => Terminal.Default.Error.WriteLine(Output.Message(Properties.Resources.Mirrors_SourceFailed, Zongsoft.Components.CommandOutletColor.Magenta, candidate, exception.Message)));
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
		var id = FormatDigest(ImageId(image));

		if(digest == null)
			await this.RunAsync(["tag", id, $"{repository}:{version}"], null, cancellation);

		// A native cache tag binds the verified child manifest to this image, including on Docker's classic store.
		var cacheTag = $"{CachePrefix(repository, architecture)}{FormatDigest(actual)[7..]}";
		await this.RunAsync(["tag", id, cacheTag], null, cancellation);

		return new()
		{
			Id = id,
			Tag = cacheTag,
			Digest = FormatDigest(actual),
			Platform = platform,
			Version = version,
			Selector = selector,
			Repository = repository,
			SourceRepository = sourceRepository,
			IndexDigest = FormatDigest(index),
			Timestamp = DateTimeOffset.TryParse(Get(image, "Created"), CultureInfo.InvariantCulture, DateTimeStyles.None, out var created) && created.Year > 1 ? created.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture) : null,
			Size = image.TryGetProperty("Size", out var size) && size.ValueKind == JsonValueKind.Number && size.TryGetInt64(out var length) && length >= 0 ? length : null,
		};
	}

	public string BuildReference(ImagePlan image) => this.Executable == PODMAN && !this.Mirrors.IsEmpty ? image.Id : $"{image.Repository}@{image.Digest}";
	public async Task BuildAsync(string directory, string platform, string reference, CancellationToken cancellation, string target = null)
	{
		await using var resources = new BuildResources(this);
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
				var builder = resources.Builder();
				List<string> create = ["buildx", "create", "--name", builder, "--driver", "docker-container"];

				if(!this.Mirrors.IsEmpty)
				{
					configuration = Path.Combine(Path.GetTempPath(), $"containerizer-buildkit-{Guid.NewGuid():N}");
					Files.PrivateDirectory(configuration);
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
	public async Task PrepareOwnershipAsync(ServiceBuildContext source, string workspace, CancellationToken cancellation)
	{
		using var json = JsonDocument.Parse(await this.InspectAsync(source.Plan.Image.Id, cancellation));
		var config = json.RootElement[0].GetProperty("Config");

		if(source.Plan.Health.Test == null && !HasHealthCheck(config))
			throw new ContainerizationException(2, Properties.Resources.Engine_9_Message);

		if(source.Plan.Health.Test?.Contains("redis-cli", StringComparer.Ordinal) == true || source.Plan.Health.Test?.Contains("valkey-cli", StringComparer.Ordinal) == true)
		{
			var command = source.Plan.Command ?? [];
			var password = Array.IndexOf(command, "--requirepass");

			if(password >= 0 && password + 1 < command.Length)
				source.Environment["REDISCLI_AUTH"] = command[password + 1];
		}

		var user = source.Plan.User ?? Get(config, "User");
		if(string.IsNullOrEmpty(user) || user is "root" or "0" or "0:0")
			user = null;

		var mounts = source.Plan.Mounts.Where(mount => !mount.ReadOnly && !mount.Temporary && (mount.User != null || user != null)).ToArray();
		if(mounts.Length == 0)
			return;

		var directory = Path.Combine(workspace, "ownership", source.Plan.Id);
		Files.PrivateDirectory(directory);
		await using var resources = new BuildResources(this);
		var container = resources.Container();
		await this.RunAsync(["create", "--name", container, "--entrypoint", "/bin/true", source.Plan.Image.Id], null, cancellation);

		var passwd = Path.Combine(directory, "passwd");
		var groups = Path.Combine(directory, "group");
		var copy = await runner.RunAsync(this.Executable, ["cp", $"{container}:/etc/passwd", passwd], null, cancellation);

		var entries = copy.ExitCode == 0 ? File.ReadAllLines(passwd).Select(line => line.Split(':')).Where(fields => fields.Length == 7).ToArray() : [];
		var owners = new Dictionary<string, string>(StringComparer.Ordinal);

		foreach(var owner in mounts.Select(mount => mount.User ?? user).Distinct(StringComparer.Ordinal))
		{
			var parts = owner.Split(':', 2);
			var account = entries.FirstOrDefault(fields => fields[0] == parts[0] || fields[2] == parts[0]);
			var uid = account?[2] ?? parts[0];
			var gid = parts.Length == 2 ? parts[1] : account?[3] ?? "0";

			if(!uint.TryParse(gid, out _))
			{
				if(!File.Exists(groups))
					await this.RunAsync(["cp", $"{container}:/etc/group", groups], null, cancellation);
				gid = File.ReadAllLines(groups).Select(line => line.Split(':')).FirstOrDefault(fields => fields.Length >= 3 && fields[0] == gid)?[2];
			}

			if(!uint.TryParse(uid, out _) || !uint.TryParse(gid, out _))
				throw new ContainerizationException(2, Properties.Resources.Engine_10_Message);

			owners.Add(owner, $"{uid}:{gid}");
		}

		foreach(var mount in mounts)
			mount.User = owners[mount.User ?? user];
	}

	public static string Repository(string image)
	{
		if(string.IsNullOrWhiteSpace(image) || image.StartsWith('-') || image.Any(char.IsWhiteSpace))
			throw new ContainerizationException(2, Properties.Resources.Engine_8_Message);

		var value = image.Split('@')[0];
		var colon = value.LastIndexOf(':');

		if(colon > value.LastIndexOf('/'))
			value = value[..colon];

		if(!value.Contains('/'))
			value = $"docker.io/library/{value}";
		else if(!value.Split('/')[0].Contains('.') && !value.Split('/')[0].Contains(':') && !value.StartsWith("localhost/", StringComparison.Ordinal))
			value = $"docker.io/{value}";

		return value;
	}

	public static void ValidateTag(string value)
	{
		if(value == null || !TagRegex().IsMatch(value))
			throw new ContainerizationException(2, Properties.Resources.Engine_8_Message);
	}

	public static void ValidateRepository(string value)
	{
		if(value == null || Repository(value) != value || !RepositoryRegex().IsMatch(value))
			throw new ContainerizationException(2, Properties.Resources.Engine_8_Message);
	}
	#endregion

	#region 内部方法
	internal static bool TryDigest(string text, out Checksum checksum)
	{
		checksum = default;
		if(text?.StartsWith("sha256:", StringComparison.Ordinal) != true)
			return false;

		try
		{
			// Core handles algorithm/hex validation; OCI references require the canonical lowercase form.
			if(Checksum.TryParse(text, out var value) && text == FormatDigest(value))
			{
				checksum = value;
				return true;
			}
		}
		catch(FormatException) { } // Core TryParse can throw for malformed hexadecimal input.

		return false;
	}
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
				.Where(item => item.TryGetProperty("platform", out var target) && Get(target, "os") == "linux" && Get(target, "architecture") == platform[6..])
				.ToArray();

			if(selected.Length != 1)
				throw new ContainerizationException(4, Properties.Resources.Engine_7_Message);

			if(!TryDigest(Get(selected[0], "digest"), out var child))
				throw new ContainerizationException(4, Properties.Resources.Engine_5_Message);

			return (child, ReadDigest(Get(json.RootElement, "digest")), default);
		}

		var digest = reference.Contains('@') ? reference.Split('@')[1] : Get(json.RootElement, "digest");
		return (ReadDigest(digest), default, json.RootElement.TryGetProperty("config", out var config) ? ReadDigest(Get(config, "digest")) : default);
	}

	private async Task<JsonElement?> TryInspectAsync(string reference, string platform, CancellationToken cancellation)
	{
		var result = await runner.RunAsync(this.Executable, this.Executable == DOCKER ? ["image", "inspect", "--platform", platform, reference] : ["image", "inspect", reference], null, cancellation);

		if(result.ExitCode != 0 && this.Executable == DOCKER && (result.Error ?? "").Contains("platform", StringComparison.OrdinalIgnoreCase))
			result = await runner.RunAsync(this.Executable, ["image", "inspect", reference], null, cancellation);
		if(result.ExitCode != 0)
			return null;

		using var json = JsonDocument.Parse(result.Output);
		return json.RootElement.GetArrayLength() == 1 ? json.RootElement[0].Clone() : null;
	}

	private static bool HasHealthCheck(JsonElement configuration) =>
		configuration.TryGetProperty("Healthcheck", out var health) &&
		health.ValueKind == JsonValueKind.Object &&
		health.TryGetProperty("Test", out var test) &&
		test.GetArrayLength() > 0 && test[0].GetString() != "NONE";

	internal static bool Matches(JsonElement image, string platform) => Get(image, "Os") == "linux" && Get(image, "Architecture") == platform[6..];
	private static string CachePrefix(string repository, string architecture) => $"localhost/containerizer/cache/{Files.HashText(repository)}:{architecture}-";
	private static string FormatDigest(Checksum checksum) => checksum.IsEmpty ? null : checksum.ToString().ToLowerInvariant();
	private static Checksum ReadDigest(string text) => text == null ? default : TryDigest(text, out var checksum) ? checksum : throw new ContainerizationException(4, Properties.Resources.Engine_5_Message);

	private static Checksum ImageId(JsonElement image)
	{
		var id = Get(image, "Id");
		if(!string.IsNullOrEmpty(id) && TryDigest(id.Contains(':') ? id : $"sha256:{id}", out var checksum))
			return checksum;

		throw new ContainerizationException(4, Properties.Resources.Engine_5_Message);
	}

	private static bool HasRepositoryDigest(JsonElement image, string repository, Checksum digest) => image.TryGetProperty("RepoDigests", out var values) && values.ValueKind == JsonValueKind.Array && values.EnumerateArray().Any(value =>
	{
		var reference = value.GetString();
		var prefix = $"{repository}@";
		return reference?.StartsWith(prefix, StringComparison.Ordinal) == true && TryDigest(reference[prefix.Length..], out var checksum) && checksum == digest;
	});

	private static Checksum LocalDigest(JsonElement image, string repository, string architecture, Checksum expected)
	{
		if(TryDigest(Get(image, "Digest"), out var candidate) && Matches(candidate))
			return candidate;

		if(image.TryGetProperty("Descriptor", out var descriptor) && Get(descriptor, "mediaType") is "application/vnd.oci.image.manifest.v1+json" or "application/vnd.docker.distribution.manifest.v2+json")
		{
			if(TryDigest(Get(descriptor, "digest"), out candidate) && Matches(candidate))
				return candidate;
		}

		var prefix = CachePrefix(repository, architecture);
		if(image.TryGetProperty("RepoTags", out var tags) && tags.ValueKind == JsonValueKind.Array)
		{
			var identities = tags.EnumerateArray().Select(value => value.GetString()).Where(value => value?.StartsWith(prefix, StringComparison.Ordinal) == true)
				.Select(value => TryDigest($"sha256:{value[prefix.Length..]}", out var checksum) ? checksum : default)
				.Where(value => !value.IsEmpty && Matches(value)).Distinct().ToArray();

			if(identities.Length == 1)
				return identities[0];
		}

		return default;

		bool Matches(Checksum checksum)
		{
			if(!expected.IsEmpty && expected != checksum)
				return false;
			if(HasRepositoryDigest(image, repository, checksum))
				return true;

			// A previously verified logical cache tag survives a mirror-address change.
			var cacheTag = $"{CachePrefix(repository, architecture)}{FormatDigest(checksum)[7..]}";
			return image.TryGetProperty("RepoTags", out var references) && references.ValueKind == JsonValueKind.Array &&
				references.EnumerateArray().Any(value => value.GetString() == cacheTag) &&
				image.TryGetProperty("RepoDigests", out var digests) && digests.ValueKind == JsonValueKind.Array &&
				digests.EnumerateArray().Any(value => value.GetString()?.EndsWith($"@{FormatDigest(checksum)}", StringComparison.Ordinal) == true);
		}
	}

	[GeneratedRegex(@"^[A-Za-z0-9_][A-Za-z0-9_.-]{0,127}$")]
	private static partial Regex TagRegex();
	[GeneratedRegex(@"^[a-z0-9][a-z0-9.-]*(?::[0-9]+)?/[a-z0-9]+(?:[._-]+[a-z0-9]+)*(?:/[a-z0-9]+(?:[._-]+[a-z0-9]+)*)*$")]
	private static partial Regex RepositoryRegex();

	private static string Get(JsonElement value, string key) => value.TryGetProperty(key, out var property) && property.ValueKind == JsonValueKind.String ? property.GetString() : null;
	#endregion
}
