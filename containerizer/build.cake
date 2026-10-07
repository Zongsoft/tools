var target = Argument("target", "default");
var edition = Argument("edition", "Debug");
const string solution = "Containerizer.slnx";

Task("restore").Does(() => DotNetRestore(solution, new DotNetRestoreSettings
{
	MSBuildSettings = new DotNetMSBuildSettings().WithProperty("Configuration", edition),
}));

Task("test").IsDependentOn("restore").Does(() =>
{
	foreach(var project in GetFiles("test/*.csproj").Concat(GetFiles("executor/test/*.csproj")))
		DotNetTest(project.FullPath, new DotNetTestSettings { Configuration = edition, NoRestore = true });
});

Task("executor").Does(() =>
{
	const string container = "zongsoft-containerizer-aot-builder";
	if(StartProcess("podman", $"container exists {container}") != 0)
	{
		if(StartProcess("podman", "kube play executor/build/containerizer.linux-x64.yaml") != 0)
			throw new Exception("Unable to create the dedicated Native AOT build environment.");
	}
	else if(StartProcess("podman", $"start {container}") != 0)
		throw new Exception("Unable to start the Native AOT build environment.");

	if(StartProcess("podman", $"exec {container} bash executor/build/setup.sh") != 0)
		throw new Exception("Unable to prepare the Native AOT toolchain.");

	foreach(var runtime in new[] { "linux-x64", "linux-arm64" })
	{
		if(StartProcess("podman", $"exec {container} bash executor/build/publish.sh {runtime} {edition}") != 0)
			throw new Exception($"Native AOT publication failed: {runtime}.");
	}
});

Task("compile").IsDependentOn("restore").Does(() =>
{
	foreach(var runtime in new[] { "linux-x64", "linux-arm64" })
	{
		if(!FileExists($"executor/src/bin/{edition}/net10.0/{runtime}/publish/containerizer"))
			throw new Exception($"Publish {runtime} before creating the tool package.");
	}

	DotNetBuild(solution, new DotNetBuildSettings { Configuration = edition });
	DotNetPack("src/Zongsoft.Tools.Containerizer.csproj", new DotNetPackSettings { Configuration = edition, NoBuild = true, NoRestore = true });
});

Task("build").IsDependentOn("executor").IsDependentOn("compile");
Task("pack").IsDependentOn("build").Does(() =>
{
	var version = XmlPeek("src/Zongsoft.Tools.Containerizer.csproj", "/Project/PropertyGroup/Version");
	DotNetNuGetPush($"src/bin/{edition}/Zongsoft.Tools.Containerizer.{version}.nupkg", new DotNetNuGetPushSettings
	{
		Source = "nuget.org", ApiKey = EnvironmentVariable("NUGET_API_KEY"), SkipDuplicate = true,
	});
});

Task("default").IsDependentOn("test");
RunTarget(target);
