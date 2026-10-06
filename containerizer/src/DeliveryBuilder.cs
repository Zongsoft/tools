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
using System.Globalization;
using System.Collections.Generic;

using Zongsoft.Terminals;

using Zongsoft.Tools.Containerizer.Protocol;

namespace Zongsoft.Tools.Containerizer;

internal sealed partial class DeliveryBuilder(IProcessRunner runner, string cacheRoot = null, string executorPath = null, bool refresh = false)
{
	#region 公共方法
	public string Plan(ContainerManifest manifest)
	{
		var sources = PrepareSources(manifest);

		foreach(var source in sources)
		{
			foreach(var site in source.Plan.Web)
			{
				var ports = source.Plan.Ports.Where(port => site.Bindings.Any(binding => binding.Publication == port.Name));
				Terminal.WriteLine(Output.Message(Properties.Resources.Web_Plan, source.Plan.Id, $"{site.Application}/{site.Name}",
					string.Join(", ", site.Bindings.Select(WebPackage.Address)), string.Join(", ", site.Hosts),
					string.Join(", ", ports.Select(port => $"{port.Container}:{port.Host}"))));
			}
		}

		if(manifest.IsGenerated)
		{
			foreach(var component in manifest.Components)
			{
				if(component["settings"] != null)
					component["settings"] = ServiceSettings.Format(component.Settings.Select(pair => new KeyValuePair<string, string>(pair.Key, ContainerManifest.EscapeVariables(pair.Value))));

				foreach(var key in component.Values.Keys.Where(key => key.StartsWith("environment!", StringComparison.OrdinalIgnoreCase)).ToArray())
					component[key] = ContainerManifest.EscapeVariables(component[key]);
			}
		}

		using var publishLock = BuildStorage.Lock(manifest["output"], cacheRoot);
		using var publisher = new ArtifactPublisher(manifest["output"], false, Path.GetFileName(manifest.ManifestPath));
		manifest.Prepare(Path.GetDirectoryName(publisher.StagePath(Path.GetFileName(manifest.ManifestPath))), true);
		publisher.Commit();

		return manifest.ManifestPath;
	}

	public async Task<string> BuildAsync(ContainerManifest manifest, CancellationToken cancellation)
	{
		using var timing = Output.Measure(Terminal.WriteLine, Properties.Resources.Build_Total);
		var sources = PrepareSources(manifest);
		var executor = FindExecutor(manifest["architecture"], executorPath);
		var workspace = Path.Combine(Path.GetTempPath(), $"containerizer-build-{Guid.NewGuid().ToString("N")}");
		var delivery = Path.Combine(workspace, "node");

		var engine = await ContainerEngine.ConnectAsync(manifest["engine"], runner, cancellation);
		engine.Mirrors = RegistryMirrorSettings.Read(manifest["output"]);

		var buildResources = new BuildResources(engine);
		var environments = new RuntimeEnvironmentCache(engine, cacheRoot, refresh);

		var node = new DeliveryPlan
		{
			Name = manifest["name"],
			Tag = manifest["tag"],
			Version = manifest["version"],
			Distribution = manifest["distribution"],
			Architecture = manifest["architecture"],
			Project = $"containerizer-{manifest["name"].ToLowerInvariant().Replace('.', '-').Replace('_', '-')}-{Files.HashText(manifest["name"])[..8]}",
			DataRoot = Installation.Paths.GetDataPath(manifest["name"]),
			ToolVersion = typeof(DeliveryBuilder).Assembly.GetName().Version?.ToString(),
			Services = sources.Select(source => source.Plan).ToList(),
		};

		try
		{
			Files.PrivateDirectory(workspace);
			Files.PrivateDirectory(delivery);
			WebIngress.Render(manifest, sources, workspace);

			for(int index = 0; index < sources.Count; index++)
			{
				var component = manifest.Components[index];
				var source = sources[index];

				if(component.IsApplication)
					continue;

				var pinned = component["digest"] != null;
				Terminal.WriteLine(Output.Message(Properties.Resources.NodeBuilder_ResolveImage, component.Name));
				using var resolveTiming = Output.Measure(Terminal.WriteLine, string.Format(Properties.Resources.NodeBuilder_ResolveImage, component.Name));
				source.Plan.Image = await engine.ResolveAsync(source.SourceImage, component["digest"], manifest.Defaults.SelectTag(component), node.Architecture, cancellation);
				source.Plan.Image.Tag = Tag(node, source.Plan.Id);
				source.Plan.Image.Mode = component["imaging"] ?? manifest["imaging"];

				if(source.Plan.Image.Mode is not ("offline" or "online"))
					throw new ContainerizationException(2, Properties.Resources.NodeBuilder_2_Message);

				component["digest"] = source.Plan.Image.Digest;
				component["tag"] = source.Plan.Image.Version;
				component["repository"] = source.Plan.Image.Repository;

				if(!pinned)
				{
					if(source.Plan.Image.Timestamp != null)
						component["timestamp"] = source.Plan.Image.Timestamp;
					else
						component.Values.Remove("timestamp");

					if(source.Plan.Image.Size.HasValue)
						component["size"] = source.Plan.Image.Size.Value.ToString(CultureInfo.InvariantCulture);
					else
						component.Values.Remove("size");
				}
			}

			for(int index = 0; index < sources.Count; index++)
			{
				var source = sources[index];
				Terminal.WriteLine(Output.Message(Properties.Resources.NodeBuilder_PrepareImage, source.Plan.Id));

				if(manifest.Components[index].IsApplication)
					await new ApplicationImageBuilder(engine, buildResources, environments).BuildAsync(manifest.Components[index], source, manifest, workspace, delivery, Tag(node, source.Plan.Id), cancellation);
				else if(source.Plan.Image.Mode == "offline")
				{
					using var exportTiming = Output.Measure(Terminal.WriteLine, string.Format(Properties.Resources.NodeBuilder_PrepareImage, source.Plan.Id));
					source.Plan.Image.Archive = $"images/{source.Plan.Id}.tar";
					await engine.ExportAsync(source.Plan.Image.Id, Path.Combine(delivery, source.Plan.Image.Archive), cancellation);
				}

				await engine.PrepareOwnershipAsync(source, workspace, cancellation);
			}

			ValidateServices(sources);

			foreach(var source in sources)
			{
				CollectConfiguration(source, delivery);

				if(node.Distribution.StartsWith("rhel", StringComparison.Ordinal) || node.Distribution.StartsWith("rocky", StringComparison.Ordinal) || node.Distribution.StartsWith("almalinux", StringComparison.Ordinal))
				{
					foreach(var mount in source.Plan.Mounts)
						mount.Selinux = "Z";
				}

				ConfigurationHash(source, delivery);
			}

			Terminal.WriteLine(Properties.Resources.NodeBuilder_ResolveBootstrap);

			using(Output.Measure(Terminal.WriteLine, Properties.Resources.NodeBuilder_ResolveBootstrap))
				node.Bootstrap = await new BootstrapPackageBuilder(engine, cacheRoot).BuildAsync(manifest, workspace, delivery, cancellation);

			foreach(var path in manifest.Migrations)
			{
				var input = MigrationInput.Parse(path);
				var archive = $"migration/{Path.GetFileName(path)}";
				var script = $"migration/{Path.GetFileName(input.Script)}";

				Directory.CreateDirectory(Path.Combine(delivery, "migration"));
				File.Copy(path, Path.Combine(delivery, archive));
				File.Copy(input.Script, Path.Combine(delivery, script));
				node.Migrations.Add(new() { Version = input.VersionText, Archive = archive, Script = script, Identity = Files.HashText($"{Files.Hash(path)}{Files.Hash(input.Script)}") });
			}

			File.Copy(executor, Path.Combine(delivery, "containerizer"));
			var resources = Path.Combine(Path.GetDirectoryName(executor), "zh-Hans");
			var nestedResources = Path.Combine(resources, "zh-Hans");

			if(Directory.Exists(nestedResources))
				resources = nestedResources;

			if(Directory.Exists(resources))
				Files.CopyTree(resources, Path.Combine(delivery, "zh-Hans"));

			Files.Write(Path.Combine(delivery, "install.sh"), Launcher("install"), true);
			Files.Write(Path.Combine(delivery, "uninstall.sh"), Launcher("uninstall"), true);
			Files.Write(Path.Combine(delivery, "README.md"), Instructions(node, manifest.ReleaseName));
			Files.Write(Path.Combine(delivery, "README.zh-Hans.md"), InstructionsZhHans(node, manifest.ReleaseName));
			ComposeWriter.Write(node, sources, delivery);
			var prepared = manifest.Prepare(workspace);
			node.SourceHash = Files.Hash(prepared);
			File.Copy(prepared, Path.Combine(delivery, Path.GetFileName(prepared)));

			foreach(var service in node.Services)
			{
				// Runtime arguments belong to the protected Compose asset; the lock records hashes only.
				service.Command = null;
				service.Entrypoint = null;
				service.Health.Test = null;
			}

			using(Output.Measure(Terminal.WriteLine, Properties.Resources.Build_Checksums))
				node.Files = Directory.EnumerateFiles(delivery, "*", SearchOption.AllDirectories).Order(StringComparer.Ordinal).Select(path => new FileRecord { Path = Path.GetRelativePath(delivery, path).Replace('\\', '/'), Hash = Files.Hash(path), Length = new FileInfo(path).Length }).ToList();

			Files.Save(Path.Combine(delivery, DeliveryPlan.FileName), node, ProtocolJson.Default.DeliveryPlan);
			Files.Write(Path.Combine(delivery, "checksums.sha256"), $"{string.Join('\n', node.Files.Append(new() { Path = DeliveryPlan.FileName, Hash = Files.Hash(Path.Combine(delivery, DeliveryPlan.FileName)) }).Select(file => $"{file.Hash}  {file.Path}"))}\n", true);

			var archivePath = Path.Combine(workspace, $"{manifest.ReleaseName}.tar.gz");

			using(Output.Measure(Terminal.WriteLine, Properties.Resources.Build_Compression))
				Files.Archive(delivery, archivePath);

			cancellation.ThrowIfCancellationRequested();
			using var publishLock = BuildStorage.Lock(manifest["output"], cacheRoot);
			var defaults = ServiceDefaults.Prepare(manifest, workspace);

			using(Output.Measure(Terminal.WriteLine, Properties.Resources.Build_Publish))
				BuildStorage.Publish(manifest, prepared, archivePath, defaults);
			return Path.Combine(manifest["output"], Path.GetFileName(archivePath));
		}
		finally
		{
			await buildResources.DisposeAsync();

			try
			{
				if(Directory.Exists(workspace))
					Directory.Delete(workspace, true);
			}
			catch(Exception exception)
			{
				Terminal.Default.Error.WriteLine(string.Format(Properties.Resources.NodeBuilder_FailedBuild_Message, workspace, exception.Message));
			}
		}
	}
	#endregion

}
