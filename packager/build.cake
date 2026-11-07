var target = Argument("target", "default");
var compilation = Argument("compilation", "Debug");

var solutionFile = "Zongsoft.Tools.Packager.slnx";

Task("clean")
	.Description("清理解决方案")
	.Does(() =>
{
	CleanDirectories($"**/bin/{compilation}");
	CleanDirectories($"**/obj/{compilation}");
});

Task("restore")
	.Description("还原项目依赖")
	.Does(() =>
{
	DotNetRestore(solutionFile, new DotNetRestoreSettings
	{
		MSBuildSettings = new DotNetMSBuildSettings().WithProperty("Configuration", compilation),
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
		Configuration = compilation,
	};

	DotNetBuild(solutionFile, settings);
	DotNetPack("src/Zongsoft.Tools.Packager.csproj", new DotNetPackSettings
	{
		Configuration = compilation,
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
		Configuration = compilation,
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
	var version = XmlPeek("src/Zongsoft.Tools.Packager.csproj", "/Project/PropertyGroup/Version");
	var package = $"src/bin/{compilation}/Zongsoft.Tools.Packager.{version}.nupkg";
	if(!FileExists(package))
		throw new Exception($"NuGet package does not exist: {package}");

	DotNetNuGetPush(package, new DotNetNuGetPushSettings
	{
		Source = "nuget.org",
		ApiKey = EnvironmentVariable("NUGET_API_KEY"),
		SkipDuplicate = true,
	});
});

Task("default")
	.Description("默认")
	.IsDependentOn("test");

RunTarget(target);
