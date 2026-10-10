using System.Xml.Linq;

using Xunit;

[assembly: CollectionBehavior(DisableTestParallelization = true)]

namespace Zongsoft.Tools.Deployer.Tests;

internal sealed class DeploymentFixture : IDisposable
{
	#region 构造函数
	public DeploymentFixture()
	{
		this.Root = Path.Combine(Path.GetTempPath(), "zongsoft-deployer-tests", Guid.NewGuid().ToString("N"));
		Directory.CreateDirectory(this.Root);
		Directory.CreateDirectory(this.Destination);
		Directory.CreateDirectory(this.Packages);
		Directory.CreateDirectory(Path.Combine(this.Root, "feed"));
		this.Variables = new()
		{
			["Framework"] = "net10.0",
			["NuGet_Packages"] = this.Packages.Replace('\\', '/'),
			["NuGet_Server"] = Path.Combine(this.Root, "feed").Replace('\\', '/'),
			["verbosity"] = "quiet",
			["offline"] = "true",
		};
		this.Evaluator = new(new() { Recursive = true });
		this.Evaluator.Providers.Add(this.Variables);
	}
	#endregion

	#region 属性定义
	public string Root { get; }
	public string Destination => Path.Combine(this.Root, "target");
	public string Packages => Path.Combine(this.Root, "packages");
	public global::Zongsoft.Common.Variables Variables { get; }
	public Zongsoft.Text.Templating.TemplateEvaluator Evaluator { get; }
	public StringWriter Log { get; } = new();
	#endregion

	#region 辅助方法
	public Deployer CreateDeployer() => new(this.Evaluator, this.Log);

	public string Write(string relativePath, string content)
	{
		var path = Path.GetFullPath(Path.Combine(this.Root, relativePath));
		if(!path.StartsWith(this.Root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
			throw new InvalidOperationException("Fixture path escapes its temporary root.");
		Directory.CreateDirectory(Path.GetDirectoryName(path));
		File.WriteAllText(path, content);
		return path;
	}

	public string Manifest(string content, string name = "source/.deploy") => this.Write(name, content.Replace("\r\n", "\n").Replace("\n", "\r\n"));

	public string Package(string id, string version = "1.0.0", string framework = "net10.0", params (string Id, string Range)[] dependencies)
	{
		var relative = $"packages/{id.ToLowerInvariant()}/{version.ToLowerInvariant()}";
		var document = new XDocument(new XElement("package", new XElement("metadata",
			new XElement("id", id), new XElement("version", version), new XElement("authors", "Fixture"), new XElement("description", "Isolated regression fixture"),
			new XElement("dependencies", new XElement("group", new XAttribute("targetFramework", framework),
				dependencies.Select(dependency => new XElement("dependency", new XAttribute("id", dependency.Id), new XAttribute("version", dependency.Range))))))));
		this.Write($"{relative}/{id.ToLowerInvariant()}.nuspec", document.ToString());
		this.Write($"{relative}/lib/{framework}/{id}.dll", $"{id}@{version}");
		return Path.Combine(this.Root, relative.Replace('/', Path.DirectorySeparatorChar));
	}
	#endregion

	#region 释放资源
	public void Dispose()
	{
		var path = Path.GetFullPath(this.Root);
		var parent = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "zongsoft-deployer-tests")) + Path.DirectorySeparatorChar;
		if(!path.StartsWith(parent, StringComparison.OrdinalIgnoreCase))
			throw new InvalidOperationException("Refusing to remove a non-fixture directory.");
		Directory.Delete(path, true);
		this.Log.Dispose();
	}
	#endregion
}
