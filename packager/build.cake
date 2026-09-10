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
	DotNetRestore(solutionFile);
});

Task("migrator")
	.Description("在独立 Rocky Linux 9 环境中发布两个架构的原生升迁运行器")
	.Does(() =>
{
	const string container = "zongsoft-packager-aot-builder";
	if(StartProcess("podman", $"container exists {container}") != 0)
	{
		var arguments = new ProcessArgumentBuilder().Append("kube play")
			.AppendQuoted(MakeAbsolute(File("migrator/build/packager.linux-x64.yaml")).FullPath);
		if(StartProcess("podman", new ProcessSettings { Arguments = arguments }) != 0)
			throw new Exception("Unable to create the migrator Native AOT build environment.");
	}
	else if(StartProcess("podman", $"start {container}") != 0)
		throw new Exception("Unable to start the migrator Native AOT build environment.");

	if(StartProcess("podman", $"exec {container} bash migrator/build/setup.sh") != 0)
		throw new Exception("Unable to prepare the migrator Native AOT toolchain.");

	CleanDirectory(Directory("src/.migrator"));
	foreach(var runtime in new[] { "linux-x64", "linux-arm64" })
	{
		var arguments = new ProcessArgumentBuilder().Append($"exec {container} bash migrator/build/publish.sh")
			.Append(runtime).AppendQuoted(edition);
		if(StartProcess("podman", new ProcessSettings { Arguments = arguments }) != 0)
			throw new Exception($"Native AOT publishing failed for {runtime}.");
	}
});

Task("build")
	.Description("编译项目")
	.IsDependentOn("clean")
	.IsDependentOn("restore")
	.IsDependentOn("migrator")
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

	var projects = GetFiles("**/test/*.csproj");

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
