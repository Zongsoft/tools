using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;

using Xunit;

using Zongsoft.Components;

namespace Zongsoft.Tools.Packager.Tests;

public sealed class DependencyTest
{
	[Theory]
	[InlineData("runtime", null, false, null, false)]
	[InlineData("runtime:10.0", "10.0", true, null, false)]
	[InlineData("runtime:[10.0)", "10.0", true, null, false)]
	[InlineData("runtime:[10.0,)", "10.0", true, null, false)]
	[InlineData("runtime:(10.0,)", "10.0", false, null, false)]
	[InlineData("runtime:[10.0]", "10.0", true, "10.0", true)]
	[InlineData("runtime:(,11.0]", null, false, "11.0", true)]
	[InlineData("runtime:(,11.0)", null, false, "11.0", false)]
	[InlineData("runtime:[10.0,11.0]", "10.0", true, "11.0", true)]
	[InlineData("runtime:(10.0,11.0)", "10.0", false, "11.0", false)]
	[InlineData("runtime:[10.0,11.0)", "10.0", true, "11.0", false)]
	[InlineData("runtime:(10.0,11.0]", "10.0", false, "11.0", true)]
	[InlineData("runtime:(,)", null, false, null, false)]
	[InlineData(" runtime : [ 2:10.00~rc1-2+build , 3:11.0-1 ) ", "2:10.00~rc1-2+build", true, "3:11.0-1", false)]
	public void Parse_PreservesRangeBoundariesAndNativeVersions(string expression, string minimum, bool minimumIncluded, string maximum, bool maximumIncluded)
	{
		var dependency = Assert.Single(Assert.Single(Dependency.Parse([expression])));
		Assert.Equal("runtime", dependency.Name);
		Assert.Equal(minimum, dependency.Minimum);
		Assert.Equal(minimumIncluded, dependency.MinimumIncluded);
		Assert.Equal(maximum, dependency.Maximum);
		Assert.Equal(maximumIncluded, dependency.MaximumIncluded);
		Assert.Equal(minimum != null && minimum == maximum && minimumIncluded && maximumIncluded, dependency.IsExact);
	}

	[Theory]
	[InlineData("libc6:any", "libc6:any", null)]
	[InlineData("libc6:amd64:[2:2.36-9]", "libc6:amd64", "2:2.36-9")]
	[InlineData("/bin/sh", "/bin/sh", null)]
	[InlineData("libfoo.so()(64bit)", "libfoo.so()(64bit)", null)]
	[InlineData("pkgconfig(openssl):[3.0)", "pkgconfig(openssl)", "3.0")]
	public void Parse_PreservesNativeDependencyNames(string expression, string name, string minimum)
	{
		var dependency = Assert.Single(Assert.Single(Dependency.Parse([expression])));
		Assert.Equal(name, dependency.Name);
		Assert.Equal(minimum, dependency.Minimum);
	}

	[Fact]
	public void Parse_PreservesConjunctionsAlternativesAndDuplicateConstraints()
	{
		var groups = Dependency.Parse(["runtime:[10.0,11.0) | alternative:[9.0); libssl:[3.0]", "runtime:(,12.0), runtime:(,12.0)"]);
		Assert.Equal(4, groups.Length);
		Assert.Equal(new[] { "runtime", "alternative" }, groups[0].Select(dependency => dependency.Name));
		Assert.Equal("11.0", groups[0][0].Maximum);
		Assert.Equal("3.0", Assert.Single(groups[1]).Minimum);
		Assert.Equal(groups[2], groups[3]);
	}

	[Theory]
	[InlineData(null)]
	[InlineData("")]
	[InlineData(" \t ")]
	[InlineData(" , ; , ")]
	public void Parse_EmptyDependenciesAreOmitted(string expression)
	{
		Assert.Empty(Dependency.Parse([expression]));
		Assert.Empty(new PackageOptions(Utility.CreateEvaluator(new global::Zongsoft.Common.Variables { ["dependencies"] = expression })).Dependencies);
	}

	[Theory]
	[InlineData("runtime >= 10.0")]
	[InlineData("runtime(>= 10.0)")]
	[InlineData("runtime (>= 10.0)")]
	[InlineData("runtime:10.*")]
	[InlineData("runtime:[10.*)")]
	[InlineData("runtime:")]
	[InlineData("runtime:[]")]
	[InlineData("runtime:[)")]
	[InlineData("runtime:(10.0)")]
	[InlineData("runtime:(10.0]")]
	[InlineData("runtime:[,10.0]")]
	[InlineData("runtime:[10.0,]")]
	[InlineData("runtime:[1,2,3)")]
	[InlineData("runtime:[1 0,2)")]
	[InlineData("runtime:[[1,2)]")]
	[InlineData("runtime:[1,2")]
	[InlineData("runtime:1,2)")]
	[InlineData("runtime:[1,2)extra")]
	[InlineData("runtime |")]
	[InlineData("| runtime")]
	[InlineData("runtime || alternative")]
	[InlineData("runtime\nInjected: yes")]
	[InlineData("runtime\rInjected: yes")]
	[InlineData("runtime\0")]
	public void Parse_RejectsMalformedDependencies(string expression)
	{
		Assert.Throws<InvalidDataException>(() => Dependency.Parse([expression]));
	}

	[Theory]
	[InlineData("deb")]
	[InlineData("rpm")]
	public void CommandOptions_ExpandRangesBeforeSplittingAtTopLevel(string format)
	{
		using var directory = new MigrationTestDirectory();
		directory.Write(".env", "minimum=10.0\nmaximum=11.0\ndependencies=runtime:[9.0,10.0)\n");
		var commandLine = CommandLine.Parse(format + " --dependencies:\"runtime:[${minimum},${maximum}) | alternative:[9.0);libssl\"")[0];
		var command = format == "deb" ? (CommandBase<CommandContext>)new DebCommand() : new RpmCommand();
		var context = new CommandContext(new CommandExecutor(), commandLine, command, null);
		var evaluator = PackCommand<Package.Deb>.CreateEvaluator(context, directory.Path);
		var options = new PackageOptions(evaluator);

		Assert.Equal(new[] { "runtime:[10.0,11.0) | alternative:[9.0)", "libssl" }, options.Dependencies);
		var groups = Dependency.Parse(options.Dependencies);
		Assert.Equal(2, groups.Length);
		Assert.Equal("11.0", groups[0][0].Maximum);
	}
}
