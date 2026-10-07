using System;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Collections.Generic;

using Xunit;

using Zongsoft.Tools.Containerizer.Protocol;

namespace Zongsoft.Tools.Containerizer.Tests;

public sealed class ContainerEngineTests
{
	private static readonly string _digest = "sha256:" + new string('a', 64);
	private static readonly string _id = "sha256:" + new string('b', 64);
	private const string REPOSITORY = "docker.io/library/redis";

	public static TheoryData<string> InvalidDigests => new()
	{
		"", new string('a', 64), "sha256:" + new string('g', 64), "sha256:" + new string('a', 63),
		"sha256:" + new string('a', 65), "sha256:" + new string('a', 32), "sha256:" + new string('a', 128),
		"SHA256:" + new string('a', 64), "sha256:" + new string('A', 64), "sha3-256:" + new string('a', 64),
		"sha512:" + new string('a', 128),
	};

	[Theory]
	[MemberData(nameof(InvalidDigests))]
	public async Task InvalidPinnedDigestsFailBeforeAccessingTheEngineAsync(string digest)
	{
		var runner = new ProbeRunner((_, _) => throw new InvalidOperationException("Must not contact the engine."));
		var exception = await Assert.ThrowsAsync<ContainerizationException>(() => new ContainerEngine("docker", runner).ResolveAsync(REPOSITORY, digest, "latest", "x64", CancellationToken.None));
		Assert.Equal(2, exception.Code);
		Assert.Empty(runner.Calls);
	}

	[Theory]
	[InlineData("single")]
	[InlineData("child")]
	[InlineData("index")]
	public async Task InvalidRegistryDigestsDoNotPullOrTagImagesAsync(string location)
	{
		var invalid = "sha256:" + new string('g', 64);
		var manifest = location == "single" ? JsonSerializer.Serialize(new { digest = invalid }) :
			JsonSerializer.Serialize(new { digest = location == "index" ? invalid : _digest, manifests = new[] { new { digest = location == "child" ? invalid : _digest, platform = new { os = "linux", architecture = "amd64" } } } });
		var runner = new ProbeRunner((_, arguments) => arguments[0] == "image" ? new(1, "", "not found") : new(0, manifest, ""));
		var exception = await Assert.ThrowsAsync<ContainerizationException>(() => new ContainerEngine("docker", runner).ResolveAsync(REPOSITORY, null, "latest", "x64", CancellationToken.None));
		Assert.Equal(4, exception.Code);
		Assert.DoesNotContain(runner.Calls, call => call.StartsWith("docker pull", StringComparison.Ordinal) || call.StartsWith("docker tag", StringComparison.Ordinal));
	}

	[Fact]
	public async Task FixedDigestMustMatchTheSelectedPlatformManifestAsync()
	{
		var manifest = JsonSerializer.Serialize(new { manifests = new[] { new { digest = "sha256:" + new string('c', 64), platform = new { os = "linux", architecture = "amd64" } } } });
		var runner = new ProbeRunner((_, arguments) => arguments[0] == "image" ? new(1, "", "not found") : new(0, manifest, ""));
		var exception = await Assert.ThrowsAsync<ContainerizationException>(() => new ContainerEngine("docker", runner).ResolveAsync(REPOSITORY, _digest, "latest", "x64", CancellationToken.None));
		Assert.Equal(4, exception.Code);
		Assert.DoesNotContain(runner.Calls, call => call.StartsWith("docker pull", StringComparison.Ordinal) || call.StartsWith("docker tag", StringComparison.Ordinal));
	}

	[Theory]
	[InlineData(null)]
	[InlineData("different")]
	[InlineData("invalid")]
	public async Task UnverifiedPodmanConfigurationDoesNotAcquireACacheTagAsync(string configuration)
	{
		var pulled = false;
		var runner = new ProbeRunner((_, arguments) =>
		{
			if(arguments[0] == "image")
				return pulled ? new(0, CreateInspectionJson(_digest, [], true), "") : new(1, "", "not found");
			if(arguments[0] == "manifest")
				return new(0, JsonSerializer.Serialize(new { config = new { digest = configuration == null ? null : "sha256:" + new string(configuration == "invalid" ? 'g' : 'c', 64) } }), "");
			if(arguments[0] == "pull")
				pulled = true;
			return new(0, "", "");
		});
		var exception = await Assert.ThrowsAsync<ContainerizationException>(() => new ContainerEngine("podman", runner).ResolveAsync(REPOSITORY, null, "latest", "x64", CancellationToken.None));
		Assert.Equal(4, exception.Code);
		Assert.DoesNotContain(runner.Calls, call => call.StartsWith("podman tag", StringComparison.Ordinal));
	}

	[Theory]
	[InlineData("docker", true)]
	[InlineData("podman", false)]
	public async Task VerifiedLocalImagesAvoidRegistryAndPullAsync(string executable, bool classic)
	{
		var cacheTag = "localhost/containerizer/cache/" + Files.HashText(REPOSITORY) + ":x64-" + _digest[7..];
		var runner = new ProbeRunner((_, arguments) => arguments[0] == "image" ? new(0, CreateInspectionJson(classic ? null : _digest, [cacheTag], true), "") : new(0, "", ""));
		var image = await new ContainerEngine(executable, runner).ResolveAsync(REPOSITORY, null, "latest", "x64", CancellationToken.None);
		Assert.Equal(_digest, image.Digest);
		Assert.Equal(_id, image.Id);
		Assert.Equal("latest", image.SourceTag);
		Assert.Equal("2026-09-15T05:30:05Z", image.Timestamp);
		Assert.Equal(123456, image.Size);
		Assert.DoesNotContain(runner.Calls, call => call.Contains("pull", StringComparison.Ordinal) || call.Contains("manifest", StringComparison.Ordinal) || call.Contains("imagetools", StringComparison.Ordinal));
		Assert.Contains(executable + " tag " + _id + " " + REPOSITORY + ":latest", runner.Calls);
		Assert.Equal(cacheTag, image.Reference);
		Assert.Equal([executable + " tag " + _id + " " + REPOSITORY + ":latest", executable + " tag " + _id + " " + cacheTag], runner.Calls.Where(call => call.StartsWith(executable + " tag ", StringComparison.Ordinal)));
	}

	[Fact]
	public async Task IncompleteCacheResolvesTheTagAndUsesTheNewChildManifestAsync()
	{
		var pulled = false;
		var runner = new ProbeRunner((_, arguments) =>
		{
			if(arguments[0] == "image")
				return new(0, CreateInspectionJson(null, [], pulled), "");
			if(arguments[0] == "buildx")
				return new(0, JsonSerializer.Serialize(new { digest = "sha256:" + new string('c', 64), manifests = new[] { new { digest = _digest, platform = new { os = "linux", architecture = "amd64" } } } }), "");

			if(arguments[0] == "pull")
			{
				Assert.Equal(REPOSITORY + "@" + _digest, arguments[^1]);
				pulled = true;
			}

			return new(0, "", "");
		});
		var image = await new ContainerEngine("docker", runner).ResolveAsync(REPOSITORY, null, "8.4", "x64", CancellationToken.None);
		Assert.True(pulled);
		Assert.Equal(_digest, image.Digest);
		Assert.Equal("8.4", image.SourceTag);
		Assert.Equal("sha256:" + new string('c', 64), image.IndexDigest);
		Assert.Contains("docker buildx imagetools inspect " + REPOSITORY + ":8.4 --format {{json .Manifest}}", runner.Calls);
	}

	[Fact]
	public async Task ReplayNeverFallsBackToTheTagWhenTheDigestIsUnavailableAsync()
	{
		var runner = new ProbeRunner((_, _) => new(1, "", "digest unavailable"));
		await Assert.ThrowsAsync<ContainerizationException>(() => new ContainerEngine("docker", runner).ResolveAsync(REPOSITORY, _digest, "latest", "x64", CancellationToken.None));
		Assert.All(runner.Calls, call => Assert.DoesNotContain(":latest", call, StringComparison.Ordinal));
		Assert.DoesNotContain(runner.Calls, call => call.StartsWith("docker pull", StringComparison.Ordinal));
	}

	[Theory]
	[InlineData(true)]
	[InlineData(false)]
	public async Task SinglePodmanManifestMustMatchThePulledConfigurationAsync(bool bareId)
	{
		var pulled = false;
		var runner = new ProbeRunner((_, arguments) =>
		{
			if(arguments[0] == "image")
				return pulled ? new(0, CreateInspectionJson(_digest, [], true, id: bareId ? _id[7..] : _id), "") : new(1, "", "not found");
			if(arguments[0] == "manifest")
				return new(0, JsonSerializer.Serialize(new { config = new { digest = _id } }), "");
			if(arguments[0] == "pull")
				pulled = true;
			return new(0, "", "");
		});
		var image = await new ContainerEngine("podman", runner).ResolveAsync(REPOSITORY, null, "latest", "x64", CancellationToken.None);
		Assert.Equal(_digest, image.Digest);
		Assert.Contains("podman pull --platform linux/amd64 " + REPOSITORY + ":latest", runner.Calls);
	}

	[Theory]
	[InlineData("arm64", true)]
	[InlineData("amd64", false)]
	public async Task PulledImagesMustMatchPlatformAndRepositoryAsync(string architecture, bool association)
	{
		var pulled = false;
		var runner = new ProbeRunner((_, arguments) =>
		{
			if(arguments[0] == "image")
				return pulled ? new(0, CreateInspectionJson(null, [], association, architecture), "") : new(1, "", "not found");
			if(arguments[0] == "buildx")
				return new(0, JsonSerializer.Serialize(new { digest = _digest }), "");
			if(arguments[0] == "pull")
				pulled = true;
			return new(0, "", "");
		});
		await Assert.ThrowsAsync<ContainerizationException>(() => new ContainerEngine("docker", runner).ResolveAsync(REPOSITORY, null, "latest", "x64", CancellationToken.None));
		Assert.DoesNotContain(runner.Calls, call => call.StartsWith("docker tag", StringComparison.Ordinal));
	}

	[Fact]
	public async Task MissingDisplayMetadataDoesNotTriggerRegistryAccessAsync()
	{
		var inspect = JsonSerializer.Serialize(new[] { new { Id = _id, Os = "linux", Architecture = "amd64", Digest = _digest, RepoDigests = new[] { REPOSITORY + "@" + _digest } } });
		var runner = new ProbeRunner((_, arguments) => new(0, arguments[0] == "image" ? inspect : "", ""));
		var image = await new ContainerEngine("podman", runner).ResolveAsync(REPOSITORY, _digest, "8.4", "x64", CancellationToken.None);
		Assert.Null(image.Timestamp);
		Assert.Null(image.Size);
		Assert.DoesNotContain(runner.Calls, call => call.Contains("manifest", StringComparison.Ordinal) || call.Contains("pull", StringComparison.Ordinal));
	}

	[Fact]
	public async Task ClassicDockerInspectionFallsBackWhenPlatformSelectionIsUnavailableAsync()
	{
		var cacheTag = "localhost/containerizer/cache/" + Files.HashText(REPOSITORY) + ":x64-" + _digest[7..];
		var runner = new ProbeRunner((_, arguments) => arguments.Contains("--platform") ? new(1, "", "unknown flag: --platform") :
			arguments[0] == "image" ? new(0, CreateInspectionJson(null, [cacheTag], true), "") : new(0, "", ""));
		var image = await new ContainerEngine("docker", runner).ResolveAsync(REPOSITORY, null, "latest", "x64", CancellationToken.None);
		Assert.Equal(_digest, image.Digest);
		Assert.Contains("docker image inspect " + REPOSITORY + ":latest", runner.Calls);
		Assert.DoesNotContain(runner.Calls, call => call.Contains("pull", StringComparison.Ordinal) || call.Contains("imagetools", StringComparison.Ordinal));
	}

	private static string CreateInspectionJson(string digest, string[] tags, bool association, string architecture = "amd64", string id = null) => JsonSerializer.Serialize(new[]
	{
		new
		{
			Id = id ?? _id, Os = "linux", Architecture = architecture, Digest = digest,
			RepoTags = tags, RepoDigests = association ? new[] { REPOSITORY + "@" + _digest } : [],
			Created = "2026-09-15T13:30:05.4441212+08:00", Size = 123456,
			Config = new { Labels = new Dictionary<string, string> { ["org.opencontainers.image.version"] = "99.9" } },
		},
	});

	[Fact]
	public async Task EngineFailurePreservesDiagnosticOutputAsync()
	{
		var runner = new ProbeRunner((_, _) => new(125, "apt dependency not found", "proxy connection refused"));
		var engine = new ContainerEngine("podman", runner);
		var exception = await Assert.ThrowsAsync<ContainerizationException>(() => engine.RunAsync(["manifest", "inspect", "redis"], null, CancellationToken.None));
		Assert.Equal(4, exception.Code);
		Assert.Contains("proxy connection refused", exception.Message);
		Assert.Contains("apt dependency not found", exception.Message);
	}

	[Fact]
	public async Task AutoFallsBackWhenDockerComposeIsUnavailableAsync()
	{
		var runner = new ProbeRunner((executable, arguments) => new(executable == "docker" && arguments[0] == "compose" ? 1 : 0, "", "missing Compose provider"));
		var engine = await ContainerEngine.ConnectAsync("auto", runner, CancellationToken.None);
		Assert.Equal("podman", engine.Executable);
		Assert.Equal(["docker info", "docker compose version", "podman info", "podman compose version"], runner.Calls);
	}

	[Fact]
	public async Task UnavailableEnginesReportEachProbeFailureAsync()
	{
		var runner = new ProbeRunner((executable, _) => executable == "docker" ? throw new ContainerizationException(3, "executable missing") : new(125, "", "connection refused"));
		var exception = await Assert.ThrowsAsync<ContainerizationException>(() => ContainerEngine.ConnectAsync("auto", runner, CancellationToken.None));
		Assert.Equal(3, exception.Code);
		Assert.Contains("docker info: executable missing", exception.Message);
		Assert.Contains("podman info: connection refused", exception.Message);
		Assert.DoesNotContain("compose", string.Join(';', runner.Calls));
	}

	[Fact]
	public async Task ExplicitEngineReportsComposeFailureAsEnvironmentErrorAsync()
	{
		var runner = new ProbeRunner((_, arguments) => new(arguments[0] == "compose" ? 1 : 0, "", "provider unavailable"));
		var exception = await Assert.ThrowsAsync<ContainerizationException>(() => ContainerEngine.ConnectAsync("podman", runner, CancellationToken.None));
		Assert.Equal(3, exception.Code);
		Assert.Contains("podman compose version: provider unavailable", exception.Message);
		Assert.Equal(["podman info", "podman compose version"], runner.Calls);
	}

	[Theory]
	[InlineData("docker", true)]
	[InlineData("podman", true)]
	[InlineData("auto", false)]
	public async Task UnavailableEngineGuidanceMatchesTheSelectionAndComposeRequirementAsync(string choice, bool compose)
	{
		var runner = new ProbeRunner((_, _) => new(1, "", "unavailable"));
		var failure = await Assert.ThrowsAsync<ContainerizationException>(() => ContainerEngine.ConnectAsync(choice, runner, TestContext.Current.CancellationToken, compose));

		if(choice == "docker")
			Assert.DoesNotContain("podman", failure.Message, StringComparison.OrdinalIgnoreCase);
		if(choice == "podman")
			Assert.DoesNotContain("docker", failure.Message, StringComparison.OrdinalIgnoreCase);
		if(!compose)
			Assert.DoesNotContain("Compose", failure.Message, StringComparison.OrdinalIgnoreCase);
		Assert.Contains("info", failure.Message);
	}

	[Fact]
	public async Task ProbeTimeoutAllowsAutoFallbackAsync()
	{
		var runner = new ProbeRunner((executable, _) => executable == "docker" ? throw new OperationCanceledException() : new(0, "", ""));
		var engine = await ContainerEngine.ConnectAsync("auto", runner, CancellationToken.None);
		Assert.Equal("podman", engine.Executable);
	}

	[Fact]
	public async Task UserCancellationDoesNotProbeAnotherEngineAsync()
	{
		using var cancellation = new CancellationTokenSource();
		cancellation.Cancel();
		var runner = new ProbeRunner((_, _) => throw new OperationCanceledException(cancellation.Token));
		await Assert.ThrowsAsync<OperationCanceledException>(() => ContainerEngine.ConnectAsync("auto", runner, cancellation.Token));
		Assert.Equal(["docker info"], runner.Calls);
	}

	private sealed class ProbeRunner(Func<string, IReadOnlyList<string>, ProcessResult> probe) : IProcessRunner
	{
		public List<string> Calls { get; } = [];
		public Task<int> StreamAsync(string executable, IReadOnlyList<string> arguments, string directory, CancellationToken cancellation) => throw new NotSupportedException();

		public Task<ProcessResult> RunAsync(string executable, IReadOnlyList<string> arguments, string directory, CancellationToken cancellation, int timeoutSeconds = 900)
		{
			this.Calls.Add(executable + " " + string.Join(' ', arguments));
			return Task.FromResult(probe(executable, arguments));
		}
	}
}
