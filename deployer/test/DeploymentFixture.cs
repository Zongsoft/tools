using System.Xml.Linq;
using Xunit;

[assembly: CollectionBehavior(DisableTestParallelization = true)]

namespace Zongsoft.Tools.Deployer.Tests;

internal sealed class DeploymentFixture : IDisposable
{
	public DeploymentFixture()
	{
		Root = Path.Combine(Path.GetTempPath(), "zongsoft-deployer-tests", Guid.NewGuid().ToString("N"));
		Directory.CreateDirectory(Root);
		Directory.CreateDirectory(Destination);
		Directory.CreateDirectory(Packages);
		Directory.CreateDirectory(Path.Combine(Root, "feed"));
		Variables = new(StringComparer.OrdinalIgnoreCase)
		{
			["Framework"] = "net10.0",
			["NuGet_Packages"] = Packages,
			["NuGet_Server"] = Path.Combine(Root, "feed"),
			["verbosity"] = "quiet",
			["offline"] = "true",
		};
	}

	public string Root { get; }
	public string Destination => Path.Combine(Root, "target");
	public string Packages => Path.Combine(Root, "packages");
	public Dictionary<string, string> Variables { get; }
	public StringWriter Log { get; } = new();
	public Deployer CreateDeployer() => new(Variables, Log);
	public string Write(string relativePath, string content)
	{
		var path = Path.GetFullPath(Path.Combine(Root, relativePath));
		if(!path.StartsWith(Root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
			throw new InvalidOperationException("Fixture path escapes its temporary root.");
		Directory.CreateDirectory(Path.GetDirectoryName(path));
		File.WriteAllText(path, content);
		return path;
	}
	public string Manifest(string content, string name = "source/.deploy") => Write(name, content.Replace("\r\n", "\n").Replace("\n", "\r\n"));
	public string Package(string id, string version = "1.0.0", string framework = "net10.0", params (string Id, string Range)[] dependencies)
	{
		var relative = $"packages/{id.ToLowerInvariant()}/{version.ToLowerInvariant()}";
		var document = new XDocument(new XElement("package", new XElement("metadata",
			new XElement("id", id), new XElement("version", version), new XElement("authors", "Fixture"), new XElement("description", "Isolated regression fixture"),
			new XElement("dependencies", new XElement("group", new XAttribute("targetFramework", framework),
				dependencies.Select(dependency => new XElement("dependency", new XAttribute("id", dependency.Id), new XAttribute("version", dependency.Range))))))));
		Write($"{relative}/{id.ToLowerInvariant()}.nuspec", document.ToString());
		Write($"{relative}/lib/{framework}/{id}.dll", $"{id}@{version}");
		return Path.Combine(Root, relative.Replace('/', Path.DirectorySeparatorChar));
	}
	public void Dispose()
	{
		var path = Path.GetFullPath(Root);
		var parent = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "zongsoft-deployer-tests")) + Path.DirectorySeparatorChar;
		if(!path.StartsWith(parent, StringComparison.OrdinalIgnoreCase))
			throw new InvalidOperationException("Refusing to remove a non-fixture directory.");
		Directory.Delete(path, true);
		Log.Dispose();
	}
}
