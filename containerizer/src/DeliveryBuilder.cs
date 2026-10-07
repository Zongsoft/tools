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
using Zongsoft.Components;

using Zongsoft.Tools.Containerizer.Protocol;

namespace Zongsoft.Tools.Containerizer;

internal sealed partial class DeliveryBuilder(IProcessRunner runner, string cacheRoot = null, string executorPath = null, bool refresh = false, Action<CommandOutletContent> output = null, Action<string> error = null)
{
	#region 成员字段
	private readonly Action<CommandOutletContent> _output = output ?? Terminal.WriteLine;
	private readonly Action<string> _error = error ?? Terminal.Default.Error.WriteLine;
	#endregion

	#region 公共方法
	public string Plan(ContainerManifest manifest)
	{
		var sources = ServicePlanner.Prepare(manifest, _output);

		foreach(var source in sources)
		{
			foreach(var site in source.Plan.Web)
			{
				var ports = source.Plan.Ports.Where(port => site.Bindings.Any(binding => binding.Publication == port.Name));
				_output(Output.FormatMessage(Properties.Resources.Web_Plan, source.Plan.Id, $"{site.Application}/{site.Name}",
					string.Join(", ", site.Bindings.Select(WebPackage.GetAddress)), string.Join(", ", site.Hosts),
					string.Join(", ", ports.Select(port => $"{port.Container}:{port.Host}"))));
			}
		}

		if(manifest.IsComplete)
		{
			foreach(var component in manifest.Components)
			{
				if(component["settings"] != null)
					component["settings"] = ServiceSettings.Format(component.Settings.Select(pair => new KeyValuePair<string, string>(pair.Key, ContainerManifest.EscapeVariables(pair.Value))));

				foreach(var key in component.Values.Keys.Where(key => key.StartsWith("environment!", StringComparison.OrdinalIgnoreCase)).ToArray())
					component[key] = ContainerManifest.EscapeVariables(component[key]);
			}
		}

		using var publishLock = BuildStorage.AcquireLock(manifest["output"], cacheRoot);
		using var publisher = new ArtifactPublisher(manifest["output"], false, Path.GetFileName(manifest.ManifestPath));
		manifest.Prepare(Path.GetDirectoryName(publisher.StagePath(Path.GetFileName(manifest.ManifestPath))), true);
		publisher.Commit();

		return manifest.ManifestPath;
	}

	public async Task<string> BuildAsync(ContainerManifest manifest, CancellationToken cancellation)
	{
		using var timing = Output.Measure(_output, Properties.Resources.Build_Total);
		var sources = ServicePlanner.Prepare(manifest, _output);
		var executor = FindExecutor(manifest["architecture"], executorPath);
		var workspace = Path.Combine(Path.GetTempPath(), $"containerizer-build-{Guid.NewGuid().ToString("N")}");
		var delivery = Path.Combine(workspace, "node");

		var engine = await ContainerEngine.ConnectAsync(manifest["engine"], runner, cancellation, error: _error);
		engine.Mirrors = RegistryMirrorSettings.Read(manifest["output"]);

		var buildResources = new BuildResources(engine, _error);
		var environments = new RuntimeEnvironmentCache(engine, cacheRoot, refresh, _output, _error);

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
			Files.CreatePrivateDirectory(workspace);
			Files.CreatePrivateDirectory(delivery);
			WebIngress.Render(sources, workspace);

			foreach(var source in sources)
			{
				var component = source.Component;

				if(component.IsApplication)
					continue;

				var pinned = component["digest"] != null;
				_output(Output.FormatMessage(Properties.Resources.NodeBuilder_ResolveImage, component.Name));
				using var resolveTiming = Output.Measure(_output, string.Format(Properties.Resources.NodeBuilder_ResolveImage, component.Name));
				source.Plan.Image = await engine.ResolveAsync(source.ImageRepository, component["digest"], manifest.Defaults.SelectTag(component), node.Architecture, cancellation);
				source.Plan.Image.Reference = CreateImageReference(node, source.Plan.Id);
				source.Plan.Image.Mode = component["imaging"] ?? manifest["imaging"];

				if(source.Plan.Image.Mode is not ("offline" or "online"))
					throw new ContainerizationException(2, Properties.Resources.NodeBuilder_2_Message);

				component["digest"] = source.Plan.Image.Digest;
				component["tag"] = source.Plan.Image.SourceTag;
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

			foreach(var source in sources)
			{
				_output(Output.FormatMessage(Properties.Resources.NodeBuilder_PrepareImage, source.Plan.Id));

				if(source.Component.IsApplication)
					await new ApplicationImageBuilder(engine, buildResources, environments, _output, _error).BuildAsync(source, manifest, workspace, delivery, CreateImageReference(node, source.Plan.Id), cancellation);
				else if(source.Plan.Image.Mode == "offline")
				{
					using var exportTiming = Output.Measure(_output, string.Format(Properties.Resources.NodeBuilder_PrepareImage, source.Plan.Id));
					source.Plan.Image.Archive = $"images/{source.Plan.Id}.tar";
					await engine.ExportAsync(source.Plan.Image.Id, Path.Combine(delivery, source.Plan.Image.Archive), cancellation);
				}

				await new ServiceImagePreparer(engine, _error).PrepareAsync(source, workspace, cancellation);
			}

			ServicePlanner.Validate(sources);

			foreach(var source in sources)
			{
				CollectConfiguration(source, delivery);

				if(node.Distribution.StartsWith("rhel", StringComparison.Ordinal) || node.Distribution.StartsWith("rocky", StringComparison.Ordinal) || node.Distribution.StartsWith("almalinux", StringComparison.Ordinal))
				{
					foreach(var mount in source.Plan.Mounts)
						mount.Selinux = "Z";
				}

				UpdateConfigurationHash(source, delivery);
			}

			_output(Output.FormatMessage(Properties.Resources.NodeBuilder_ResolveBootstrap));

			using(Output.Measure(_output, Properties.Resources.NodeBuilder_ResolveBootstrap))
				node.Bootstrap = await new BootstrapPackageBuilder(engine, cacheRoot, _error).BuildAsync(manifest, workspace, delivery, cancellation);

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

			Files.Write(Path.Combine(delivery, "install.sh"), CreateLauncher("install"), true);
			Files.Write(Path.Combine(delivery, "uninstall.sh"), CreateLauncher("uninstall"), true);
			Files.Write(Path.Combine(delivery, "README.md"), CreateInstructions(node, manifest.ReleaseName));
			Files.Write(Path.Combine(delivery, "README.zh-Hans.md"), CreateInstructionsZhHans(node, manifest.ReleaseName));
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

			using(Output.Measure(_output, Properties.Resources.Build_Checksums))
				node.Files = Directory.EnumerateFiles(delivery, "*", SearchOption.AllDirectories).Order(StringComparer.Ordinal).Select(path => new FileRecord { Path = Path.GetRelativePath(delivery, path).Replace('\\', '/'), Hash = Files.Hash(path), Length = new FileInfo(path).Length }).ToList();

			Files.Save(Path.Combine(delivery, DeliveryPlan.FileName), node, ProtocolJson.Default.DeliveryPlan);
			Files.Write(Path.Combine(delivery, "checksums.sha256"), $"{string.Join('\n', node.Files.Append(new() { Path = DeliveryPlan.FileName, Hash = Files.Hash(Path.Combine(delivery, DeliveryPlan.FileName)) }).Select(file => $"{file.Hash}  {file.Path}"))}\n", true);

			var archivePath = Path.Combine(workspace, $"{manifest.ReleaseName}.tar.gz");

			using(Output.Measure(_output, Properties.Resources.Build_Compression))
				Files.Archive(delivery, archivePath);

			cancellation.ThrowIfCancellationRequested();
			using var publishLock = BuildStorage.AcquireLock(manifest["output"], cacheRoot);
			var defaults = ServiceDefaults.Prepare(manifest, workspace);

			using(Output.Measure(_output, Properties.Resources.Build_Publish))
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
				_error(string.Format(Properties.Resources.NodeBuilder_FailedBuild_Message, workspace, exception.Message));
			}
		}
	}
	#endregion

}
