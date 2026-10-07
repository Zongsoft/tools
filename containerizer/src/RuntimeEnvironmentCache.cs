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
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Collections.Generic;

using Zongsoft.Components;
using Zongsoft.Terminals;
using Zongsoft.Tools.Containerizer.Protocol;

namespace Zongsoft.Tools.Containerizer;

/// <summary>Shares verified public runtime files, never application build layers or inputs.</summary>
internal sealed partial class RuntimeEnvironmentCache(ContainerEngine engine, string cacheRoot = null, bool refresh = false,
	Action<CommandOutletContent> output = null, Action<string> error = null)
{
	#region 成员字段
	private readonly Action<CommandOutletContent> _output = output ?? Terminal.WriteLine;
	private readonly HashSet<string> _refreshedProfiles = [];
	private readonly Dictionary<string, ImagePlan> _baseImages = [];
	#endregion

	#region 公共方法
	public async Task<ImagePlan> PrepareAsync(string distribution, string architecture, string runtime, string directory, CancellationToken cancellation)
	{
		var recipe = GetRecipe(distribution, runtime);
		var profile = GetProfile(distribution, architecture, runtime);
		var root = Path.Combine(cacheRoot ?? BuildStorage.CacheRoot, "runtime", engine.Executable, profile);

		Files.EnsureNoLinks(root);
		Files.CreatePrivateDirectory(root);

		using var cacheLock = await BuildStorage.AcquireLockAsync(root, cacheRoot, cancellation);
		using var timing = Output.Measure(_output, profile);
		var baselineKey = $"{distribution}/{architecture}";

		if(!_baseImages.TryGetValue(baselineKey, out var baseline))
		{
			baseline = await engine.ResolveAsync(BootstrapPackageBuilder.GetBaseImage(distribution), null, null, architecture, cancellation, refresh);
			_baseImages.Add(baselineKey, baseline);
		}

		var signature = Files.HashText($"{baseline.Repository}@{baseline.Digest}\n{baseline.Platform}\n{recipe}");
		var metadata = Path.Combine(root, "environment.json");

		Files.EnsureNoLinks(metadata);

		var cached = ReadRecord(metadata, root, signature, runtime);
		var rebuild = refresh && !_refreshedProfiles.Contains(profile);

		if(cached != null && !rebuild)
			_output(Output.FormatMessage(Properties.Resources.RuntimeCache_Hit, profile));
		else
		{
			_output(Output.FormatMessage(rebuild ? Properties.Resources.RuntimeCache_Refresh : Properties.Resources.RuntimeCache_Prepare, profile));
			cached = await this.CreateEnvironmentAsync(root, baseline, recipe, signature, runtime, cancellation);
			_refreshedProfiles.Add(profile);
		}

		// Copy while holding the lock: another build may replace this generation afterwards.
		Files.CreatePrivateDirectory(directory);
		File.Copy(Files.ResolveRelativePath(root, cached.Archive), Path.Combine(directory, "runtime.tar"));
		Files.Write(Path.Combine(directory, "runtime.env"), string.Join('\n', cached.Environment.Select(value => value.Split('=', 2)).Select(pair => $"ENV {pair[0]}={JsonSerializer.Serialize(pair[1])}")), true);
		CleanUnusedArchives(root, cached.Archive);

		return baseline;
	}

	#endregion

	#region 内部方法
	internal static string GetInstalledRuntime(string required, string output)
	{
		var match = GetRuntimeRegex().Match(required);
		var minimum = Versioning.Version.Number.Parse(match.Groups[2].Value);
		var framework = match.Groups[1].Value == "aspnetcore-runtime" ? "Microsoft.AspNetCore.App" : "Microsoft.NETCore.App";
		var versions = output.Split('\n')
			.Select(line => line.Split(' ', StringSplitOptions.RemoveEmptyEntries))
			.Where(parts => parts.Length >= 2 && parts[0] == framework)
			.Select(parts => Versioning.Version.Number.TryParse(parts[1], out var version) ? version : (Versioning.Version.Number?)null)
			.OfType<Versioning.Version.Number>()
			.Where(version => version.Major == minimum.Major && version.Minor == minimum.Minor && version >= minimum)
			.OrderDescending().ToArray();

		if(versions.Length == 0)
			throw new ContainerizationException(2, Properties.Resources.ApplicationBuilder_10_Message);

		return $"{match.Groups[1].Value}-{versions[0]}";
	}

	#endregion

	#region 私有方法
	private static string GetProfile(string distribution, string architecture, string runtime)
	{
		var line = runtime == null ? "native" : runtime[..runtime.LastIndexOf('.')];
		return $"{distribution}-{architecture}-{line}";
	}

	private static string GetRecipe(string distribution, string runtime) =>
		$"{GetProbeInstallCommand(distribution, runtime == null || !Distribution.IsDebianFamily(distribution))}{GetRuntimeInstallCommand(distribution, runtime)}rm -rf /tmp/* /var/tmp/* /var/log/* /var/cache/apt/* /var/cache/dnf/*";

	private static string GetProbeInstallCommand(string distribution, bool required)
	{
		if(!required)
			return string.Empty;

		if(Distribution.IsDebianFamily(distribution))
			return "apt-get update && apt-get install -y --no-install-recommends ca-certificates curl && ";

		return "dnf install -y ca-certificates && (command -v curl >/dev/null 2>&1 || dnf install -y curl-minimal) && ";
	}

	private static string GetRuntimeInstallCommand(string distribution, string runtime)
	{
		if(runtime == null)
			return string.Empty;

		var match = GetRuntimeRegex().Match(runtime);
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

	private async Task<Record> CreateEnvironmentAsync(string root, ImagePlan baseline, string recipe, string signature, string runtime, CancellationToken cancellation)
	{
		var workspace = Path.Combine(Path.GetTempPath(), $"containerizer-runtime-{Guid.NewGuid():N}");
		var archive = $"runtime-{Guid.NewGuid():N}.tar";
		var path = Files.ResolveRelativePath(root, archive);
		var published = false;

		try
		{
			Files.CreatePrivateDirectory(workspace);
			await using var resources = new BuildResources(engine, error);

			var image = resources.RegisterImage();
			Files.Write(Path.Combine(workspace, "Dockerfile"), $"FROM {engine.GetBuildReference(baseline)}\nRUN {recipe}\n", true);
			await engine.BuildAsync(workspace, baseline.Platform, image, cancellation);
			var runtimes = runtime == null ? string.Empty : await engine.RunAsync(["run", "--name", resources.RegisterContainer(), "--rm", "--network", "none", "--entrypoint", "dotnet", image, "--list-runtimes"], workspace, cancellation);

			if(runtime != null)
				GetInstalledRuntime(runtime, runtimes);

			using var inspected = JsonDocument.Parse(await engine.InspectAsync(image, cancellation));
			if(!ContainerEngine.MatchesPlatform(inspected.RootElement[0], baseline.Platform))
				throw new ContainerizationException(4, Properties.Resources.Engine_4_Message);

			var config = inspected.RootElement[0].GetProperty("Config");
			if(config.TryGetProperty("Volumes", out var volumes) && volumes.ValueKind == JsonValueKind.Object && volumes.EnumerateObject().Any())
				throw new ContainerizationException(4, Properties.Resources.RuntimeCache_Volumes);

			var container = resources.RegisterContainer();
			await engine.RunAsync(["create", "--name", container, "--entrypoint", "/bin/true", image], workspace, cancellation);
			await engine.RunAsync(["export", "--output", path, container], workspace, cancellation);

			var record = new Record
			{
				Signature = signature,
				Archive = archive,
				Hash = Files.Hash(path),
				Length = new FileInfo(path).Length,
				Runtimes = runtimes,
				Environment = config.TryGetProperty("Env", out var variables) && variables.ValueKind == JsonValueKind.Array ? variables.EnumerateArray().Select(value => value.GetString()).ToArray() : [],
			};

			cancellation.ThrowIfCancellationRequested();

			// Only the small pointer is atomically replaced. A failed refresh leaves the previous generation intact.
			Files.Save(Path.Combine(root, "environment.json"), record, CacheJson.Default.Record);
			published = true;

			return record;
		}
		finally
		{
			if(!published && File.Exists(path))
				File.Delete(path);
			if(Directory.Exists(workspace))
				Directory.Delete(workspace, true);
		}
	}

	private static Record ReadRecord(string metadata, string root, string signature, string runtime)
	{
		if(!File.Exists(metadata))
			return null;

		try
		{
			var record = JsonSerializer.Deserialize(File.ReadAllText(metadata), CacheJson.Default.Record);
			if(record == null || record.Signature != signature || !IsCacheArchiveName(record.Archive) || record.Environment == null || record.Runtimes == null)
				return null;
			if(record.Environment.Any(value => string.IsNullOrEmpty(value) || value.IndexOf('=') < 1))
				return null;

			var path = Files.ResolveRelativePath(root, record.Archive);
			if(!File.Exists(path) || new FileInfo(path).Length != record.Length || Files.Hash(path) != record.Hash)
				return null;
			if(runtime != null)
				GetInstalledRuntime(runtime, record.Runtimes);

			return record;
		}
		catch(JsonException) { return null; }
		catch(ContainerizationException exception) when(exception.Code == 2) { return null; }
	}

	private static bool IsCacheArchiveName(string name) => name?.Length == 44 && name.StartsWith("runtime-", StringComparison.Ordinal) && name.EndsWith(".tar", StringComparison.Ordinal) && Guid.TryParseExact(name[8..^4], "N", out _);
	private static void CleanUnusedArchives(string root, string current)
	{
		foreach(var path in Directory.EnumerateFiles(root, "runtime-*.tar").Where(path => IsCacheArchiveName(Path.GetFileName(path)) && Path.GetFileName(path) != current))
		{
			Files.EnsureNoLinks(path);
			File.Delete(path);
		}
	}

	#endregion

	#region 嵌套类型
	private sealed class Record
	{
		public string Signature { get; set; }
		public string Archive { get; set; }
		public string Hash { get; set; }
		public long Length { get; set; }
		public string Runtimes { get; set; }
		public string[] Environment { get; set; }
	}

	[JsonSerializable(typeof(Record))]
	private partial class CacheJson : JsonSerializerContext;

	[GeneratedRegex(@"^(dotnet-runtime|aspnetcore-runtime)-(\d+\.\d+\.\d+)$")]
	private static partial Regex GetRuntimeRegex();
	#endregion
}
