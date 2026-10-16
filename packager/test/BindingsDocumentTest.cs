using System;
using System.IO;
using System.Linq;
using System.Text;

using Xunit;

namespace Zongsoft.Tools.Packager.Tests;

public class BindingsDocumentTest
{
	[Fact]
	public void EmptyDocumentHasAStableVersionedRepresentation()
	{
		var document = new BindingDocument();
		var content = document.Serialize();

		Assert.Equal("{\"version\":1,\"bindings\":[],\"sites\":[]}", Encoding.UTF8.GetString(content));
		Assert.Equal(content, BindingDocument.Deserialize(content).Serialize());
	}

	[Fact]
	public void SerializerOrdersBindingsAndTheirAliasesDeterministically()
	{
		var document = new BindingDocument
		{
			Bindings =
			[
				BindingDocument.CreateBinding("application", ["z", "a"], [new("tcp", "127.0.0.1", 7000, "/", [])]),
				BindingDocument.CreateBinding("application", [], [new("tcp", "127.0.0.1", 6000, "/", [])]),
			],
		};

		var content = Encoding.UTF8.GetString(document.Serialize());

		var first = document.Bindings.OrderBy(item => item.Id, StringComparer.Ordinal).First().Id;
		var second = document.Bindings.OrderBy(item => item.Id, StringComparer.Ordinal).Last().Id;
		Assert.True(content.IndexOf(first, StringComparison.Ordinal) < content.IndexOf(second, StringComparison.Ordinal));
		Assert.True(content.IndexOf("\"aliases\":[\"a\",\"z\"]", StringComparison.Ordinal) >= 0);
	}

	[Theory]
	[InlineData("{\"version\":2,\"bindings\":[],\"sites\":[]}")]
	[InlineData("{\"version\":1,\"bindings\":[]}")]
	[InlineData("{\"version\":1,\"bindings\":[],\"sites\":[],\"legacyListen\":\"http://localhost:80\"}")]
	[InlineData("{\"version\":1,\"version\":1,\"bindings\":[],\"sites\":[]}")]
	[InlineData("{\"version\":1,\"bindings\":[{\"id\":\"x\"}],\"sites\":[]}")]
	public void DeserializerRejectsUnsupportedVersionsUnknownFieldsAndDuplicateProperties(string json)
	{
		Assert.Throws<InvalidDataException>(() => BindingDocument.Deserialize(Encoding.UTF8.GetBytes(json)));
	}

	[Fact]
	public void DeserializerRejectsOversizedDocumentsBeforeParsing()
	{
		var content = new byte[1024 * 1024 + 1];

		Assert.Throws<InvalidDataException>(() => BindingDocument.Deserialize(content));
	}

	[Fact]
	public void SerializerRejectsDocumentsThatExceedTheSharedMetadataLimit()
	{
		var document = new BindingDocument
		{
			Bindings = [BindingDocument.CreateBinding("application", [new string('a', 1024 * 1024)], [new("tcp", "localhost", 7000, "/", [])])],
		};

		Assert.Throws<InvalidDataException>(() => document.Serialize());
	}

	[Fact]
	public void SerializerRejectsUnsupportedSchemesAndDanglingSiteReferences()
	{
		var danglingSite = new BindingDocument
		{
			Sites = [new("site:site", "site", [], [new(new string('a', 64), new string('b', 64), false)], [])],
		};

		Assert.Throws<InvalidDataException>(() => BindingDocument.CreateBinding("application", [], [new("sctp", "localhost", 6000, "/", [])]));
		Assert.Throws<InvalidDataException>(() => danglingSite.Serialize());
	}
}
