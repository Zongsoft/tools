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
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using System.Runtime.InteropServices;

using Zongsoft.Tools.Containerizer.Protocol;

namespace Zongsoft.Tools.Containerizer.Execution;

partial class DockerHost
{
	#region 公共方法
	public async Task InstallExecutorAsync(DeliveryBundle bundle, CancellationToken cancellation)
	{
		using var hostLock = store.AcquireHostLock();
		var target = Installation.Paths.ExecutorPath;
		var marker = Path.Combine(store.StateRoot, "executor.identity");
		Files.EnsureNoLinks(target);

		if(File.Exists(target))
		{
			if(!File.Exists(marker) || File.ReadAllText(marker).Trim() != Files.Hash(target))
				throw new ContainerizationException(3, Properties.Resources.DockerHost_6_Message);

			var metadata = await this.RunCommandAsync(target, ["--protocol"], null, cancellation);
			using var json = JsonDocument.Parse(metadata);

			if(json.RootElement.GetProperty("minimum").GetInt32() <= bundle.Plan.Schema && json.RootElement.GetProperty("maximum").GetInt32() >= bundle.Plan.Schema)
				return;
		}

		foreach(var installation in store.List())
		{
			if(installation.Schema != DeliveryPlan.ProtocolVersion)
				throw new ContainerizationException(3, Properties.Resources.DockerHost_7_Message);
		}

		var candidate = Files.ResolveRelativePath(bundle.Directory, "containerizer");
		var protocol = await this.RunCommandAsync(candidate, ["--protocol"], null, cancellation);

		using(var json = JsonDocument.Parse(protocol))
		{
			if(json.RootElement.GetProperty("tool").GetString() != "containerizer" ||
				json.RootElement.GetProperty("minimum").GetInt32() != DeliveryPlan.ProtocolVersion ||
				json.RootElement.GetProperty("maximum").GetInt32() < bundle.Plan.Schema)
				throw new ContainerizationException(3, Properties.Resources.DockerHost_8_Message);
		}

		var staging = $"{target}.{Guid.NewGuid().ToString("N")}";
		Directory.CreateDirectory(Path.GetDirectoryName(target));

		try
		{
			var resources = Path.Combine(bundle.Directory, "zh-Hans", "containerizer.resources.dll");
			if(File.Exists(resources))
			{
				var resourceTarget = Path.Combine(Path.GetDirectoryName(target), "zh-Hans", "containerizer.resources.dll");
				var resourceMarker = $"{marker}.zh-Hans";
				Files.EnsureNoLinks(resourceTarget);

				if(File.Exists(resourceTarget) && (!File.Exists(resourceMarker) || File.ReadAllText(resourceMarker).Trim() != Files.Hash(resourceTarget)))
					throw new ContainerizationException(3, Properties.Resources.DockerHost_6_Message);

				Directory.CreateDirectory(Path.GetDirectoryName(resourceTarget));
				ArtifactPublisher.Write(resourceTarget, output =>
				{
					using var input = File.OpenRead(resources);
					input.CopyTo(output);
				});

				Files.Write(resourceMarker, Files.Hash(resourceTarget));
			}

			File.Copy(candidate, staging);
			if(!OperatingSystem.IsWindows())
				File.SetUnixFileMode(staging, (UnixFileMode)493);

			File.Move(staging, target, true);
			Files.Write(marker, Files.Hash(target));
		}
		finally { if(File.Exists(staging)) File.Delete(staging); }
	}

	public async Task PrepareBootstrapAsync(DeliveryBundle bundle, Installation installation, CancellationToken cancellation)
	{
		var engineProbe = await runner.RunAsync("sh", ["-c", "command -v docker"], null, cancellation, 30);
		if(engineProbe.ExitCode == 0)
		{
			await this.RunCommandAsync(ENGINE, ["version", "--format", "{{.Server.Version}}"], null, cancellation);
			await this.RunCommandAsync(ENGINE, ["compose", "version"], null, cancellation);
			await this.RunCommandAsync(ENGINE, ["compose", "up", "--help"], null, cancellation);

			return;
		}

		var plan = bundle.Plan.Bootstrap;
		if(plan.Packages.Count == 0)
			throw new ContainerizationException(5, Properties.Resources.DockerHost_9_Message);

		var cacheRoot = store.GetCachePath(bundle.Plan.Name);

		using(var ownershipLock = store.AcquireHostLock())
			this.PrepareOwnedDirectory(installation, cacheRoot);

		Files.CreatePrivateDirectory(cacheRoot);
		var cache = Path.Combine(cacheRoot, bundle.Id, "bootstrap");
		Files.CreatePrivateDirectory(cache);

		var paths = new List<string>();
		using var client = new HttpClient { Timeout = TimeSpan.FromMinutes(30) };

		foreach(var package in plan.Packages)
		{
			var path = plan.Mode == "offline" ? Files.ResolveRelativePath(bundle.Directory, package.Path) : Path.Combine(cache, Path.GetFileName(package.Path));

			if(!File.Exists(path))
			{
				if(plan.Mode != "online" || !Uri.TryCreate(package.Url, UriKind.Absolute, out var uri) || uri.Scheme != "https")
					throw new ContainerizationException(5, Properties.Resources.DockerHost_10_Message);

				using var response = await client.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, cancellation);
				response.EnsureSuccessStatusCode();
				var temporary = $"{path}.partial";

				try
				{
					await using(var output = new FileStream(temporary, FileMode.Create, FileAccess.Write, FileShare.None))
						await response.Content.CopyToAsync(output, cancellation);

					if(new FileInfo(temporary).Length != package.Length || Files.Hash(temporary) != package.Hash)
						throw new ContainerizationException(4, Properties.Resources.DockerHost_11_Message);

					File.Move(temporary, path);
				}
				finally
				{
					if(File.Exists(temporary))
						File.Delete(temporary);
				}
			}

			if(new FileInfo(path).Length != package.Length || Files.Hash(path) != package.Hash)
				throw new ContainerizationException(4, Properties.Resources.DockerHost_12_Message);

			paths.Add(path);
		}

		var usesDebianPackages = bundle.Plan.Distribution.StartsWith("debian", StringComparison.Ordinal) || bundle.Plan.Distribution.StartsWith("ubuntu", StringComparison.Ordinal);
		var arguments = usesDebianPackages ? new List<string>
		{
			"-y",
			"--no-download",
			"--no-remove",
			"--no-install-recommends",
			"-o",
			"Dir::Etc::sourcelist=/dev/null",
			"-o",
			"Dir::Etc::sourceparts=-",
			"-o",
			$"Dir::Cache::archives={PrepareAptCache(paths, Path.Combine(cache, "apt"))}",
			"install"
		} : ["-y", "--disablerepo=*", "--setopt=install_weak_deps=False", "install"];

		arguments.AddRange(paths);
		var result = await runner.RunAsync(usesDebianPackages ? "apt-get" : "dnf", arguments, null, cancellation, 1800);

		if(result.ExitCode != 0)
			throw new ContainerizationException(5, string.Format(Properties.Resources.DockerHost_13_Message, result.ExitCode));

		await this.RunCommandAsync("systemctl", ["enable", "--now", ENGINE], null, cancellation);
		await this.RunCommandAsync(ENGINE, ["compose", "version"], null, cancellation);
	}
	#endregion

	#region 私有方法
	private static string PrepareAptCache(IReadOnlyList<string> packages, string directory)
	{
		var names = new HashSet<string>(StringComparer.Ordinal);
		foreach(var package in packages)
		{
			if(!names.Add(Path.GetFileName(package)))
				throw new ContainerizationException(4, Properties.Resources.DockerHost_12_Message);
		}

		Files.CreatePrivateDirectory(directory);
		foreach(var package in packages)
		{
			var target = Files.ResolveRelativePath(directory, Path.GetFileName(package));
			Files.EnsureNoLinks(target);
			File.Copy(package, target, true);
		}

		return directory;
	}
	#endregion
}
