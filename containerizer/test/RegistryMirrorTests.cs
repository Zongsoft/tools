using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Collections.Generic;

using Xunit;
using Zongsoft.Tools.Containerizer.Protocol;

namespace Zongsoft.Tools.Containerizer.Tests;

public sealed class RegistryMirrorTests : IDisposable
{
	private readonly string _root = Path.Combine(Path.GetTempPath(), $"containerizer-mirrors-test-{Guid.NewGuid():N}");

	public RegistryMirrorTests() => Directory.CreateDirectory(_root);
	public void Dispose() => Directory.Delete(_root, true);

	[Fact]
	public void ProfilePreservesOrderedMirrorsAndPrefixes()
	{
		File.WriteAllText(Path.Combine(_root, ".mirrors"), "# Sources\r\ndocker.io=first.example.com; second.example.com/docker.io\r\nmcr.microsoft.com=mirror.example.com/mcr\r\n");
		var mirrors = RegistryMirrorSettings.Read(_root);

		Assert.Equal(["first.example.com/library/redis", "second.example.com/docker.io/library/redis", "docker.io/library/redis"], mirrors.Repositories("docker.io/library/redis"));
		Assert.Equal(["mirror.example.com/mcr/mssql/server", "mcr.microsoft.com/mssql/server"], mirrors.Repositories("mcr.microsoft.com/mssql/server"));
		Assert.Equal(["quay.io/coreos/etcd"], mirrors.Repositories("quay.io/coreos/etcd"));
	}

	[Fact]
	public void MissingOrEmptyFileDoesNotInstallDefaultMirrors()
	{
		Assert.True(RegistryMirrorSettings.Read(_root).IsEmpty);
		File.WriteAllText(Path.Combine(_root, ".mirrors"), "# No configured mirrors\r\n");
		Assert.True(RegistryMirrorSettings.Read(_root).IsEmpty);
	}

	[Theory]
	[InlineData("[docker.io]\r\nlocation=mirror.example.com")]
	[InlineData("docker.io=first.example.com\r\ndocker.io=second.example.com")]
	[InlineData("docker.io=https://mirror.example.com")]
	[InlineData("docker.io=user:password@mirror.example.com")]
	[InlineData("docker.io=mirror.example.com/redis:latest")]
	[InlineData("docker.io=mirror.example.com/")]
	[InlineData("docker.io=mirror.example.com; ")]
	[InlineData("docker.io=")]
	[InlineData("docker.io/library=mirror.example.com")]
	[InlineData("redis=mirror.example.com")]
	[InlineData("docker.io=localhost:65536")]
	public void InvalidRulesFailBeforeEngineAccess(string text)
	{
		File.WriteAllText(Path.Combine(_root, ".mirrors"), $"{text}\r\n");
		Assert.Equal(2, Assert.Throws<ContainerizationException>(() => RegistryMirrorSettings.Read(_root)).Code);
	}

	[Fact]
	public async Task SourceFallbackReportsEveryFailureAndDoesNotRepeatDuplicates()
	{
		var mirrors = new RegistryMirrors { Registries = new() { ["docker.io"] = ["one.example.com", "one.example.com", "two.example.com", "docker.io"] } };
		var calls = new List<string>();
		var reports = new List<string>();

		var error = await Assert.ThrowsAsync<ContainerizationException>(() => mirrors.ExecuteAsync<bool>("docker.io/library/redis", repository =>
		{
			calls.Add(repository);
			throw new ContainerizationException(4, "connection refused");
		}, CancellationToken.None, (repository, _) => reports.Add(repository)));

		Assert.Equal(["one.example.com/library/redis", "two.example.com/library/redis", "docker.io/library/redis"], calls);
		Assert.Equal(calls, reports);
		Assert.All(calls, repository => Assert.Contains(repository, error.Message));
		Assert.Contains("connection refused", error.Message);
	}

	[Fact]
	public async Task CancellationNeverContinuesToAnotherSource()
	{
		using var cancellation = new CancellationTokenSource();
		var mirrors = new RegistryMirrors { Registries = new() { ["docker.io"] = ["mirror.example.com"] } };
		var calls = 0;

		await Assert.ThrowsAsync<OperationCanceledException>(() => mirrors.ExecuteAsync<bool>("docker.io/library/redis", _ =>
		{
			calls++;
			cancellation.Cancel();
			throw new OperationCanceledException(cancellation.Token);
		}, cancellation.Token));
		Assert.Equal(1, calls);
	}
}
