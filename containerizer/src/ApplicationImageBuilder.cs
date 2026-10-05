/*
 *   _____                                ______
 *  /_   /  ____  ____  ____  _________  / __/ /_
 *    / /  / __ \/ __ \/ __ \/ ___/ __ \/ /_/ __/
 *   / /__/ /_/ / / / /_/ /\_ \/ /_/ / __/ /_
 *  /____/\____/_/ /_/\__  /____/\____/_/  \__/
 *                   /____/
 *
 * Authors:
 *   钟峰(Popeye Zhong) <zongsoft@gmail.com>
 *
 * The MIT License (MIT)
 *
 * Copyright (C) 2026 Zongsoft Corporation <http://www.zongsoft.com>
 *
 * Permission is hereby granted, free of charge, to any person obtaining a copy
 * of this software and associated documentation files (the "Software"), to deal
 * in the Software without restriction, including without limitation the rights
 * to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
 * copies of the Software, and to permit persons to whom the Software is
 * furnished to do so, subject to the following conditions:
 *
 * The above copyright notice and this permission notice shall be included in all
 * copies or substantial portions of the Software.
 *
 * THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
 * IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
 * FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
 * AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
 * LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
 * OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE SOFTWARE.
 */

using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Text.RegularExpressions;

using Zongsoft.Tools.Containerizer.Protocol;

namespace Zongsoft.Tools.Containerizer;

internal sealed partial class ApplicationImageBuilder(ContainerEngine engine, BuildResources buildResources)
{
	#region 公共方法
	public async Task BuildAsync(ContainerManifest.Component component, ServiceBuildContext source, ContainerManifest manifest, string workspace, string delivery, string tag, CancellationToken cancellation)
	{
		var package = PackageReader.Read(component["package"]);
		ResolveEntry(package, source);

		var runtime = ResolveRuntime(package, component, source);
		var distribution = manifest["distribution"];
		var baseReference = BootstrapPackageBuilder.BaseImage(distribution);
		var baseline = await engine.ResolveAsync(baseReference, null, null, manifest["architecture"], null, cancellation);
		var directory = Path.Combine(workspace, source.Plan.Id);

		var ingress = PrepareImage(component["package"], component["dependences"], package, source, distribution, baseline, runtime, directory);
		var root = source.Plan.WorkingDirectory;

		var reference = buildResources.Image();
		await engine.BuildAsync(directory, baseline.Platform, reference, cancellation, "final");

		await using var resources = new BuildResources(engine);
		if(runtime != null)
			runtime = InstalledRuntime(runtime, await engine.RunAsync(["run", "--name", resources.Container(), "--rm", "--network", "none", "--entrypoint", "dotnet", reference, "--list-runtimes"], directory, cancellation));

		if(ingress)
			await this.ReadIngressAsync(source, reference, directory, resources, cancellation);

		using var inspected = JsonDocument.Parse(await engine.InspectAsync(reference, cancellation));
		var id = inspected.RootElement[0].GetProperty("Id").GetString();

		if(!id.StartsWith("sha256:", StringComparison.Ordinal))
			id = $"sha256:{id}";

		source.Plan.Image = new()
		{
			Id = id,
			Tag = tag,
			Platform = baseline.Platform,
			Mode = "offline",
			Archive = $"images/{source.Plan.Id}.tar"
		};

		source.Plan.Package = new()
		{
			Name = package.Name,
			Version = package.Version,
			Architecture = package.Architecture,
			Format = package.Format,
			Hash = Files.Hash(component["package"]),
			InstallPath = root,
			Runtime = runtime,
			BaseDigest = $"{baseline.Repository}@{baseline.Digest}"
		};

		await engine.ExportAsync(source.Plan.Image.Id, Path.Combine(delivery, source.Plan.Image.Archive), cancellation);
	}
	#endregion

	#region 内部方法
	internal static void ResolveEntry(PackageReader.Descriptor package, ServiceBuildContext source)
	{
		var services = package.Texts.Where(pair => pair.Key.EndsWith(".service", StringComparison.Ordinal)).ToArray();
		if(services.Length != 1)
			throw new ContainerizationException(2, Properties.Resources.ApplicationBuilder_3_Message);

		var lines = services[0].Value.Split('\n').Select(line => line.Trim()).Where(line => line.Contains('=')).Select(line => line.Split('=', 2)).ToArray();
		var entries = lines.Where(pair => pair[0] == "ExecStart").ToArray();
		var directories = lines.Where(pair => pair[0] == "WorkingDirectory").ToArray();

		if(entries.Length != 1 || directories.Length != 1)
			throw new ContainerizationException(2, Properties.Resources.ApplicationBuilder_4_Message);

		var listeners = ApplicationHealth.ParseListeners(package.Listen, package.Name);
		var listening = string.Join(';', listeners.Select(listener => listener.GetLeftPart(UriPartial.Authority)));
		var entry = listeners.Length == 0 ? entries[0][1] : entries[0][1].Replace(package.Listen, listening, StringComparison.Ordinal);
		var command = listening.Length == 0 ? entry : entry.Replace(listening, "", StringComparison.Ordinal);

		if(command.IndexOfAny(['$', '%', '\\', '\'', '"', ';', '|', '&', '\n']) >= 0)
			throw new ContainerizationException(2, Properties.Resources.ApplicationBuilder_4_Message);

		var arguments = entry.Split(' ', StringSplitOptions.RemoveEmptyEntries);
		if(arguments.Length == 0 || arguments[0].EndsWith("sh", StringComparison.Ordinal))
			throw new ContainerizationException(2, Properties.Resources.ApplicationBuilder_5_Message);

		source.Plan.Entrypoint = arguments;
		source.Plan.WorkingDirectory = TemplateCatalog.LinuxPath(directories[0][1]);

		foreach(var environment in lines.Where(pair => pair[0] == "Environment"))
		{
			var pair = environment[1].Split('=', 2);
			if(pair.Length != 2 || pair[0].Any(char.IsWhiteSpace))
				throw new ContainerizationException(2, Properties.Resources.ApplicationBuilder_6_Message);

			source.Environment.TryAdd(pair[0], pair[1]);
		}

		var signal = lines.FirstOrDefault(pair => pair[0] == "KillSignal");
		if(signal != null)
			source.Plan.StopSignal = signal[1];

		ApplicationHealth.Configure(package, source);
	}

	internal static string Quote(string text) => $"'{text.Replace("'", "'\"'\"'", StringComparison.Ordinal)}'";
	internal static string InstalledRuntime(string required, string output)
	{
		var match = RuntimeRegex().Match(required);
		var minimum = Versioning.Version.Number.Parse(match.Groups[2].Value);
		var framework = match.Groups[1].Value == "aspnetcore-runtime" ? "Microsoft.AspNetCore.App" : "Microsoft.NETCore.App";
		var versions = output.Split('\n').Select(line => line.Split(' ', StringSplitOptions.RemoveEmptyEntries))
			.Where(parts => parts.Length >= 2 && parts[0] == framework)
			.Select(parts => Versioning.Version.Number.TryParse(parts[1], out var version) ? version : (Versioning.Version.Number?)null)
			.OfType<Versioning.Version.Number>()
			.Where(version => version.Major == minimum.Major && version.Minor == minimum.Minor && version >= minimum)
			.OrderDescending().ToArray();

		if(versions.Length == 0)
			throw new ContainerizationException(2, Properties.Resources.ApplicationBuilder_10_Message);

		return $"{match.Groups[1].Value}-{versions[0]}";
	}

	internal static string ProbeInstall(string distribution, bool required)
	{
		if(!required)
			return string.Empty;

		if(Distribution.IsDebian(distribution))
			return "apt-get update && apt-get install -y --no-install-recommends ca-certificates curl && ";

		return "dnf install -y ca-certificates && (command -v curl >/dev/null 2>&1 || dnf install -y curl-minimal) && ";
	}
	#endregion

	#region 私有方法
	private static string ResolveRuntime(PackageReader.Descriptor package, ContainerManifest.Component component, ServiceBuildContext source)
	{
		var entry = source.Plan.Entrypoint.FirstOrDefault(value => value.EndsWith(".dll", StringComparison.Ordinal));
		if(entry == null)
			return null;

		var name = $"{Path.GetFileNameWithoutExtension(entry)}.runtimeconfig.json";
		var candidates = package.Texts.Where(pair => pair.Key.EndsWith($"/{name}", StringComparison.Ordinal) || pair.Key == name).ToArray();

		if(candidates.Length != 1)
			throw new ContainerizationException(2, Properties.Resources.ApplicationBuilder_9_Message);

		using var json = JsonDocument.Parse(candidates[0].Value);
		var options = json.RootElement.GetProperty("runtimeOptions");
		var frameworks = options.TryGetProperty("frameworks", out var array) ? array.EnumerateArray().ToArray() : options.TryGetProperty("framework", out var framework) ? [framework] : [];

		if(frameworks.Length == 0)
			return null;

		var selected = frameworks.OrderBy(item => item.GetProperty("name").GetString() == "Microsoft.AspNetCore.App" ? 0 : 1).First();
		var runtime = $"{(selected.GetProperty("name").GetString() == "Microsoft.AspNetCore.App" ? "aspnetcore-runtime-" : "dotnet-runtime-")}{selected.GetProperty("version").GetString()}";

		foreach(var dependency in (component["dependences"] ?? "").Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Where(value => value.StartsWith("runtime-", StringComparison.Ordinal)))
		{
			var expected = dependency.StartsWith("runtime-aspnetcore-", StringComparison.Ordinal) ? $"aspnetcore-runtime-{dependency[18..]}" : $"dotnet-runtime-{dependency[8..]}";
			if(expected != runtime)
				throw new ContainerizationException(2, Properties.Resources.ApplicationBuilder_10_Message);
		}

		return runtime;
	}

	internal static string RuntimeInstall(string distribution, string runtime)
	{
		if(runtime == null)
			return string.Empty;

		var match = RuntimeRegex().Match(runtime);
		if(!match.Success)
			throw new ContainerizationException(2, Properties.Resources.ApplicationBuilder_11_Message);

		var number = Versioning.Version.Number.Parse(match.Groups[2].Value);
		var name = $"{match.Groups[1].Value}-{number.Major}.{number.Minor}";
		var parts = distribution.Split('@');

		if(distribution == "ubuntu@22.04" && number.Major >= 9)
			return $"apt-get update && apt-get install -y --no-install-recommends ca-certificates curl gnupg software-properties-common && add-apt-repository -y ppa:dotnet/backports && apt-get update && apt-get install -y --no-install-recommends {name} && ";

		if(parts[0] is "ubuntu" or "debian")
			return $"apt-get update && apt-get install -y ca-certificates curl && curl -fsSL https://packages.microsoft.com/config/{parts[0]}/{parts[1]}/packages-microsoft-prod.deb -o /tmp/microsoft.deb && dpkg -i /tmp/microsoft.deb && apt-get update && apt-get install -y --no-install-recommends {name} && ";
		return $"rpm --import https://packages.microsoft.com/keys/microsoft.asc && rpm -U https://packages.microsoft.com/config/rhel/9/packages-microsoft-prod.rpm && dnf install -y {name} && ";
	}

	[GeneratedRegex(@"^(dotnet-runtime|aspnetcore-runtime)-(\d+\.\d+\.\d+)$")]
	private static partial Regex RuntimeRegex();
	#endregion
}
