var target = Argument("target", "default");
var edition = Argument("edition", "Debug");

var solutionFile = "Zongsoft.Tools.Packager.slnx";

Task("clean")
	.Description("清理解决方案")
	.Does(() =>
{
	CleanDirectories($"**/bin/{edition}");
	CleanDirectories($"**/obj/{edition}");
});

Task("restore")
	.Description("还原项目依赖")
	.Does(() =>
{
	DotNetRestore(solutionFile, new DotNetRestoreSettings
	{
		MSBuildSettings = new DotNetMSBuildSettings().WithProperty("Configuration", edition),
	});
});

Task("build")
	.Description("编译项目")
	.IsDependentOn("clean")
	.IsDependentOn("restore")
	.Does(() =>
{
	var settings = new DotNetBuildSettings
	{
		Configuration = edition,
	};

	DotNetBuild(solutionFile, settings);
	DotNetPack("src/Zongsoft.Tools.Packager.csproj", new DotNetPackSettings
	{
		Configuration = edition,
		NoBuild = true,
		NoRestore = true,
	});
});

Task("test")
	.Description("单元测试")
	.IsDependentOn("restore")
	.Does(() =>
{
	var settings = new DotNetTestSettings
	{
		NoRestore = true,
		Configuration = edition,
	};

	var projects = GetFiles("test/*.csproj");

	foreach(var project in projects)
	{
		DotNetTest(project.FullPath, settings);
	}
});

Task("pack")
	.Description("发包(NuGet)")
	.IsDependentOn("build")
	.Does(() =>
{
	var packages = GetFiles($"**/{edition}/*.nupkg");

	foreach(var package in packages)
	{
		DotNetNuGetPush(package.FullPath, new DotNetNuGetPushSettings
		{
			Source = "nuget.org",
			ApiKey = EnvironmentVariable("NUGET_API_KEY"),
			SkipDuplicate = true,
		});
	}
});

Task("default")
	.Description("默认")
	.IsDependentOn("test");

RunTarget(target);
