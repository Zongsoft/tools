using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Collections.Generic;

using Xunit;

using Zongsoft.Tools.Containerizer.Protocol;

namespace Zongsoft.Tools.Containerizer.Tests;

public sealed class BootstrapPackageBuilderTests : IDisposable
{
	private readonly string _root = Path.Combine(Path.GetTempPath(), "containerizer-bootstrap-tests-" + Guid.NewGuid().ToString("N"));
	public BootstrapPackageBuilderTests() => Directory.CreateDirectory(_root);
	public void Dispose() => Directory.Delete(_root, true);

	[Theory]
	[InlineData("offline")]
	[InlineData("online")]
	public async Task ImportedRpmCollectionPreservesRepositoryAndPackagePolicyAsync(string mode)
	{
		var imported = Path.Combine(_root, ".containerizer", "bootstrap", "rocky@9_x64");
		var expected = CreateCollection(imported);
		File.Delete(Path.Combine(imported, "packages", "requirements.txt"));
		var runner = new CollectionRunner();
		var delivery = Path.Combine(_root, "delivery");
		var actual = await this.CreateBuilder(runner).BuildAsync(this.CreateManifest(mode), Path.Combine(_root, "work"), delivery, TestContext.Current.CancellationToken);

		Assert.Equal(mode, actual.Mode);
		Assert.Equal(expected.Requirements, actual.Requirements);
		Assert.Equal(expected.Packages.Select(package => package.Name), actual.Packages.Select(package => package.Name));
		Assert.Equal("packages/repodata", actual.Repository);
		Assert.Equal(File.ReadAllBytes(Path.Combine(imported, expected.Metadata)), File.ReadAllBytes(Path.Combine(delivery, actual.Metadata)));
		AssertRepositoryBytes(imported, delivery);
		Assert.All(actual.Packages, package =>
		{
			Assert.Equal(mode == "offline", File.Exists(Path.Combine(delivery, package.Path)));
			Assert.Equal(package.Hash, Files.Hash(Path.Combine(imported, package.Path)));
			if(mode == "offline")
				Assert.Equal(File.ReadAllBytes(Path.Combine(imported, package.Path)), File.ReadAllBytes(Path.Combine(delivery, package.Path)));
		});
		Assert.Empty(runner.Calls);
	}

	[Theory]
	[InlineData("offline")]
	[InlineData("online")]
	public async Task CollectedRpmRequirementsAndMetadataSurviveOnlineCacheReuseAsync(string mode)
	{
		var collected = Path.Combine(_root, "collected");
		var expected = CreateCollection(collected);
		var runner = new CollectionRunner(collected);
		var cacheRoot = Path.Combine(_root, "cache");
		var legacy = BuildStorage.GetBootstrapCache(_root, "rocky@9_x64", cacheRoot);
		Directory.CreateDirectory(legacy);
		File.WriteAllText(Path.Combine(legacy, "bootstrap.lock.json"), "{}");
		var delivery = Path.Combine(_root, "first");
		var actual = await this.CreateBuilder(runner).BuildAsync(this.CreateManifest("online"), Path.Combine(_root, "work"), delivery, TestContext.Current.CancellationToken);
		var cache = Path.Combine(legacy, Files.Hash(TemplateCatalog.Find("bootstrap-rpm.sh")));
		var cached = Files.Load(Path.Combine(cache, "bootstrap.lock.json"), ProtocolJson.Default.BootstrapPlan);

		Assert.Equal(expected.Requirements, actual.Requirements);
		Assert.Equal(expected.Packages.Select(package => package.Name), actual.Packages.Select(package => package.Name));
		Assert.Equal(expected.Requirements, cached.Requirements);
		Assert.Equal("packages/repodata", cached.Repository);
		Assert.All(actual.Packages, package =>
		{
			Assert.False(File.Exists(Path.Combine(delivery, package.Path)));
			Assert.Equal(package.Hash, Files.Hash(Path.Combine(cache, package.Path)));
		});
		AssertRepositoryBytes(collected, delivery);
		AssertRepositoryBytes(collected, cache);
		Assert.Single(runner.Calls, arguments => arguments[0] == "build");
		Assert.Single(runner.Calls, arguments => arguments[0] == "cp");

		var reused = Path.Combine(_root, "reused");
		var unused = new CollectionRunner();
		var restored = await this.CreateBuilder(unused).BuildAsync(this.CreateManifest(mode), Path.Combine(_root, "unused"), reused, TestContext.Current.CancellationToken);

		Assert.Equal(mode, restored.Mode);
		Assert.Equal(expected.Requirements, restored.Requirements);
		AssertRepositoryBytes(collected, reused);
		Assert.All(restored.Packages, package => Assert.Equal(mode == "offline", File.Exists(Path.Combine(reused, package.Path))));
		Assert.Empty(unused.Calls);
	}

	[Theory]
	[InlineData("null-requirements")]
	[InlineData("empty-requirements")]
	[InlineData("null-name")]
	[InlineData("empty-name")]
	[InlineData("option-name")]
	[InlineData("space-name")]
	[InlineData("unicode-name")]
	[InlineData("capability-name")]
	[InlineData("unknown-name")]
	[InlineData("case-name")]
	[InlineData("duplicate-name")]
	[InlineData("null-repository")]
	[InlineData("empty-repository")]
	[InlineData("absolute-repository")]
	[InlineData("escaping-repository")]
	[InlineData("missing-repository")]
	[InlineData("missing-repomd")]
	public async Task InvalidRpmRepositoryDeclarationsAreRejectedOnImportAsync(string failure)
	{
		var imported = Path.Combine(_root, ".containerizer", "bootstrap", "rocky@9_x64");
		var plan = CreateCollection(imported);

		switch(failure)
		{
			case "null-requirements":
				plan.Requirements = null;
				break;
			case "empty-requirements":
				plan.Requirements.Clear();
				break;
			case "null-name":
				plan.Requirements = [null];
				break;
			case "empty-name":
				plan.Requirements = [""];
				break;
			case "option-name":
				plan.Requirements = ["--allowerasing"];
				break;
			case "space-name":
				plan.Requirements = ["docker ce"];
				break;
			case "unicode-name":
				plan.Requirements = ["d\u00F6cker"];
				break;
			case "capability-name":
				plan.Requirements = ["coreutils >= 0"];
				break;
			case "unknown-name":
				plan.Requirements = ["missing-package"];
				break;
			case "case-name":
				plan.Requirements = ["Docker-ce"];
				break;
			case "duplicate-name":
				plan.Requirements.Add(plan.Requirements[0]);
				break;
			case "null-repository":
				plan.Repository = null;
				break;
			case "empty-repository":
				plan.Repository = "";
				break;
			case "absolute-repository":
				plan.Repository = "/packages/repodata";
				break;
			case "escaping-repository":
				plan.Repository = "../repodata";
				break;
			case "missing-repository":
				plan.Repository = "packages/missing";
				break;
			case "missing-repomd":
				File.Delete(Path.Combine(imported, plan.Repository, "repomd.xml"));
				break;
		}

		if(failure is "option-name" or "space-name" or "unicode-name" or "capability-name")
			plan.Packages[0].Name = plan.Requirements[0];

		Files.Save(Path.Combine(imported, "bootstrap.lock.json"), plan, ProtocolJson.Default.BootstrapPlan);
		var runner = new CollectionRunner();
		var exception = await Assert.ThrowsAsync<ContainerizationException>(() => this.CreateBuilder(runner).BuildAsync(this.CreateManifest("offline"), Path.Combine(_root, "work"), Path.Combine(_root, "delivery"), TestContext.Current.CancellationToken));

		Assert.Equal(4, exception.Code);
		Assert.Empty(runner.Calls);
	}

	[Theory]
	[InlineData("0root")]
	[InlineData("root.name")]
	[InlineData("root_name")]
	[InlineData("root+name")]
	[InlineData("root-name")]
	public async Task ValidRpmRequirementNamesPreserveExactIdentityAsync(string name)
	{
		var imported = Path.Combine(_root, ".containerizer", "bootstrap", "rocky@9_x64");
		var plan = CreateCollection(imported);
		plan.Packages[0].Name = name;
		plan.Requirements = [name];
		Files.Save(Path.Combine(imported, "bootstrap.lock.json"), plan, ProtocolJson.Default.BootstrapPlan);

		var actual = await this.CreateBuilder(new CollectionRunner()).BuildAsync(this.CreateManifest("offline"), Path.Combine(_root, "work"), Path.Combine(_root, "delivery"), TestContext.Current.CancellationToken);

		Assert.Equal(name, Assert.Single(actual.Requirements));
	}

	[Theory]
	[InlineData(false)]
	[InlineData(true)]
	public async Task ImportedRpmPackagesStillRequireMatchingHashAndLengthAsync(bool length)
	{
		var imported = Path.Combine(_root, ".containerizer", "bootstrap", "rocky@9_x64");
		var plan = CreateCollection(imported);
		var package = plan.Packages[0];

		if(length)
			package.Length++;
		else
			package.Hash = new string('0', 64);

		Files.Save(Path.Combine(imported, "bootstrap.lock.json"), plan, ProtocolJson.Default.BootstrapPlan);
		var runner = new CollectionRunner();
		var exception = await Assert.ThrowsAsync<ContainerizationException>(() => this.CreateBuilder(runner).BuildAsync(this.CreateManifest("online"), Path.Combine(_root, "work"), Path.Combine(_root, "delivery"), TestContext.Current.CancellationToken));

		Assert.Equal(4, exception.Code);
		Assert.Empty(runner.Calls);
	}

	[Fact]
	public void RpmCollectorRecordsRootRequirementsAndCreatesRepository()
	{
		var script = File.ReadAllText(TemplateCatalog.Find("bootstrap-rpm.sh"));

		Assert.Contains("dnf install -y dnf-plugins-core ca-certificates createrepo_c", script, StringComparison.Ordinal);
		Assert.Contains("printf '%s\\n' \"$@\" > /delivery/requirements.txt", script, StringComparison.Ordinal);
		Assert.Contains("dnf download --resolve --alldeps --destdir=/delivery \"$@\"", script, StringComparison.Ordinal);
		Assert.Contains("createrepo_c /delivery", script, StringComparison.Ordinal);
		Assert.True(script.IndexOf("requirements.txt", StringComparison.Ordinal) < script.IndexOf("dnf download", StringComparison.Ordinal));
		Assert.True(script.IndexOf("dnf download", StringComparison.Ordinal) < script.IndexOf("createrepo_c /delivery", StringComparison.Ordinal));
	}

	private BootstrapPackageBuilder CreateBuilder(CollectionRunner runner) => new(new ContainerEngine("podman", runner), Path.Combine(_root, "cache"));
	private ContainerManifest CreateManifest(string mode) => new()
	{
		["source"] = _root,
		["distribution"] = "rocky@9",
		["architecture"] = "x64",
		["bootstrap"] = mode,
	};

	private static BootstrapPlan CreateCollection(string directory)
	{
		var plan = new BootstrapPlan { Profile = "rocky@9_x64", EngineVersion = "1.0", ComposeVersion = "1.0", Metadata = "packages/metadata.tsv", Repository = "packages/repodata", Requirements = ["docker-ce", "docker-compose-plugin", "coreutils"] };
		Directory.CreateDirectory(Path.Combine(directory, "packages", "repodata", "nested"));
		File.WriteAllText(Path.Combine(directory, plan.Repository, "repomd.xml"), "<repomd>fixture</repomd>");
		File.WriteAllBytes(Path.Combine(directory, plan.Repository, "nested", "primary.xml.gz"), [0, 1, 2, 254, 255]);

		foreach(var name in new[] { "docker-ce", "docker-compose-plugin", "coreutils", "coreutils-single", "curl", "curl-minimal" })
		{
			var relative = $"packages/{name}.rpm";
			var path = Path.Combine(directory, relative);
			File.WriteAllText(path, $"opaque {name} RPM fixture");
			plan.Packages.Add(new() { Name = name, Version = "1.0", Architecture = "x86_64", Path = relative, Url = $"https://example.com/{name}.rpm", Hash = Files.Hash(path), Length = new FileInfo(path).Length, Dependencies = "fixture-dependency" });
		}

		File.WriteAllText(Path.Combine(directory, plan.Metadata), string.Join('\n', plan.Packages.Select(package => $"{Path.GetFileName(package.Path)}\t{package.Name}\t{package.Version}\t{package.Architecture}\t{package.Url}\t{package.Dependencies}")) + "\n");
		File.WriteAllLines(Path.Combine(directory, "packages", "requirements.txt"), plan.Requirements);
		Files.Save(Path.Combine(directory, "bootstrap.lock.json"), plan, ProtocolJson.Default.BootstrapPlan);
		return plan;
	}

	private static void AssertRepositoryBytes(string expected, string actual)
	{
		foreach(var relative in new[] { "packages/repodata/repomd.xml", "packages/repodata/nested/primary.xml.gz" })
			Assert.Equal(File.ReadAllBytes(Path.Combine(expected, relative)), File.ReadAllBytes(Path.Combine(actual, relative)));
	}

	private sealed class CollectionRunner(string collection = null) : IProcessRunner
	{
		public List<string[]> Calls { get; } = [];
		public Task<int> StreamAsync(string executable, IReadOnlyList<string> arguments, string directory, CancellationToken cancellation) => throw new NotSupportedException();
		public Task<ProcessResult> RunAsync(string executable, IReadOnlyList<string> arguments, string directory, CancellationToken cancellation, int timeoutSeconds = 900)
		{
			this.Calls.Add(arguments.ToArray());
			Assert.NotNull(collection);
			if(arguments[0] == "image" && arguments[1] == "inspect")
				return Task.FromResult(new ProcessResult(0, JsonSerializer.Serialize(new[] { new { Id = "sha256:" + new string('a', 64), Digest = "sha256:" + new string('b', 64), Os = "linux", Architecture = "amd64", RepoDigests = new[] { "docker.io/rockylinux/rockylinux@sha256:" + new string('b', 64) } } }), ""));

			if(arguments[0] == "cp")
				Files.CopyTree(Path.Combine(collection, "packages"), arguments[^1]);

			return Task.FromResult(new ProcessResult(0, "", ""));
		}
	}
}
