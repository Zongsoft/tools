using System;
using System.Linq;
using System.Collections.Generic;

using Zongsoft.Tools.Packager.Web;

using Xunit;

namespace Zongsoft.Tools.Packager.Tests.Web;

public class DefinitionLoadingTest
{
	[Fact]
	public void ImportCallbacksRetainOverwrittenInvalidLocalDeclarations()
	{
		using var files = new MigrationTestDirectory();
		files.Write("shared.profile", "[api]\nserver=http://good");
		var path = files.Write("web.profile", "[api]\nunknown=$(unused)\n#@import shared.profile");
		var error = Assert.Throws<DefinitionException>(() => Definition.Load(path));
		Assert.Equal("Field", error.Diagnostic.Code);
		Assert.Equal(path, error.Diagnostic.Source.File);
		Assert.Equal(2, error.Diagnostic.Source.Line);
	}

	[Theory]
	[InlineData("server!a=$(missing)", "server=http://local", "http://local:80")]
	[InlineData("server=$(missing)", "server!b=http://local", "http://local:80")]
	[InlineData("server!a=$(missing)\nserver!b=invalid weight=0", "server!c=http://local", "http://local:80")]
	public void ImportReplacesTheEntireServerGroup(string shared, string local, string expected)
	{
		using var files = new MigrationTestDirectory();
		files.Write("shared.profile", "[api]\nbind!legacy=http://*\n" + shared);
		var path = files.Write("web.profile", "#@import shared.profile\n[api]\n" + local);
		var model = Resolve(path);
		var route = Assert.Single(Assert.Single(model.Sites).Routes);
		Assert.Equal(expected, Assert.Single(route.Server.Members).Endpoint.Address);
	}

	[Fact]
	public void NestedRepeatedAndDiamondImportsPreserveSequenceAndLocalPoolIdentity()
	{
		using var files = new MigrationTestDirectory();
		files.Write("common.profile", "[api]\nbind!legacy=http://*\nserver!common=http://common\n[api z]\npath=/z\n[api a]\npath=/a");
		files.Write("left.profile", "#@import common.profile\n[api]\nserver!left=http://left");
		files.Write("right.profile", "#@import common.profile\n[api]\nserver!right=http://right");
		var path = files.Write("web.profile", "[api]\nserver!before=http://before\n#@import left.profile|right.profile|left.profile\n[api]\nserver!after=http://after");
		var model = Resolve(path);
		var site = Assert.Single(model.Sites);
		Assert.Equal(["api z", "api a"], site.Routes.Select(item => item.Name));
		Assert.All(site.Routes, route => Assert.Equal(["before", "after"], route.Server.Members.Select(item => item.Name)));
	}

	[Theory]
	[InlineData("server=http://one\nserver!two=http://two", "MixedServers")]
	[InlineData("host=api.example.com", "Scope")]
	[InlineData("bind!legacy=http://*", "Scope")]
	[InlineData("ngnix:listen=80", "Hoster")]
	public void StructuralErrorsAreNotIgnored(string text, string code)
	{
		using var files = new MigrationTestDirectory();
		var error = Assert.Throws<DefinitionException>(() => Definition.Load(files.Write("web.profile", text)));
		Assert.Equal(code, error.Diagnostic.Code);
	}

	[Fact]
	public void MissingRecursiveImportFailsWithoutChangingInputs()
	{
		using var files = new MigrationTestDirectory();
		var content = "#@import missing.profile\n[api]\nbind!legacy=http://*";
		var path = files.Write("web.profile", content);
		var error = Assert.Throws<DefinitionException>(() => Definition.Load(path));
		Assert.Equal("Load", error.Diagnostic.Code);
		Assert.NotNull(error.InnerException);
		Assert.Equal(content.Replace("\n", "\r\n"), System.IO.File.ReadAllText(path));
	}

	internal static Definition.Model Resolve(string path, Dictionary<string, string> variables = null, string application = null) =>
		Definition.Load(path).Resolve(new("example", "/opt/example", variables ?? [], application), new("nginx"));
}
