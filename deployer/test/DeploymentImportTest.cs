using System.Security.Cryptography;

using Xunit;

namespace Zongsoft.Tools.Deployer.Tests;

public class DeploymentImportTest
{
	[Fact]
	public async Task Deploy_NamespacedSourceUsesDefaultPathResolverAsync()
	{
		using var fixture = new DeploymentFixture();
		fixture.Write("source/payload.txt", "namespaced-source");
		fixture.Variables["files:source"] = "payload.txt";
		var manifest = fixture.Manifest("${files:source}");
		var deployer = fixture.CreateDeployer();

		var result = await deployer.DeployAsync((manifest).Replace('\\', '/'), (fixture.Destination).Replace('\\', '/'), TestContext.Current.CancellationToken);

		Assert.Equal(0, result.Failures);
		Assert.Equal(1, result.Successes);
		Assert.Single(deployer.Plan.Operations);
		Assert.Equal("namespaced-source", File.ReadAllText(Path.Combine(fixture.Destination, "payload.txt")));
	}

	[Fact]
	public async Task Deploy_ImportsEnterPlanAndCommentsInvalidateLockAsync()
	{
		using var fixture = new DeploymentFixture();
		fixture.Write("source/file.txt", "stable content");
		var child = fixture.Manifest("# original comment\nfile.txt", "source/child.deploy");
		var manifest = fixture.Manifest("#@import child.deploy\n#@import missing.deploy");
		var lockFile = Path.Combine(fixture.Root, "deployment.lock.json");
		fixture.Variables["lockFile"] = (lockFile).Replace('\\', '/');
		var initial = fixture.CreateDeployer();

		var first = await initial.DeployAsync((manifest).Replace('\\', '/'), (fixture.Destination).Replace('\\', '/'), TestContext.Current.CancellationToken);

		Assert.Equal(0, first.Failures);
		Assert.Equal(1, first.Successes);
		Assert.Equal(2, initial.Plan.Manifests.Count);
		Assert.Equal(Hash(manifest), initial.Plan.Manifests[manifest]);
		Assert.Equal(Hash(child), initial.Plan.Manifests[child]);
		var originalLock = File.ReadAllBytes(lockFile);
		var originalOperation = Assert.Single(initial.Plan.Operations);
		fixture.Manifest("# changed comment only\nfile.txt", "source/child.deploy");
		fixture.Variables["locked"] = "true";
		var locked = fixture.CreateDeployer();

		var result = await locked.DeployAsync((manifest).Replace('\\', '/'), (fixture.Destination).Replace('\\', '/'), TestContext.Current.CancellationToken);

		Assert.True(result.Failures > 0);
		Assert.Equal(0, result.Successes);
		Assert.Equal(Hash(manifest), locked.Plan.Manifests[manifest]);
		Assert.NotEqual(initial.Plan.Manifests[child], locked.Plan.Manifests[child]);
		Assert.Equal(originalOperation.Source, Assert.Single(locked.Plan.Operations).Source);
		Assert.Equal(originalOperation.Destination, Assert.Single(locked.Plan.Operations).Destination);
		Assert.Equal("stable content", File.ReadAllText(Path.Combine(fixture.Destination, "file.txt")));
		Assert.Equal(originalLock, File.ReadAllBytes(lockFile));
	}

	private static string Hash(string path)
	{
		using var stream = File.OpenRead(path);
		return Convert.ToHexString(SHA256.HashData(stream));
	}
}
