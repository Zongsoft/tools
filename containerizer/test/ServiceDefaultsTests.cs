using System;
using System.IO;
using Xunit;
using Zongsoft.Tools.Containerizer.Protocol;

namespace Zongsoft.Tools.Containerizer.Tests;

public sealed class ServiceDefaultsTests : IDisposable
{
	private readonly string _root = Path.Combine(Path.GetTempPath(), "containerizer-defaults-" + Guid.NewGuid().ToString("N"));
	public ServiceDefaultsTests() => Directory.CreateDirectory(_root);
	public void Dispose() => Directory.Delete(_root, true);

	[Fact]
	public void DefaultsUseComponentIdentityAndMergeIndividualExplicitKeys()
	{
		var path = Path.Combine(_root, ".settings");
		File.WriteAllText(path, "[cache]\ntag=8.4\nrepository=quay.io/team/cache\nsettings=password=shared;storage=persistent\n");
		var defaults = ServiceDefaults.Read(path);
		var component = new ContainerManifest.Component { Name = "cache" };
		component["template"] = "redis";
		component["settings"] = "password=";
		defaults.Apply(component);
		Assert.Equal("8.4", component["tag"]);
		Assert.Equal("quay.io/team/cache", component["repository"]);
		Assert.Equal("", component.Settings["password"]);
		Assert.Equal("persistent", component.Settings["storage"]);
		Assert.Equal("latest", defaults.SelectTag(new ContainerManifest.Component { Name = "redis" }));
	}

	[Theory]
	[InlineData("redis=1")]
	[InlineData("[redis]\ntag=bad tag")]
	[InlineData("[redis]\ntag=1\nTag=2")]
	[InlineData("[redis]\nrepository=redis")]
	[InlineData("[redis]\nrepository=docker.io/library/redis:latest")]
	[InlineData("[repositories]\nredis=docker.io/library/redis")]
	[InlineData("[redis]\ntemplate=redis")]
	public void InvalidAndRemovedFormatsAreRejected(string text)
	{
		var path = Path.Combine(_root, ".settings");
		File.WriteAllText(path, text);
		Assert.Contains(path, Assert.Throws<ContainerizationException>(() => ServiceDefaults.Read(path)).Message, StringComparison.Ordinal);
	}

	[Fact]
	public void BackfillPreservesExistingSettingsTagsCommentsAndOrder()
	{
		var original = "# reviewed\r\n[redis]\r\ntag=8.4\r\nsettings=password=${secret}\r\n\r\n[rustfs]\r\n# keep\r\nrepository=docker.io/rustfs/rustfs\r\n";
		var path = Path.Combine(_root, ".settings");
		File.WriteAllText(path, original);
		var manifest = new ContainerManifest();
		manifest["output"] = _root;
		var redis = new ContainerManifest.Component { Name = "redis" };
		redis["tag"] = "9.0";
		var rustfs = new ContainerManifest.Component { Name = "rustfs" };
		rustfs["tag"] = "latest";
		manifest.Components.AddRange([redis, rustfs]);
		Assert.Equal(original.Replace("[rustfs]\r\n", "[rustfs]\r\ntag=latest\r\n", StringComparison.Ordinal), File.ReadAllText(ServiceDefaults.Prepare(manifest, _root)));
		Assert.Equal(original, File.ReadAllText(path));
		var draft = Path.Combine(_root, "draft.container");
		File.WriteAllText(draft, "[redis]\ntag=9.0\n");
		Assert.Null(ServiceDefaults.Prepare(ContainerManifest.Read(draft), _root));
	}
}
