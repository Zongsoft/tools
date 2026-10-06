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
using System.Threading;
using System.Threading.Tasks;

using Zongsoft.Tools.Containerizer.Protocol;

namespace Zongsoft.Tools.Containerizer;

internal sealed class BootstrapPackageBuilder(ContainerEngine engine, string cacheRoot = null)
{
	#region 公共方法
	public static string BaseImage(string distribution) => distribution switch
	{
		"ubuntu@22.04" => "docker.io/library/ubuntu:22.04",
		"debian@12" => "docker.io/library/debian:12",
		"debian@13" => "docker.io/library/debian:13",
		"rocky@9" => "docker.io/rockylinux/rockylinux:9",
		"almalinux@9" => "docker.io/library/almalinux:9",
		"rhel@9" => "registry.access.redhat.com/ubi9/ubi:9",
		_ => throw new ContainerizationException(3, Properties.Resources.BootstrapBuilder_1_Message),
	};

	public async Task<BootstrapPlan> BuildAsync(ContainerManifest manifest, string workspace, string delivery, CancellationToken cancellation)
	{
		var distribution = manifest["distribution"];
		var profile = $"{distribution}_{manifest["architecture"]}";
		var imported = Path.Combine(manifest["source"], ".containerizer", "bootstrap", profile);
		var cache = BuildStorage.BootstrapCache(manifest["source"], profile, cacheRoot);

		Files.PrivateDirectory(Path.GetDirectoryName(cache));
		using var cacheLock = new FileStream($"{cache}.lock", FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);

		if(File.Exists(Path.Combine(imported, "bootstrap.lock.json")))
			return Import(imported, delivery, profile, manifest["bootstrap"]);
		if(File.Exists(Path.Combine(cache, "bootstrap.lock.json")))
			return Import(cache, delivery, profile, manifest["bootstrap"]);
		if(distribution == "rhel@9")
			throw new ContainerizationException(3, Properties.Resources.BootstrapBuilder_2_Message);

		var baseline = await engine.ResolveAsync(BaseImage(distribution), null, null, manifest["architecture"], cancellation);
		var directory = Path.Combine(workspace, "bootstrap");
		Files.PrivateDirectory(directory);

		var scriptName = Distribution.IsDebian(distribution) ? "bootstrap-deb.sh" : "bootstrap-rpm.sh";
		File.Copy(TemplateCatalog.Find(scriptName), Path.Combine(directory, "collect.sh"));
		Files.Write(Path.Combine(directory, "Dockerfile"), $"FROM {engine.BuildReference(baseline)}\nCOPY collect.sh /collect.sh\nRUN sh /collect.sh\n", true);

		await using var resources = new BuildResources(engine);
		var name = resources.Image();
		await engine.BuildAsync(directory, baseline.Platform, name, cancellation);

		var container = resources.Container();
		await engine.RunAsync(["create", "--name", container, name], directory, cancellation);
		var packages = Path.Combine(delivery, "packages");
		Directory.CreateDirectory(packages);

		await engine.RunAsync(["cp", $"{container}:/delivery/.", packages], directory, cancellation);

		var plan = new BootstrapPlan
		{
			Mode = manifest["bootstrap"],
			Profile = $"{distribution}_{manifest["architecture"]}",
			BaseDigest = $"{baseline.Repository}@{baseline.Digest}",
			Metadata = "packages/metadata.tsv"
		};

		foreach(var line in File.ReadLines(Path.Combine(packages, "metadata.tsv")))
		{
			var fields = line.Split('\t');

			if(fields.Length != 6)
				throw new ContainerizationException(4, Properties.Resources.BootstrapBuilder_3_Message);

			var file = Files.Below(packages, fields[0]);

			plan.Packages.Add(new()
			{
				Path = $"packages/{fields[0]}",
				Name = fields[1],
				Version = fields[2],
				Architecture = fields[3],
				Url = fields[4],
				Dependencies = fields[5],
				Hash = Files.Hash(file),
				Length = new FileInfo(file).Length
			});

			if(fields[1] == "docker-ce")
				plan.EngineVersion = fields[2];
			if(fields[1] == "docker-compose-plugin")
				plan.ComposeVersion = fields[2];
		}

		if(plan.Packages.Count == 0 || plan.EngineVersion == null || plan.ComposeVersion == null)
			throw new ContainerizationException(4, Properties.Resources.BootstrapBuilder_4_Message);

		var staging = $"{cache}.{Guid.NewGuid().ToString("N")}";

		try
		{
			Files.PrivateDirectory(staging);
			Files.CopyTree(packages, Path.Combine(staging, "packages"));
			Files.Save(Path.Combine(staging, "bootstrap.lock.json"), plan, ProtocolJson.Default.BootstrapPlan);
			Directory.Move(staging, cache);
		}
		finally
		{
			if(Directory.Exists(staging))
				Directory.Delete(staging, true);
		}

		if(plan.Mode == "online")
		{
			foreach(var package in plan.Packages)
				File.Delete(Files.Below(delivery, package.Path));
		}

		return plan;
	}
	#endregion

	#region 内部方法
	internal static BootstrapPlan Import(string directory, string delivery, string profile, string mode)
	{
		Files.CheckTree(directory);
		var plan = Files.Load(Path.Combine(directory, "bootstrap.lock.json"), ProtocolJson.Default.BootstrapPlan);

		if(plan.Profile != profile || plan.Packages.Count == 0 || string.IsNullOrEmpty(plan.EngineVersion) || string.IsNullOrEmpty(plan.ComposeVersion))
			throw new ContainerizationException(4, Properties.Resources.BootstrapBuilder_5_Message);

		var paths = new System.Collections.Generic.HashSet<string>(StringComparer.Ordinal);
		foreach(var package in plan.Packages)
		{
			if(!paths.Add(package.Path) ||
			   !package.Path.StartsWith("packages/", StringComparison.Ordinal) ||
			   string.IsNullOrWhiteSpace(package.Name) ||
			   string.IsNullOrWhiteSpace(package.Version) ||
			   !Uri.TryCreate(package.Url, UriKind.Absolute, out var uri) || uri.Scheme != "https")
				throw new ContainerizationException(4, Properties.Resources.BootstrapBuilder_6_Message);

			var path = Files.Below(directory, package.Path);
			if(!File.Exists(path) || new FileInfo(path).Length != package.Length || Files.Hash(path) != package.Hash)
				throw new ContainerizationException(4, Properties.Resources.BootstrapBuilder_7_Message);

			var architecture = profile.EndsWith("_arm64", StringComparison.Ordinal) ? "arm64" : "x64";
			if(package.Architecture is not ("all" or "noarch") &&
			   package.Architecture != (architecture == "arm64" ? "arm64" : "amd64") &&
			   package.Architecture != (architecture == "arm64" ? "aarch64" : "x86_64"))
				throw new ContainerizationException(4, Properties.Resources.BootstrapBuilder_8_Message);

			if(mode == "offline")
			{
				var target = Files.Below(delivery, package.Path);
				Directory.CreateDirectory(Path.GetDirectoryName(target));
				File.Copy(path, target);
			}
		}

		var metadata = Files.Below(directory, plan.Metadata);
		var destination = Files.Below(delivery, plan.Metadata);

		Directory.CreateDirectory(Path.GetDirectoryName(destination));
		File.Copy(metadata, destination);
		plan.Mode = mode;

		return plan;
	}
	#endregion
}
