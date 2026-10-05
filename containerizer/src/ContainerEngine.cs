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
using Zongsoft.Tools.Containerizer.Protocol;

namespace Zongsoft.Tools.Containerizer;

internal sealed partial class ContainerEngine(string executable, IProcessRunner runner)
{
	#region 公共属性
	public string Executable { get; } = executable;
	#endregion

	#region 公共方法
	public async Task<string> RunAsync(IReadOnlyList<string> arguments, string directory, CancellationToken cancellation, int timeout = 1800)
	{
		var result = await runner.RunAsync(this.Executable, arguments, directory, cancellation, timeout);

		if(result.ExitCode != 0)
			throw new ContainerizationException(4, string.Format(Properties.Resources.Engine_1_Message, this.Executable, arguments[0], result.ExitCode) +
				(string.IsNullOrWhiteSpace(result.Output) ? "" : $"{Environment.NewLine}{result.Output[Math.Max(0, result.Output.Length - 65536)..].Trim()}") +
				(string.IsNullOrWhiteSpace(result.Error) ? "" : $"{Environment.NewLine}{result.Error.Trim()}"));

		return result.Output;
	}

	public static async Task<ContainerEngine> ConnectAsync(string choice, IProcessRunner runner, CancellationToken cancellation)
	{
		var failures = new List<string>();

		string[] candidates = choice == "auto" ? ["docker", "podman"] : [choice];
		foreach(var candidate in candidates)
		{
			var operation = "info";

			try
			{
				var result = await runner.RunAsync(candidate, ["info"], null, cancellation, 30);
				if(result.ExitCode == 0)
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

		throw new ContainerizationException(3, string.Format(Properties.Resources.Engine_2_Message, string.Join(Environment.NewLine, failures)));
	}

	public async Task<ImagePlan> ResolveAsync(string reference, string digest, string version, string architecture, string tag, CancellationToken cancellation)
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
		var cached = await this.TryInspectAsync(selector, platform, cancellation);
		var actual = cached.HasValue && Matches(cached.Value, platform) ? LocalDigest(cached.Value, repository, architecture, expected) : default;
		var index = default(Checksum);

		if(actual.IsEmpty)
		{
			var resolved = await this.ResolveManifestAsync(selector, platform, cancellation);
			actual = resolved.Digest;
			index = resolved.Index;

			if(!expected.IsEmpty && actual != expected)
				throw new ContainerizationException(4, Properties.Resources.Engine_3_Message);

			var fixedReference = actual.IsEmpty ? selector : $"{repository}@{FormatDigest(actual)}";
			await this.RunAsync(["pull", "--platform", platform, fixedReference], null, cancellation);
			cached = await this.TryInspectAsync(fixedReference, platform, cancellation);

			if(!cached.HasValue || !Matches(cached.Value, platform))
				throw new ContainerizationException(4, Properties.Resources.Engine_4_Message);

			if(actual.IsEmpty)
			{
				// Podman returns a raw single manifest without its digest. Verify the pulled config before accepting its manifest identity.
				if(resolved.Config.IsEmpty || resolved.Config != ImageId(cached.Value))
					throw new ContainerizationException(4, Properties.Resources.Engine_3_Message);

				actual = LocalDigest(cached.Value, repository, architecture, default);
			}

			if(actual.IsEmpty || !HasRepositoryDigest(cached.Value, repository, actual))
				throw new ContainerizationException(4, Properties.Resources.Engine_5_Message);
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

		if(tag != null)
			await this.RunAsync(["tag", id, tag], null, cancellation);

		return new()
		{
			Id = id,
			Tag = tag ?? cacheTag,
			Digest = FormatDigest(actual),
			Platform = platform,
			Version = version,
			Selector = selector,
			Repository = repository,
			IndexDigest = FormatDigest(index),
			Timestamp = DateTimeOffset.TryParse(Get(image, "Created"), CultureInfo.InvariantCulture, DateTimeStyles.None, out var created) && created.Year > 1 ? created.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture) : null,
			Size = image.TryGetProperty("Size", out var size) && size.ValueKind == JsonValueKind.Number && size.TryGetInt64(out var length) && length >= 0 ? length : null,
		};
	}

	public async Task BuildAsync(string directory, string platform, string reference, CancellationToken cancellation, string target = null)
	{
		await using var resources = new BuildResources(this);
		List<string> arguments;

		if(this.Executable == "podman")
		{
			// --no-cache alone still creates cache images; disable intermediate layers.
			arguments = ["build", "--layers=false", "--force-rm"];
		}
		else
		{
			// BuildKit caches belong to a private builder, removed even after a failed build.
			var builder = resources.Builder();
			await this.RunAsync(["buildx", "create", "--name", builder, "--driver", "docker-container"], directory, cancellation);
			arguments = ["buildx", "build", "--builder", builder, "--load"];
		}

		arguments.AddRange(["--platform", platform, "-t", reference]);

		if(target != null)
			arguments.AddRange(["--target", target]);

		arguments.Add(directory);
		await this.RunAsync(arguments, directory, cancellation);
	}

	public async Task ExportAsync(string reference, string path, CancellationToken cancellation)
	{
		Directory.CreateDirectory(Path.GetDirectoryName(path));
		await this.RunAsync(this.Executable == "podman" ? ["save", "--format", "docker-archive", "--output", path, reference] : ["image", "save", "--output", path, reference], null, cancellation);
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
			return;

		var mounts = source.Plan.Mounts.Where(mount => !mount.ReadOnly && !mount.Temporary && mount.User == null).ToArray();
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
		var parts = user.Split(':', 2);
		var account = entries.FirstOrDefault(fields => fields[0] == parts[0] || fields[2] == parts[0]);
		var uid = account?[2] ?? parts[0];
		var gid = parts.Length == 2 ? parts[1] : account?[3] ?? "0";

		if(!uint.TryParse(gid, out _))
		{
			await this.RunAsync(["cp", $"{container}:/etc/group", groups], null, cancellation);
			gid = File.ReadAllLines(groups).Select(line => line.Split(':')).FirstOrDefault(fields => fields.Length >= 3 && fields[0] == gid)?[2];
		}

		if(!uint.TryParse(uid, out _) || !uint.TryParse(gid, out _))
			throw new ContainerizationException(2, Properties.Resources.Engine_10_Message);

		foreach(var mount in mounts)
			mount.User = $"{uid}:{gid}";
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
			this.Executable == "podman" ?
			["manifest", "inspect", reference] :
			["buildx", "imagetools", "inspect", reference, "--format", "{{json .Manifest}}"], null, cancellation);

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
		var result = await runner.RunAsync(this.Executable, this.Executable == "docker" ? ["image", "inspect", "--platform", platform, reference] : ["image", "inspect", reference], null, cancellation);

		if(result.ExitCode != 0 && this.Executable == "docker" && (result.Error ?? "").Contains("platform", StringComparison.OrdinalIgnoreCase))
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

	private static bool Matches(JsonElement image, string platform) => Get(image, "Os") == "linux" && Get(image, "Architecture") == platform[6..];
	private static string CachePrefix(string repository, string architecture) => $"containerizer/cache/{Files.HashText(repository)}:{architecture}-";
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

		bool Matches(Checksum checksum) => (expected.IsEmpty || expected == checksum) && HasRepositoryDigest(image, repository, checksum);
	}

	[GeneratedRegex(@"^[A-Za-z0-9_][A-Za-z0-9_.-]{0,127}$")]
	private static partial Regex TagRegex();
	[GeneratedRegex(@"^[a-z0-9][a-z0-9.-]*(?::[0-9]+)?/[a-z0-9]+(?:[._-]+[a-z0-9]+)*(?:/[a-z0-9]+(?:[._-]+[a-z0-9]+)*)*$")]
	private static partial Regex RepositoryRegex();

	private static string Get(JsonElement value, string key) => value.TryGetProperty(key, out var property) && property.ValueKind == JsonValueKind.String ? property.GetString() : null;
	#endregion
}
