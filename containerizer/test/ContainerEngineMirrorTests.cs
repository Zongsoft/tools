using System;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Collections.Generic;

using Xunit;
using Zongsoft.Tools.Containerizer.Protocol;

namespace Zongsoft.Tools.Containerizer.Tests;

public sealed class ContainerEngineMirrorTests
{
	private const string REPOSITORY = "docker.io/library/redis";
	private static readonly string _digest = $"sha256:{new string('a', 64)}";
	private static readonly string _id = $"sha256:{new string('b', 64)}";
	private static readonly string _other = $"sha256:{new string('c', 64)}";
	private static readonly string _cacheTag = $"localhost/containerizer/cache/{Files.HashText(REPOSITORY)}:x64-{_digest[7..]}";

	[Theory]
	[InlineData("docker")]
	[InlineData("podman")]
	public async Task MirrorPullFallbackKeepsTheFirstResolvedDigest(string executable)
	{
		var runner = new MirrorRunner { FailFirstPull = true };
		var engine = Engine(executable, runner);
		var image = await engine.ResolveAsync(REPOSITORY, null, "8.10.2", "x64", TestContext.Current.CancellationToken);
		Assert.Equal(_digest, image.Digest);
		Assert.Equal(REPOSITORY, image.Repository);
		Assert.Equal($"{REPOSITORY}:8.10.2", image.Selector);
		Assert.Equal("two.example.com/docker.io/library/redis", image.SourceRepository);
		Assert.Single(runner.Calls, arguments => IsManifest(arguments));
		Assert.Equal([$"one.example.com/library/redis@{_digest}", $"two.example.com/docker.io/library/redis@{_digest}"], runner.Calls.Where(arguments => arguments[0] == "pull").Select(arguments => arguments[^1]));
		Assert.DoesNotContain(runner.Calls, arguments => arguments.Any(value => value == $"{REPOSITORY}:8.10.2") && arguments[0] == "pull");
		Assert.Contains(runner.Calls, arguments => arguments.SequenceEqual(["tag", _id, _cacheTag]));
	}

	[Theory]
	[InlineData("docker")]
	[InlineData("podman")]
	public async Task MetadataFailuresContinueInConfiguredOrderAndUseTheOriginalLast(string executable)
	{
		var runner = new MirrorRunner { FailMirrorMetadata = true };
		var image = await Engine(executable, runner).ResolveAsync(REPOSITORY, _digest, "8.10.2", "x64", TestContext.Current.CancellationToken);
		Assert.Equal(REPOSITORY, image.SourceRepository);
		Assert.Equal([$"one.example.com/library/redis@{_digest}", $"two.example.com/docker.io/library/redis@{_digest}", $"{REPOSITORY}@{_digest}"], runner.Calls.Where(IsManifest).Select(arguments => arguments[0] == "manifest" ? arguments[2] : arguments[3]));
		Assert.All(runner.Calls.Where(IsManifest), arguments => Assert.DoesNotContain(arguments, value => value.Contains(":8.10.2", StringComparison.Ordinal)));
	}

	[Theory]
	[InlineData("docker")]
	[InlineData("podman")]
	public async Task MalformedMirrorMetadataFallsBackWithoutSelectingAnIdentity(string executable)
	{
		var runner = new MirrorRunner { MalformedFirstMetadata = true };
		var image = await Engine(executable, runner).ResolveAsync(REPOSITORY, _digest, "8.10.2", "x64", TestContext.Current.CancellationToken);
		Assert.Equal("two.example.com/docker.io/library/redis", image.SourceRepository);
		Assert.Equal(_digest, image.Digest);
		Assert.Equal(2, runner.Calls.Count(IsManifest));
		Assert.Equal($"two.example.com/docker.io/library/redis@{_digest}", Assert.Single(runner.Calls, arguments => arguments[0] == "pull")[^1]);
	}

	[Theory]
	[InlineData("docker", false)]
	[InlineData("podman", false)]
	[InlineData("docker", true)]
	[InlineData("podman", true)]
	public async Task AVerifiedLogicalCacheSurvivesChangingOrRemovingMirrorRules(string executable, bool removeMirrors)
	{
		var runner = new MirrorRunner { Cached = true };
		var engine = Engine(executable, runner);
		if(removeMirrors)
			engine.Mirrors = new();
		var image = await engine.ResolveAsync(REPOSITORY, _digest, "8.10.2", "x64", TestContext.Current.CancellationToken);
		Assert.Equal(_digest, image.Digest);
		Assert.Equal(_id, image.Id);
		Assert.Equal(REPOSITORY, image.Repository);
		Assert.DoesNotContain(runner.Calls, arguments => IsManifest(arguments) || arguments[0] == "pull");
	}

	[Fact]
	public async Task ACacheTagAloneCannotAuthorizeADifferentManifest()
	{
		var runner = new MirrorRunner { Cached = true, InvalidCachedDigest = true };
		var image = await Engine("docker", runner).ResolveAsync(REPOSITORY, _digest, "8.10.2", "x64", TestContext.Current.CancellationToken);
		Assert.Equal(_digest, image.Digest);
		Assert.Single(runner.Calls, arguments => arguments[0] == "pull");
	}

	[Fact]
	public async Task PodmanRawManifestChecksTheConfigurationBeforeAcceptingMirrorIdentity()
	{
		var runner = new MirrorRunner { RawManifest = true };
		var engine = Engine("podman", runner);
		var image = await engine.ResolveAsync(REPOSITORY, null, "8.10.2", "x64", TestContext.Current.CancellationToken);
		Assert.Equal(_digest, image.Digest);
		Assert.Equal(_id, image.Id);
		Assert.Equal(_id, engine.BuildReference(image));
		Assert.Equal("one.example.com/library/redis:8.10.2", Assert.Single(runner.Calls, arguments => arguments[0] == "pull")[^1]);
		var json = JsonSerializer.Serialize(image, ProtocolJson.Default.ImagePlan);
		Assert.DoesNotContain("one.example.com", json);
	}

	private static ContainerEngine Engine(string executable, MirrorRunner runner) => new(executable, runner)
	{
		Mirrors = new() { Registries = new() { ["docker.io"] = ["one.example.com", "two.example.com/docker.io"] } },
	};
	private static bool IsManifest(IReadOnlyList<string> arguments) => arguments[0] == "manifest" || arguments.Take(2).SequenceEqual(["buildx", "imagetools"]);

	private sealed class MirrorRunner : IProcessRunner
	{
		public bool FailFirstPull { get; init; }
		public bool FailMirrorMetadata { get; init; }
		public bool MalformedFirstMetadata { get; init; }
		public bool Cached { get; init; }
		public bool InvalidCachedDigest { get; init; }
		public bool RawManifest { get; init; }
		public List<string[]> Calls { get; } = [];
		private readonly HashSet<string> _pulled = [];

		public Task<ProcessResult> RunAsync(string executable, IReadOnlyList<string> arguments, string directory, CancellationToken cancellation, int timeoutSeconds = 900)
		{
			this.Calls.Add(arguments.ToArray());
			if(arguments[0] == "image")
			{
				var selector = arguments[^1];
				var cached = this.Cached && selector == _cacheTag && _pulled.Count == 0;
				if(!cached && !_pulled.Contains(selector))
					return Result(1, "", "image missing");
				var repository = cached ? "old.example.com/library/redis" : ContainerEngine.Repository(selector);
				return Result(0, JsonSerializer.Serialize(new[] { new { Id = _id, Os = "linux", Architecture = "amd64", Digest = cached && this.InvalidCachedDigest ? _other : _digest, RepoDigests = new[] { $"{repository}@{(cached && this.InvalidCachedDigest ? _other : _digest)}" }, RepoTags = cached ? new[] { _cacheTag } : Array.Empty<string>() } }), "");
			}

			if(IsManifest(arguments))
			{
				var selector = arguments[0] == "manifest" ? arguments[2] : arguments[3];
				if(this.MalformedFirstMetadata && selector.StartsWith("one.example.com", StringComparison.Ordinal))
					return Result(0, "<invalid manifest>", "");
				if(this.FailMirrorMetadata && !selector.StartsWith(REPOSITORY, StringComparison.Ordinal))
					return Result(125, "", "registry unavailable");
				var metadata = this.RawManifest ? JsonSerializer.Serialize(new { config = new { digest = _id } }) :
					JsonSerializer.Serialize(new { manifests = new[] { new { digest = _digest, platform = new { os = "linux", architecture = "amd64" } } } });
				return Result(0, metadata, "");
			}

			if(arguments[0] == "pull")
			{
				if(this.FailFirstPull && arguments[^1].StartsWith("one.example.com", StringComparison.Ordinal))
					return Result(125, "", "blob unavailable");
				_pulled.Add(arguments[^1]);
			}

			return Result(0, "", "");
		}

		private static Task<ProcessResult> Result(int code, string output, string error) => Task.FromResult(new ProcessResult(code, output, error));
	}
}
