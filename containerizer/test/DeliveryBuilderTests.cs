using System;
using System.IO;
using System.Text;
using System.Formats.Tar;
using System.IO.Compression;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Collections.Generic;

using Xunit;
using Zongsoft.Components;
using Zongsoft.Tools.Containerizer.Protocol;

namespace Zongsoft.Tools.Containerizer.Tests;

public sealed class DeliveryBuilderTests : IDisposable
{
	private readonly string _root = Path.Combine(Path.GetTempPath(), "containerizer-build-tests-" + Guid.NewGuid().ToString("N"));
	private static readonly string _digest = "sha256:" + new string('a', 64);
	public DeliveryBuilderTests() => Directory.CreateDirectory(_root);
	public void Dispose() => Directory.Delete(_root, true);

	[Theory]
	[InlineData("offline", null)]
	[InlineData("online", null)]
	[InlineData("offline", "online")]
	[InlineData("online", "offline")]
	public async Task BuildsFlatDeliveryWithIdenticalManifestAndVerifiedInventoryAsync(string mode, string serviceImaging)
	{
		var manifest = this.CreateManifest(mode);
		if(serviceImaging != null)
			manifest.Components[0]["imaging"] = serviceImaging;
		manifest.Components[0]["tag"] = "8.10.2";
		var runner = new BuildRunner();
		var archive = await this.CreateBuilder(runner).BuildAsync(manifest, CancellationToken.None);

		Assert.Equal("example@1.0-x64.tar.gz", Path.GetFileName(archive));
		Assert.Equal([".settings", "example@1.0-x64.container", "example@1.0-x64.tar.gz"], Directory.GetFiles(manifest["output"]).Select(Path.GetFileName).Order(StringComparer.Ordinal));
		Assert.Empty(Directory.GetDirectories(manifest["output"]));
		Assert.Equal("[redis]\r\ntag=8.10.2\r\n", File.ReadAllText(Path.Combine(manifest["output"], ".settings")));
		Assert.False(Directory.Exists(runner.Workspace));

		var unpacked = Path.Combine(_root, "unpacked");
		Files.Extract(archive, unpacked);
		var readme = File.ReadAllText(Path.Combine(unpacked, "README.md"));
		var readmeZhHans = File.ReadAllText(Path.Combine(unpacked, "README.zh-Hans.md"));
		Assert.Contains("Target:", readme);
		Assert.Contains("目标系统：", readmeZhHans);
		Assert.Contains("Services: redis@8.10.2.", readme);
		Assert.Contains("服务：redis@8.10.2。", readmeZhHans);
		Assert.True(File.Exists(Path.Combine(unpacked, "zh-Hans", "containerizer.resources.dll")));
		Assert.False(File.Exists(Path.Combine(unpacked, "zh-Hans", "zh-Hans", "containerizer.resources.dll")));
		Assert.Equal(File.ReadAllBytes(manifest.ManifestPath), File.ReadAllBytes(Path.Combine(unpacked, Path.GetFileName(manifest.ManifestPath))));

		var recorded = ContainerManifest.Read(manifest.ManifestPath);
		var component = Assert.Single(recorded.Components);

		Assert.Equal(mode, recorded["imaging"]);
		Assert.Equal(serviceImaging, component["imaging"]);
		Assert.False(recorded.Root.ContainsKey("mode"));
		Assert.False(component.Values.ContainsKey("mode"));
		Assert.Equal("8.10.2", component["tag"]);
		Assert.Equal("x64", recorded["architecture"]);
		Assert.Equal("debian@13", recorded["distribution"]);
		Assert.False(component.Values.ContainsKey("platform"));
		Assert.False(component.Values.ContainsKey("architecture"));
		Assert.Equal("docker.io/library/redis", component["repository"]);
		Assert.Equal(_digest, component["digest"]);
		Assert.Equal("2026-09-15T05:30:05Z", component["timestamp"]);
		Assert.Equal("123456", component["size"]);
		Assert.Equal("literal ${NOT_A_VARIABLE} ${NOT_A_VARIABLE}", component["environment!LITERAL"]);

		var planFile = Path.Combine(unpacked, "containerizer.json");
		Assert.Contains($"{Files.Hash(planFile)}  containerizer.json", File.ReadAllLines(Path.Combine(unpacked, "checksums.sha256")));
		var node = Files.Load(planFile, ProtocolJson.Default.DeliveryPlan);
		Assert.Equal(1, node.Schema);
		Assert.Equal("linux/amd64", Assert.Single(node.Services).Image.Platform);
		Assert.Equal(serviceImaging ?? mode, Assert.Single(node.Services).Image.Mode);
		var image = Assert.Single(node.Services).Image;
		Assert.StartsWith("containerizer/" + node.Project + "/redis:", image.Reference, StringComparison.Ordinal);
		using var compose = JsonDocument.Parse(File.ReadAllText(Path.Combine(unpacked, "compose.yaml")));
		Assert.Equal(image.Reference, compose.RootElement.GetProperty("services").GetProperty("redis").GetProperty("image").GetString());
		Assert.DoesNotContain(runner.Calls, arguments => arguments[0] == "tag" && arguments[^1] == image.Reference);
		var exports = runner.Calls.Where(arguments => arguments[0] == "save" || arguments.Take(2).SequenceEqual(["image", "save"])).ToArray();

		if(image.Mode == "offline")
			Assert.Equal(image.Id, Assert.Single(exports)[^1]);
		else
			Assert.Empty(exports);

		Assert.Equal(Files.Hash(manifest.ManifestPath), node.SourceHash);
		Assert.Contains(node.Files, file => file.Path == Path.GetFileName(manifest.ManifestPath) && file.Hash == node.SourceHash);
		Assert.Contains(node.Files, file => file.Path == "README.md");
		Assert.Contains(node.Files, file => file.Path == "README.zh-Hans.md");
		Assert.Contains(node.Files, file => file.Path == "zh-Hans/containerizer.resources.dll");
		Assert.All(node.Files, file =>
		{
			Assert.Equal(file.Hash, Files.Hash(Files.ResolveRelativePath(unpacked, file.Path)));
			Assert.Equal(file.Length, new FileInfo(Files.ResolveRelativePath(unpacked, file.Path)).Length);
		});
		Assert.Equal((serviceImaging ?? mode) == "offline", File.Exists(Path.Combine(unpacked, "images", "redis.tar")));
		Assert.Equal(mode == "offline", File.Exists(Path.Combine(unpacked, "packages", "engine.deb")));

		var defaults = File.ReadAllBytes(Path.Combine(manifest["output"], ".settings"));
		var replay = ManifestFactory.Create(CreateContext(manifest.ManifestPath, "--output:replay"));
		Assert.Equal(component["environment!LITERAL"], Assert.Single(replay.Components)["environment!LITERAL"]);

		var replayArchive = await this.CreateBuilder(new BuildRunner()).BuildAsync(replay, CancellationToken.None);
		Assert.True(File.Exists(replayArchive));
		Assert.False(File.Exists(Path.Combine(replay["output"], ".settings")));
		Assert.Equal(defaults, File.ReadAllBytes(Path.Combine(manifest["output"], ".settings")));
	}

	[Theory]
	[InlineData(false)]
	[InlineData(true)]
	public async Task FailureOrCancellationCleansWorkspaceAndPreservesInputsAsync(bool cancel)
	{
		var manifest = this.CreateManifest("offline");
		Directory.CreateDirectory(manifest["output"]);
		var defaults = Path.Combine(manifest["output"], ".settings");
		File.WriteAllText(defaults, "[redis]\r\ntag=8.4\r\n");
		var runner = new BuildRunner { FailExport = !cancel, CancelExport = cancel };

		if(cancel)
			await Assert.ThrowsAsync<OperationCanceledException>(() => this.CreateBuilder(runner).BuildAsync(manifest, CancellationToken.None));
		else
			await Assert.ThrowsAsync<ContainerizationException>(() => this.CreateBuilder(runner).BuildAsync(manifest, CancellationToken.None));

		Assert.False(Directory.Exists(runner.Workspace));
		Assert.Equal([".settings"], Directory.GetFiles(manifest["output"]).Select(Path.GetFileName));
		Assert.Equal("[redis]\r\ntag=8.4\r\n", File.ReadAllText(defaults));
		Assert.DoesNotContain(runner.Calls, arguments => arguments[0] == "tag" && arguments[^1].StartsWith("containerizer/", StringComparison.Ordinal));
		Assert.True(File.Exists(Path.Combine(_root, "input.container")));
	}

	[Fact]
	public async Task MixedComponentOrderKeepsApplicationAndInfrastructureImagesAssociatedAsync()
	{
		var manifest = this.CreateApplication("podman");
		manifest.Components.Reverse();

		var archive = await this.CreateBuilder(new BuildRunner()).BuildAsync(manifest, TestContext.Current.CancellationToken);
		var directory = Path.Combine(_root, "mixed-order");
		Files.Extract(archive, directory);
		var plan = Files.Load(Path.Combine(directory, DeliveryPlan.FileName), ProtocolJson.Default.DeliveryPlan);
		var completed = ContainerManifest.Read(manifest.ManifestPath);

		Assert.Equal(["redis", "application"], plan.Services.Select(service => service.Id));
		Assert.Equal(_digest, plan.Services[0].Image.Digest);
		Assert.Null(plan.Services[0].Package);
		Assert.Equal("application", plan.Services[1].Package.Name);
		Assert.Equal("images/application.tar", plan.Services[1].Image.Archive);
		Assert.Equal(_digest, completed.Components[0]["digest"]);
		Assert.Null(completed.Components[1]["digest"]);
	}

	[Fact]
	public async Task UnknownMetadataIsOmittedInsteadOfReusingStaleDisplayValuesAsync()
	{
		var manifest = this.CreateManifest("offline");
		manifest.Components[0]["timestamp"] = "2025-01-01T00:00:00Z";
		manifest.Components[0]["size"] = "999";
		await this.CreateBuilder(new BuildRunner { OmitMetadata = true }).BuildAsync(manifest, CancellationToken.None);
		var component = Assert.Single(ContainerManifest.Read(manifest.ManifestPath).Components);

		Assert.Null(component["timestamp"]);
		Assert.Null(component["size"]);
		Assert.Equal(_digest, component["digest"]);
	}

	[Fact]
	public async Task DefaultsPublicationFailureRollsBackOnlyThisDeliveryAsync()
	{
		var manifest = this.CreateManifest("offline");
		Directory.CreateDirectory(Path.Combine(manifest["output"], ".settings"));
		var previous = Path.Combine(manifest["output"], "previous.tar.gz");
		File.WriteAllText(previous, "preserve");
		var runner = new BuildRunner();
		var exception = await Record.ExceptionAsync(() => this.CreateBuilder(runner).BuildAsync(manifest, CancellationToken.None));

		Assert.True(exception is IOException or UnauthorizedAccessException);
		Assert.False(File.Exists(manifest.ManifestPath));
		Assert.False(File.Exists(Path.Combine(manifest["output"], manifest.ReleaseName + ".tar.gz")));
		Assert.Equal("preserve", File.ReadAllText(previous));
		Assert.False(Directory.Exists(runner.Workspace));
	}

	[Fact]
	public async Task ReplayCanUseItsOwnInputPathWithoutRewritingItAsync()
	{
		var manifest = this.CreateManifest("offline");
		await this.CreateBuilder(new BuildRunner()).BuildAsync(manifest, CancellationToken.None);
		File.Delete(Path.Combine(manifest["output"], manifest.ReleaseName + ".tar.gz"));
		var original = File.ReadAllBytes(manifest.ManifestPath);
		var replay = ManifestFactory.Create(CreateContext(manifest.ManifestPath));
		await this.CreateBuilder(new BuildRunner()).BuildAsync(replay, CancellationToken.None);
		Assert.Equal(original, File.ReadAllBytes(manifest.ManifestPath));
	}

	[Fact]
	public async Task ReplayUsesRecordedValuesWithoutTheOriginalBuildVariablesAsync()
	{
		var manifest = this.CreateManifest("offline");
		File.WriteAllText(Path.Combine(_root, "custom.template"), "version=1\nimage=docker.io/library/redis\ncommand=[\"redis-server\"]\nhealth=[\"CMD\",\"redis-cli\",\"ping\"]\n[environment]\nDYNAMIC=${only_at_build}\n[settings note]\nargument=--note\nvariable=only_at_build\ndefault=${missing_default}\n");
		manifest.Components[0]["template"] = "custom.template";
		manifest.Evaluator.SetVariable("only_at_build", "\\${RUNTIME_VARIABLE}");
		await this.CreateBuilder(new BuildRunner()).BuildAsync(manifest, CancellationToken.None);
		var replay = ManifestFactory.Create(CreateContext(manifest.ManifestPath, "--output:replay"));
		Assert.False(replay.Evaluator.TryGetVariable("only_at_build", out _));

		var source = TemplateCatalog.Read(Assert.Single(replay.Components), replay);
		Assert.Equal("${RUNTIME_VARIABLE}", source.Environment["DYNAMIC"]);
		Assert.Equal(["redis-server", "--note", "${RUNTIME_VARIABLE}"], source.Plan.Command);
		await this.CreateBuilder(new BuildRunner()).BuildAsync(replay, CancellationToken.None);
	}

	[Fact]
	public void BootstrapCacheIsOutsideOutputAndIsolatedBySourceAndPlatform()
	{
		var cache = Path.Combine(_root, "cache");
		var first = BuildStorage.GetBootstrapCache(Path.Combine(_root, "one"), "debian@13_x64", cache);
		Assert.NotEqual(first, BuildStorage.GetBootstrapCache(Path.Combine(_root, "two"), "debian@13_x64", cache));
		Assert.NotEqual(first, BuildStorage.GetBootstrapCache(Path.Combine(_root, "one"), "debian@13_arm64", cache));
		Assert.StartsWith(cache, first, StringComparison.Ordinal);
	}

	[Fact]
	public async Task MakeCompletesItsDraftAndFailurePreservesEditedInputAsync()
	{
		var fixture = this.CreateManifest("offline");
		var planning = ManifestFactory.Create(CreateContext(Path.Combine(_root, "input.container"), "--source:" + _root, "--output:delivery"), planning: true);
		var draft = this.CreateBuilder(new BuildRunner()).Plan(planning);
		var original = File.ReadAllBytes(draft);
		var failed = ManifestFactory.Create(CreateContext(draft));
		await Assert.ThrowsAsync<ContainerizationException>(() => this.CreateBuilder(new BuildRunner { FailExport = true }).BuildAsync(failed, CancellationToken.None));
		Assert.Equal(original, File.ReadAllBytes(draft));
		var manifest = ManifestFactory.Create(CreateContext(draft));
		var archive = await this.CreateBuilder(new BuildRunner()).BuildAsync(manifest, CancellationToken.None);
		Assert.Equal("complete", ContainerManifest.Read(draft)["stage"]);
		Assert.False(File.Exists(Path.Combine(manifest["output"], ".settings")));
		var unpacked = Path.Combine(_root, "draft-delivery");
		Files.Extract(archive, unpacked);
		Assert.Equal(File.ReadAllBytes(draft), File.ReadAllBytes(Path.Combine(unpacked, Path.GetFileName(draft))));
	}

	[Theory]
	[InlineData("podman", "offline")]
	[InlineData("podman", "online")]
	[InlineData("docker", "offline")]
	[InlineData("docker", "online")]
	public async Task InfrastructureImagesReuseOneCacheAcrossReleaseVersionsAsync(string engine, string mode)
	{
		var manifest = this.CreateManifest(mode);
		manifest["engine"] = engine;
		var runner = new BuildRunner();
		var first = await this.CreateBuilder(runner).BuildAsync(manifest, CancellationToken.None);
		var replay = ManifestFactory.Create(CreateContext(manifest.ManifestPath, "--version:2.0", "--output:second"));
		var second = await this.CreateBuilder(runner).BuildAsync(replay, CancellationToken.None);

		Assert.NotEqual(first, second);
		Assert.True(File.Exists(first));
		Assert.True(File.Exists(second));
		var cacheTag = "localhost/containerizer/cache/" + Files.HashText("docker.io/library/redis") + ":x64-" + _digest[7..];
		var tags = runner.Calls.Where(arguments => arguments[0] == "tag").Select(arguments => arguments[^1]).ToArray();
		Assert.Equal(2, tags.Count(tag => tag == cacheTag));
		Assert.All(tags, tag => Assert.True(tag == cacheTag || tag == "docker.io/library/redis:latest", tag));
		Assert.DoesNotContain(runner.Calls, arguments => arguments.Contains("prune") || arguments.Contains("--force") || arguments.Contains("-f"));
	}

	private DeliveryBuilder CreateBuilder(BuildRunner runner, Action<string> error = null) => new(runner, Path.Combine(_root, "cache"), Path.Combine(_root, "executor"), output: _ => { }, error: error);

	[Theory]
	[InlineData("podman")]
	[InlineData("docker")]
	public async Task ApplicationImagesAreRemovedAfterPublicationWithoutDeletingSharedImagesAsync(string engine)
	{
		var manifest = this.CreateApplication(engine);
		var runner = new BuildRunner { Published = () => File.Exists(manifest.ManifestPath) && File.Exists(Path.Combine(manifest["output"], manifest.ReleaseName + ".tar.gz")) };
		var archive = await this.CreateBuilder(runner).BuildAsync(manifest, CancellationToken.None);

		Assert.True(runner.FinalRemovedAfterPublication);
		Assert.Single(runner.FinalImages);
		Assert.Empty(runner.Images);
		Assert.Empty(runner.Containers);
		Assert.DoesNotContain(runner.Calls, arguments => arguments.Contains("prune") || arguments.Contains("--force") || arguments.Contains("-f"));
		Assert.All(runner.Calls.Where(arguments => arguments.Length > 1 && arguments[1] == "rm"), arguments => Assert.StartsWith(arguments[0] == "image" ? "localhost/containerizer-build-" : "containerizer-build-", arguments[^1], StringComparison.Ordinal));
		Assert.DoesNotContain(runner.Calls, arguments => arguments[0] == "tag" && arguments[^1].EndsWith("-base", StringComparison.Ordinal));
		Assert.Single(runner.Calls, arguments => (arguments[0] == "build" || arguments.Take(2).SequenceEqual(["buildx", "build"])) && arguments.Contains("final"));
		Assert.All(runner.Calls.Where(arguments => arguments[0] == "create"), arguments => Assert.Contains("/bin/true", arguments));
		Assert.All(runner.Calls.Where(arguments => arguments[0] == "cp"), arguments => Assert.EndsWith(":/etc/passwd", arguments[1], StringComparison.Ordinal));
		Assert.DoesNotContain("collect.sh", runner.Dockerfile);
		Assert.DoesNotContain("containerizer-config", runner.Dockerfile);
		Assert.Contains("COPY --from=installed / /", runner.Dockerfile);

		var unpacked = Path.Combine(_root, "application-delivery");
		Files.Extract(archive, unpacked);
		var node = Files.Load(Path.Combine(unpacked, "containerizer.json"), ProtocolJson.Default.DeliveryPlan);
		Assert.Equal(1, node.Schema);
		var application = Assert.Single(node.Services, service => service.Kind == "application");
		Assert.Empty(application.Mounts);
		Assert.DoesNotContain(node.Files, file => file.Path.StartsWith("config/application/", StringComparison.Ordinal));
		Assert.Contains("Services: application@1.0.0, redis@latest.", File.ReadAllText(Path.Combine(unpacked, "README.md")));
		Assert.Contains("服务：application@1.0.0、redis@latest。", File.ReadAllText(Path.Combine(unpacked, "README.zh-Hans.md")));
		Assert.Equal(BuildRunner.ApplicationId, application.Image.Id);
		Assert.StartsWith("containerizer/" + node.Project + "/application:", application.Image.Reference, StringComparison.Ordinal);
		Assert.DoesNotContain(application.Image.Reference, runner.FinalImages);
		Assert.Equal("test image archive", File.ReadAllText(Files.ResolveRelativePath(unpacked, application.Image.Archive)));
		Assert.Contains(runner.Calls, arguments => arguments.Contains("--output") && arguments[^1] == application.Image.Id);
		Assert.Contains(runner.Calls, arguments => arguments.Take(2).SequenceEqual(["image", "inspect"]) && arguments[^1] == application.Image.Id);
	}

	[Theory]
	[InlineData("podman")]
	[InlineData("docker")]
	public async Task PackagedWebTemplateBecomesProtectedIngressConfigurationAsync(string engine)
	{
		var manifest = this.CreateApplication(engine);
		manifest.Components[0]["dependences"] = "nginx";
		manifest.Components.Add(new() { Name = "nginx" });
		var configuration = "server { listen 80; location / { proxy_pass http://application:8080; } }\n";
		WebFixtures.WritePackage(manifest.Components[0]["package"], "application", "[api]\nbind=http://0.0.0.0:80\n", configuration);
		manifest.Normalize();
		var runner = new BuildRunner();
		var archive = await this.CreateBuilder(runner).BuildAsync(manifest, CancellationToken.None);
		var unpacked = Path.Combine(_root, "ingress-delivery");
		Files.Extract(archive, unpacked);
		var node = Files.Load(Path.Combine(unpacked, "containerizer.json"), ProtocolJson.Default.DeliveryPlan);
		Assert.Empty(node.Services.Single(service => service.Kind == "application").Mounts);
		var mount = Assert.Single(node.Services.Single(service => service.Id == "nginx").Mounts, item => item.Target == "/etc/nginx/containerizer/application.conf");
		Assert.Equal("/etc/nginx/containerizer/application.conf", mount.Target);
		Assert.Equal(configuration.ReplaceLineEndings("\r\n"), File.ReadAllText(Files.ResolveRelativePath(unpacked, mount.Source)));
		Assert.DoesNotContain(runner.Calls, arguments => arguments[0] == "cp" && !arguments[1].EndsWith("/etc/passwd", StringComparison.Ordinal));
		Assert.Empty(runner.Images);
		Assert.Empty(runner.Containers);
	}

	[Theory]
	[InlineData("podman")]
	[InlineData("docker")]
	public async Task FailedContainerCreationCleansThePartiallyCreatedContainerAndImageAsync(string engine)
	{
		var manifest = this.CreateApplication(engine);
		var runner = new BuildRunner { FailCreate = true };
		var exception = await Assert.ThrowsAsync<ContainerizationException>(() => this.CreateBuilder(runner).BuildAsync(manifest, CancellationToken.None));
		Assert.Contains("create failed", exception.Message);
		Assert.Empty(runner.Images);
		Assert.Empty(runner.Containers);
		Assert.False(File.Exists(manifest.ManifestPath));
		Assert.False(Directory.Exists(runner.Workspace));
	}

	[Theory]
	[InlineData("podman")]
	[InlineData("docker")]
	public async Task CancellationDuringApplicationExportStillCleansResourcesAsync(string engine)
	{
		var manifest = this.CreateApplication(engine);
		using var cancellation = new CancellationTokenSource();
		var runner = new BuildRunner { CancelExport = true, Cancellation = cancellation };
		await Assert.ThrowsAsync<OperationCanceledException>(() => this.CreateBuilder(runner).BuildAsync(manifest, cancellation.Token));
		Assert.True(cancellation.IsCancellationRequested);
		Assert.Empty(runner.Images);
		Assert.Empty(runner.Containers);
		Assert.False(runner.CleanupWasCancelled);
		Assert.False(File.Exists(manifest.ManifestPath));
	}

	[Theory]
	[InlineData("podman")]
	[InlineData("docker")]
	public async Task InUseImageCleanupWarnsWithoutInvalidatingThePublishedDeliveryAsync(string engine)
	{
		var manifest = this.CreateApplication(engine);
		var runner = new BuildRunner { FailCleanup = true };
		using var errors = new StringWriter();

		var archive = await this.CreateBuilder(runner, errors.WriteLine).BuildAsync(manifest, CancellationToken.None);
		Assert.True(File.Exists(archive));
		Assert.True(File.Exists(manifest.ManifestPath));
		Assert.Equal("[redis]\r\ntag=latest\r\n", File.ReadAllText(Path.Combine(manifest["output"], ".settings")));
		Assert.Contains(Assert.Single(runner.FinalImages), errors.ToString());
		Assert.Contains("image is in use", errors.ToString());
		Assert.Contains(engine, errors.ToString());
		Assert.Single(runner.Images);
		Assert.Empty(runner.Containers);
		Assert.False(Directory.Exists(runner.Workspace));
	}

	[Theory]
	[InlineData("podman")]
	[InlineData("docker")]
	public async Task PublicationFailureCleansApplicationImagesAndKeepsHistoricalArtifactsAsync(string engine)
	{
		var manifest = this.CreateApplication(engine);
		Directory.CreateDirectory(Path.Combine(manifest["output"], ".settings"));
		var previous = Path.Combine(manifest["output"], "previous.tar.gz");
		File.WriteAllText(previous, "preserve");
		var runner = new BuildRunner();
		var exception = await Record.ExceptionAsync(() => this.CreateBuilder(runner).BuildAsync(manifest, CancellationToken.None));
		Assert.True(exception is IOException or UnauthorizedAccessException);
		Assert.Empty(runner.Images);
		Assert.Empty(runner.Containers);
		Assert.False(File.Exists(manifest.ManifestPath));
		Assert.Equal("preserve", File.ReadAllText(previous));
	}

	[Fact]
	public async Task BootstrapContainerCreationFailureCleansItsBuildResourcesAsync()
	{
		var manifest = this.CreateManifest("offline");
		Directory.Delete(Path.Combine(_root, ".containerizer"), true);
		manifest.Components[0].Values.Remove("dependences");
		var runner = new BuildRunner { FailCreate = true };
		var exception = await Assert.ThrowsAsync<ContainerizationException>(() => this.CreateBuilder(runner).BuildAsync(manifest, CancellationToken.None));
		Assert.Contains("create failed", exception.Message);
		Assert.Empty(runner.Images);
		Assert.Empty(runner.Containers);
	}

	[Fact]
	public async Task ChangedPackageContentsWithTheSameVersionAlwaysRebuildTheApplicationAsync()
	{
		var runner = new BuildRunner();
		var first = this.CreateApplication("podman", "first payload");
		first["output"] = Path.Combine(_root, "first");
		await this.CreateBuilder(runner).BuildAsync(first, TestContext.Current.CancellationToken);
		var second = this.CreateApplication("podman", "revised payload");
		second["output"] = Path.Combine(_root, "second");
		await this.CreateBuilder(runner).BuildAsync(second, TestContext.Current.CancellationToken);

		Assert.Equal(first["version"], second["version"]);
		Assert.Equal(2, runner.PackageHashes.Count);
		Assert.NotEqual(runner.PackageHashes[0], runner.PackageHashes[1]);
		Assert.Equal(Files.Hash(second.Components[0]["package"]), runner.PackageHashes[1]);
		Assert.Equal(2, runner.FinalImages.Count);
		Assert.Single(runner.Calls, arguments => arguments[0] == "build" && !arguments.Contains("final"));
		Assert.Empty(runner.Images);
		Assert.Empty(runner.Containers);
	}

	private ContainerManifest CreateApplication(string engine, string payload = "application payload")
	{
		var manifest = this.CreateManifest("offline");
		manifest["engine"] = engine;
		var path = Path.Combine(_root, "application.tar.gz");

		using(var output = File.Create(path))
		using(var gzip = new GZipStream(output, CompressionLevel.Optimal))
		using(var writer = new TarWriter(gzip))
		{
			writer.WriteEntry(new PaxGlobalExtendedAttributesTarEntry(new Dictionary<string, string> { ["PackageName"] = "application", ["Architecture"] = "x64", ["Version"] = "1.0.0", ["InstallPath"] = "/opt/application" }));
			using var service = new MemoryStream(Encoding.UTF8.GetBytes("[Service]\nWorkingDirectory=/opt/application\nExecStart=/opt/application/application\n"));
			writer.WriteEntry(new PaxTarEntry(TarEntryType.RegularFile, "application.service") { DataStream = service });
			using var contents = new MemoryStream(Encoding.UTF8.GetBytes(payload));
			writer.WriteEntry(new PaxTarEntry(TarEntryType.RegularFile, "application") { DataStream = contents });
		}

		File.WriteAllText(path[..^7] + ".sh", "#!/bin/sh\nexit 0\n");
		manifest.Components.Insert(0, new() { Name = "application", ["package"] = path });
		return manifest;
	}
	private ContainerManifest CreateManifest(string mode)
	{
		var header = new byte[20];
		header[0] = 0x7f;
		header[1] = (byte)'E';
		header[2] = (byte)'L';
		header[3] = (byte)'F';
		header[4] = 2;
		header[5] = 1;
		header[18] = 62;

		File.WriteAllBytes(Path.Combine(_root, "executor"), header);
		var resources = Path.Combine(_root, "zh-Hans", "zh-Hans");
		Directory.CreateDirectory(resources);
		File.WriteAllText(Path.Combine(resources, "containerizer.resources.dll"), "localized executor resources");

		var input = Path.Combine(_root, "input.container");
		File.WriteAllText(input, "name=example\r\nversion=1.0\r\ndistribution=debian\r\nengine=podman\r\nimaging=" + mode + "\r\nbootstrap=" + mode + "\r\n[redis]\r\nenvironment!LITERAL=literal \\${NOT_A_VARIABLE} \\${NOT_A_VARIABLE}\r\n");

		var imported = Path.Combine(_root, ".containerizer", "bootstrap", "debian@13_x64");
		Directory.CreateDirectory(Path.Combine(imported, "packages"));
		var package = Path.Combine(imported, "packages", "engine.deb");
		File.WriteAllText(package, "test package");
		File.WriteAllText(Path.Combine(imported, "packages", "metadata.tsv"), "fixture metadata");

		Files.Save(Path.Combine(imported, "bootstrap.lock.json"), new BootstrapPlan
		{
			Profile = "debian@13_x64",
			EngineVersion = "test",
			ComposeVersion = "test",
			Metadata = "packages/metadata.tsv",
			Packages = [new() { Name = "docker-ce", Version = "test", Architecture = "amd64", Url = "https://example.com/engine.deb", Path = "packages/engine.deb", Hash = Files.Hash(package), Length = new FileInfo(package).Length }],
		}, ProtocolJson.Default.BootstrapPlan);

		var result = ManifestFactory.Create(CreateContext("redis", "--name:example", "--version:1.0", "--distribution:debian", "--engine:podman", "--imaging:" + mode, "--bootstrap:" + mode, "--source:" + _root, "--output:delivery"));
		result.Components[0]["environment!LITERAL"] = "literal \\${NOT_A_VARIABLE} \\${NOT_A_VARIABLE}";
		return result;
	}

	private static CommandContext CreateContext(params string[] arguments) => new(new CommandExecutor(), CommandLine.Parse(Utility.FormatCommand("containerize", arguments.Select(argument => argument.Replace('\\', '/')).ToArray()))[0], new ContainerizeCommand(), null);

	private sealed class BuildRunner : IProcessRunner
	{
		public Task<int> StreamAsync(string executable, IReadOnlyList<string> arguments, string directory, CancellationToken cancellation) => throw new InvalidOperationException("Unexpected streaming process call.");
		public static readonly string ApplicationId = "sha256:" + new string('c', 64);
		public string Workspace { get; private set; }
		public bool FailExport { get; set; }
		public bool CancelExport { get; set; }
		public bool OmitMetadata { get; set; }
		public bool FailCreate { get; set; }
		public bool FailCleanup { get; set; }
		public bool CleanupWasCancelled { get; private set; }
		public bool FinalRemovedAfterPublication { get; private set; }
		public Func<bool> Published { get; set; }
		public CancellationTokenSource Cancellation { get; set; }
		public List<string[]> Calls { get; } = [];
		public List<string> PackageHashes { get; } = [];
		public string Dockerfile { get; private set; }
		public HashSet<string> Images { get; } = [];
		public HashSet<string> FinalImages { get; } = [];
		public HashSet<string> Containers { get; } = [];

		public Task<ProcessResult> RunAsync(string executable, IReadOnlyList<string> arguments, string directory, CancellationToken cancellation, int timeoutSeconds = 900)
		{
			this.Calls.Add(arguments.ToArray());
			if(arguments[0] == "build" || arguments.Take(2).SequenceEqual(["buildx", "build"]))
			{
				this.Dockerfile = File.ReadAllText(Path.Combine(directory, "Dockerfile"));
				if(arguments.Contains("final"))
					this.Workspace = Directory.GetParent(directory).FullName;
				var tag = arguments[Array.IndexOf(arguments.ToArray(), "-t") + 1];
				this.Images.Add(tag);

				if(arguments.Contains("final"))
				{
					this.FinalImages.Add(tag);
					this.PackageHashes.Add(Files.Hash(Path.Combine(directory, "input", "application.tar.gz")));
				}
			}

			if(arguments[0] == "create")
			{
				var name = arguments[Array.IndexOf(arguments.ToArray(), "--name") + 1];
				this.Containers.Add(name);
				return Task.FromResult(this.FailCreate ? new ProcessResult(125, "", "create failed") : new ProcessResult(0, name, ""));
			}

			if(arguments[0] == "export")
				File.WriteAllText(arguments[Array.IndexOf(arguments.ToArray(), "--output") + 1], "public runtime files");

			if(arguments[0] == "cp")
			{
				if(arguments[1].EndsWith("/etc/passwd", StringComparison.Ordinal))
					File.WriteAllText(arguments[^1], "redis:x:999:999::/home/redis:/bin/sh\n");
			}

			if(arguments[0] is "image" or "container")
			{
				if(arguments[1] == "rm")
				{
					this.CleanupWasCancelled |= cancellation.IsCancellationRequested;
					var name = arguments[^1];

					if(arguments[0] == "image" && this.FinalImages.Contains(name))
					{
						this.FinalRemovedAfterPublication = this.Published?.Invoke() == true;
						if(this.FailCleanup)
							return Task.FromResult(new ProcessResult(1, "", "image is in use"));
					}

					var removed = (arguments[0] == "image" ? this.Images : this.Containers).Remove(name);
					return Task.FromResult(new ProcessResult(removed ? 0 : 1, "", removed ? "" : "not found"));
				}

				if(arguments[1] == "ls")
				{
					var name = arguments[^1].Split('=', 2)[1];
					return Task.FromResult(new ProcessResult(0, (arguments[0] == "image" ? this.Images : this.Containers).Contains(name) ? "retained-resource-id" : "", ""));
				}

				if(arguments[1] == "inspect")
				{
					var application = this.FinalImages.Contains(arguments[^1]) || arguments[^1] == ApplicationId;
					var repository = arguments[^1].StartsWith("docker.io/library/debian", StringComparison.Ordinal) ? "docker.io/library/debian" : arguments[^1].StartsWith("docker.io/library/nginx", StringComparison.Ordinal) ? "docker.io/library/nginx" : "docker.io/library/redis";
					return Task.FromResult(new ProcessResult(0, JsonSerializer.Serialize(new[] { new { Id = application ? ApplicationId : "sha256:" + new string('b', 64), Os = "linux", Architecture = "amd64", Digest = _digest, RepoDigests = new[] { repository + "@" + _digest }, Config = new { User = "root" }, Created = this.OmitMetadata ? null : "2026-09-15T13:30:05.4441212+08:00", Size = this.OmitMetadata ? (long?)null : 123456 } }), ""));
				}
			}

			if(arguments[0] == "save" || arguments.Take(2).SequenceEqual(["image", "save"]))
			{
				var path = arguments[Array.IndexOf(arguments.ToArray(), "--output") + 1];
				this.Workspace = Directory.GetParent(Path.GetDirectoryName(path)).Parent.FullName;

				if(this.CancelExport)
				{
					this.Cancellation?.Cancel();
					throw new OperationCanceledException();
				}

				if(this.FailExport)
					return Task.FromResult(new ProcessResult(125, "", "export failed"));

				File.WriteAllText(path, "test image archive");
			}

			return Task.FromResult(new ProcessResult(0, "", ""));
		}
	}
}
