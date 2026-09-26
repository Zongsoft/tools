var target = Argument("target", "default");
var edition = Argument("edition", "Debug");

var solutionFile = "Zongsoft.Tools.Regular.slnx";
var guiProject = "src/Zongsoft.Tools.Regular.csproj";
var toolProject = "tool/Zongsoft.Tools.Regular.Tool.csproj";

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
	.Description("编译项目并生成本地 NuGet 工具包")
	.IsDependentOn("clean")
	.IsDependentOn("restore")
	.Does(() =>
{
	DotNetBuild(solutionFile, new DotNetBuildSettings
	{
		Configuration = edition,
		NoRestore = true,
	});

	var version = XmlPeek(toolProject, "/Project/PropertyGroup/Version");
	var guiPublishDir = MakeAbsolute(Directory($"src/bin/{edition}/gui/win-x64")).FullPath;

	DotNetPublish(guiProject, new DotNetPublishSettings
	{
		Configuration = edition,
		Runtime = "win-x64",
		SelfContained = false,
		OutputDirectory = guiPublishDir,
		MSBuildSettings = new DotNetMSBuildSettings().WithProperty("Version", version),
	});

	DotNetPack(toolProject, new DotNetPackSettings
	{
		Configuration = edition,
		MSBuildSettings = new DotNetMSBuildSettings().WithProperty("RegularPublishDir", guiPublishDir),
	});
});

Task("pack")
	.Description("发包(NuGet)")
	.IsDependentOn("build")
	.Does(() =>
{
	var version = XmlPeek(toolProject, "/Project/PropertyGroup/Version");
	var packageDirectory = $"tool/bin/{edition}";
	var package = $"{packageDirectory}/Zongsoft.Tools.Regular.{version}.nupkg";

	if(!FileExists(package))
		throw new Exception($"NuGet package does not exist: {package}");

	var settings = new DotNetNuGetPushSettings
	{
		Source = "nuget.org",
		ApiKey = EnvironmentVariable("NUGET_API_KEY"),
		SkipDuplicate = true,
	};

	DotNetNuGetPush(package, settings);
});

Task("default")
	.Description("默认")
	.IsDependentOn("build");

RunTarget(target);
