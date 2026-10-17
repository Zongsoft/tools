using System;
using System.Linq;
using System.Runtime.InteropServices;

using Xunit;

namespace Zongsoft.Tools.Packager.Tests;

public class BindingExtractorTest
{
	[Theory]
	[InlineData("${this.Url}", 8069, "/")]
	[InlineData("${THIS.Url}", 8069, "/")]
	[InlineData("${this['Url']}", 8069, "/")]
	[InlineData("${this.Network.Address}", 8070, "/")]
	[InlineData("${this['Network/Address']}", 8070, "/")]
	[InlineData("${this['/Network/Address']}", 8071, "/")]
	[InlineData("${this.Settings['urls']}", 9464, "/metrics")]
	[InlineData("${this.Settings[this.Key]}", 9464, "/metrics")]
	public void ThisReadsTheSelectedConfigurationView(string template, int port, string path)
	{
		var package = CreatePackage(template, "${this.Protocols}");
		var document = BindingExtractor.Extract(package);

		var binding = Assert.Single(document.Bindings);
		Assert.Equal("application", binding.Owner);
		Assert.Contains("server", binding.Aliases);

		var endpoint = Assert.Single(binding.Endpoints);
		Assert.Equal("http", endpoint.Scheme);
		Assert.Equal("0.0.0.0", endpoint.Host);
		Assert.Equal(port, endpoint.Port);
		Assert.Equal(path, endpoint.Path);
		Assert.Equal(["http1"], endpoint.Protocols);
	}

	[Fact]
	public void ThisChangesForEachFilteredCandidateAfterImportsFinish()
	{
		var package = new Package.Tar("example", null, new Version(1, 0), Platform.Linux, Architecture.X64);
		package.InstallPath = "/opt/example";
		package.Entries.AddGeneratedContent("config/web.option", """
			<?zongsoft.tools.binding path="Server/*" filter="Enabled:true" template="${this.Url}" protocols="${this.Protocols}" required="true"?>
			<?import path="./shared.xml"?>
			<configuration><option /></configuration>
			""", Utility.Unix.Mode644);
		package.Entries.AddGeneratedContent("config/shared.xml", """
			<configuration>
				<option path="Server">
					<http enabled="true" url="http://*:8069" protocols="Http1" />
					<metrics enabled="true" url="http://*:9464/metrics" protocols="Http1" />
					<disabled enabled="false" url="invalid" />
				</option>
			</configuration>
			""", Utility.Unix.Mode644);

		var document = BindingExtractor.Extract(package);

		Assert.Equal(2, document.Bindings.Length);
		Assert.Equal([8069, 9464], document.Bindings.SelectMany(binding => binding.Endpoints).Select(endpoint => endpoint.Port).Order());
		Assert.All(document.Bindings, binding => Assert.Equal(["http1"], Assert.Single(binding.Endpoints).Protocols));
	}

	[Theory]
	[InlineData("${option.Url}", "Http1")]
	[InlineData("${OPTION.Url}", "Http1")]
	[InlineData("${option['Url']}", "Http1")]
	[InlineData("${this.Url}", "${option.Protocols}")]
	[InlineData("${this.Settings[option.Key]}", "${this.Protocols}")]
	public void OptionIsRejectedInTemplatesProtocolsAndDynamicIndexes(string template, string protocols)
	{
		var package = CreatePackage(template, protocols);

		Assert.ThrowsAny<Exception>(() => BindingExtractor.Extract(package));
	}

	private static Package CreatePackage(string template, string protocols)
	{
		var package = new Package.Tar("example", null, new Version(1, 0), Platform.Linux, Architecture.X64);
		package.InstallPath = "/opt/example";
		package.Entries.AddGeneratedContent("web.option", $$"""
			<?zongsoft.tools.binding path="Server" template="{{template}}" protocols="{{protocols}}"?>
			<configuration>
				<option>
					<network address="http://*:8071" />
					<server url="http://*:8069" protocols="Http1" key="urls" settings="urls=http://*:9464/metrics">
						<network address="http://*:8070" />
					</server>
				</option>
			</configuration>
			""", Utility.Unix.Mode644);
		return package;
	}
}
