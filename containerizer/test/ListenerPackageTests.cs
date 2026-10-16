using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Collections.Generic;

using Xunit;
using Zongsoft.Tools.Containerizer.Protocol;

namespace Zongsoft.Tools.Containerizer.Tests;

public sealed class ListenerPackageTests : IDisposable
{
	private readonly string _root = Path.Combine(Path.GetTempPath(), "containerizer-listener-" + Guid.NewGuid().ToString("N"));
	public ListenerPackageTests() => Directory.CreateDirectory(_root);
	public void Dispose() => Directory.Delete(_root, true);

	[Theory]
	[InlineData("tar.gz", false)]
	[InlineData("deb", false)]
	[InlineData("rpm", false)]
	[InlineData("tar.gz", true)]
	[InlineData("deb", true)]
	[InlineData("rpm", true)]
	public void ReadsRootJsonAndKeepsTheConfiguredEntrypoint(string format, bool hasBinding)
	{
		var document = new BindingDocument { Bindings = hasBinding ? [BindingDocument.CreateBinding("application", ["web"], [new("http", "0.0.0.0", 8069, "/", ["http1"])])] : [] };
		var path = this.WritePackage(format, Encoding.UTF8.GetString(document.Serialize()));
		var package = PackageReader.Read(path);
		Assert.Equal(hasBinding ? 1 : 0, package.Bindings.Bindings.Length);
		Assert.Equal(document.Serialize(), package.Bindings.Serialize());
		Assert.Contains("/opt/example/.bindings", package.Entries.Keys);
		Assert.DoesNotContain("/opt/example/.web/nginx/.bindings", package.Entries.Keys);
		var source = ApplicationPlanner.Create(new(new PackageReader.Candidate(path, package)), new());

		Assert.Equal(["CMD-SHELL", "kill -0 1"], source.Plan.Health.Test);
		Assert.Equal("http://127.0.0.1:8069", source.Plan.Entrypoint[^1]);
		Assert.Equal(hasBinding ? 1 : 0, source.Plan.Ports.Count);
	}

	[Theory]
	[InlineData("tar.gz")]
	[InlineData("deb")]
	[InlineData("rpm")]
	public void WebReferencesAndStructuredTemplatesAreReadFromEveryPayload(string format)
	{
		var document = WebFixtures.CreateDocument(new WebFixtures.Site("api", ["http://0.0.0.0:80"]));
		var path = WebFixtures.WritePackage(Path.Combine(_root, "example." + format), "example", document, WebFixtures.Template(document));
		var package = PackageReader.Read(path);
		var site = Assert.Single(package.Web.Sites);
		var reference = Assert.Single(site.Bindings);
		var binding = package.Bindings.Bindings.Single(binding => binding.Id == reference.Binding);

		Assert.Equal(document.Serialize(), package.Bindings.Serialize());
		Assert.Equal("nginx", binding.Owner);
		Assert.Equal(binding.Endpoints[0].Id, reference.Endpoint);
		Assert.Equal("tcp", reference.Protocol);
		Assert.Contains("{{zongsoft:binding:", package.Web.Template);
		Assert.DoesNotContain("{{zongsoft:application}}", package.Web.Template);
	}

	[Theory]
	[InlineData("tar.gz")]
	[InlineData("deb")]
	[InlineData("rpm")]
	public void MissingInvalidOversizedAndLegacyBindingsFailDuringPackageReading(string format)
	{
		Assert.Equal(2, Assert.Throws<ContainerizationException>(() => PackageReader.Read(this.WritePackage(format, null))).Code);
		Assert.Throws<InvalidDataException>(() => PackageReader.Read(this.WritePackage(format, "not-json")));
		Assert.Throws<InvalidDataException>(() => PackageReader.Read(this.WritePackage(format, "[application]\nlisten=http://0.0.0.0:8069")));
		Assert.Equal(2, Assert.Throws<ContainerizationException>(() => PackageReader.Read(this.WritePackage(format, new string(' ', BindingDocument.MaximumLength + 1)))).Code);
	}

	private string WritePackage(string format, string bindings)
	{
		var files = new Dictionary<string, string> { ["example.service"] = "[Service]\nWorkingDirectory=/opt/example\nExecStart=dotnet /opt/example/example.dll --urls http://127.0.0.1:8069\n" };
		if(bindings != null)
			files[".bindings"] = bindings;
		return PackageFixtures.Write(Path.Combine(_root, "example." + format), "example", files);
	}
}
