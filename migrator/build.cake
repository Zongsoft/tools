var target = Argument("target", "default");
var edition = Argument("edition", "Debug");

var solutionFile = "Zongsoft.Tools.Migrator.slnx";

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

Task("executor")
	.Description("发布 Linux 双架构和 Windows x64 原生升迁运行器")
	.Does(() =>
{
	const string container = "zongsoft-migrator-aot-builder";
	if(StartProcess("podman", $"container exists {container}") != 0)
	{
		//使用相对路径，避免 Windows 盘符被 Podman 识别为 URL 协议。
		var arguments = new ProcessArgumentBuilder().Append("kube play")
			.AppendQuoted("executor/build/migrator.linux-x64.yaml");
		if(StartProcess("podman", new ProcessSettings { Arguments = arguments }) != 0)
			throw new Exception("Unable to create the migrator Native AOT build environment.");
	}
	else if(StartProcess("podman", $"start {container}") != 0)
		throw new Exception("Unable to start the migrator Native AOT build environment.");

	if(StartProcess("podman", $"exec {container} bash executor/build/setup.sh") != 0)
		throw new Exception("Unable to prepare the migrator Native AOT toolchain.");

	foreach(var runtime in new[] { "linux-x64", "linux-arm64" })
	{
		var arguments = new ProcessArgumentBuilder().Append($"exec {container} bash executor/build/publish.sh")
			.Append(runtime).AppendQuoted(edition);
		if(StartProcess("podman", new ProcessSettings { Arguments = arguments }) != 0)
			throw new Exception($"Native AOT publishing failed for {runtime}.");
	}
	if(IsRunningOnWindows())
	{
		var arguments = new ProcessArgumentBuilder().Append("-NoProfile -File")
			.AppendQuoted("executor/build/publish.ps1").Append("-Configuration").AppendQuoted(edition);
		if(StartProcess("pwsh", new ProcessSettings { Arguments = arguments }) != 0)
			throw new Exception("Native AOT publishing failed for win-x64.");
	}

	foreach(var runtime in new[] { "linux-x64", "linux-arm64", "win-x64" })
	{
		var executable = runtime == "win-x64" ? "Zongsoft.Tools.Migrator.Executor.exe" : "Zongsoft.Tools.Migrator.Executor";
		if(!FileExists($"executor/src/bin/{edition}/net10.0/{runtime}/publish/{executable}"))
			throw new Exception($"Prepare the complete Native AOT publication for {runtime} before packaging the tool.");
	}
});

Task("compile")
	.Description("编译并制作已备齐运行器的工具包")
	.IsDependentOn("restore")
	.Does(() =>
{
	foreach(var runtime in new[] { "linux-x64", "linux-arm64", "win-x64" })
	{
		var executable = runtime == "win-x64" ? "Zongsoft.Tools.Migrator.Executor.exe" : "Zongsoft.Tools.Migrator.Executor";
		if(!FileExists($"executor/src/bin/{edition}/net10.0/{runtime}/publish/{executable}"))
			throw new Exception($"Prepare the complete Native AOT publication for {runtime} before packaging the tool.");
	}

	var settings = new DotNetBuildSettings
	{
		Configuration = edition,
	};

	DotNetBuild(solutionFile, settings);
	DotNetPack("src/Zongsoft.Tools.Migrator.csproj", new DotNetPackSettings
	{
		Configuration = edition,
		NoBuild = true,
		NoRestore = true,
	});
});

Task("build")
	.IsDependentOn("clean")
	.IsDependentOn("executor")
	.IsDependentOn("compile");

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

	var projects = GetFiles("test/*.csproj").Concat(GetFiles("executor/test/*.csproj"));

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
	var version = XmlPeek("src/Zongsoft.Tools.Migrator.csproj", "/Project/PropertyGroup/Version");
	var package = $"src/bin/{edition}/Zongsoft.Tools.Migrator.{version}.nupkg";
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
