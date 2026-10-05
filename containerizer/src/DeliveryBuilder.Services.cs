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

partial class DeliveryBuilder
{
	#region 内部方法
	internal static List<ServiceBuildContext> PrepareSources(ContainerManifest manifest)
	{
		var sources = new List<ServiceBuildContext>();
		foreach(var component in manifest.Components)
		{
			var source = component.IsApplication ? ServiceBuildContext.FromApplication(component, manifest) : new TemplateCatalog().Read(component, manifest);
			foreach(var required in source.RequiredEnvironment)
			{
				if(!source.Environment.TryGetValue(required, out var value) || string.IsNullOrEmpty(value))
					source.MissingSettings.Add(required);
			}

			foreach(var missing in source.MissingSettings.Distinct(StringComparer.OrdinalIgnoreCase))
			{
				var message = string.Format(Properties.Resources.NodeBuilder_1_Message, component.Name, missing);
				if(!manifest.IsPlanning)
					throw new ContainerizationException(2, message);
				Terminal.WriteLine(Output.Message(Properties.Resources.NodeBuilder_1_Message, CommandOutletColor.Magenta, component.Name, missing));
			}

			if(!component.IsApplication)
			{
				component["tag"] = manifest.Defaults.SelectTag(component);
				component["repository"] = source.SourceImage;
				component["settings"] = ServiceSettings.Format(source.Settings);
			}

			if(!manifest.IsPlanning)
			{
				foreach(var pair in source.Environment.Where(pair => !source.ManagedEnvironment.Contains(pair.Key)))
					component[$"environment!{pair.Key}"] = pair.Value;
				foreach(var key in component.Values.Keys.Where(key => key.StartsWith("environment!", StringComparison.OrdinalIgnoreCase)).ToArray())
					component[key] = source.Environment[key[12..]];
			}

			sources.Add(source);
		}

		ValidateServices(sources);
		return sources;
	}

	internal static void ValidateServices(IReadOnlyList<ServiceBuildContext> sources)
	{
		var names = sources.ToDictionary(source => source.Plan.Id, StringComparer.Ordinal);

		foreach(var source in sources)
		{
			for(int index = 0; index < source.Plan.Dependencies.Count; index++)
			{
				var dependency = source.Plan.Dependencies[index];

				if(!names.ContainsKey(dependency))
				{
					throw new ContainerizationException(2, string.Format(Properties.Resources.NodeBuilder_6_Message, dependency));
				}
			}
		}

		var visited = new HashSet<string>(StringComparer.Ordinal);
		var stack = new HashSet<string>(StringComparer.Ordinal);

		foreach(var name in names.Keys)
			Visit(name);

		var ports = sources.SelectMany(source => source.Plan.Ports).Where(port => port.Host > 0).ToArray();
		for(int index = 0; index < ports.Length; index++)
		{
			if(ports.Skip(index + 1).Any(port => PortsConflict(ports[index], port)))
				throw new ContainerizationException(2, Properties.Resources.NodeBuilder_7_Message);
		}

		void Visit(string name)
		{
			if(visited.Contains(name))
				return;
			if(!stack.Add(name))
				throw new ContainerizationException(2, Properties.Resources.NodeBuilder_8_Message);

			foreach(var dependency in names[name].Plan.Dependencies)
			{
				if(Rank(names[dependency].Plan.Kind) > Rank(names[name].Plan.Kind))
					throw new ContainerizationException(2, Properties.Resources.NodeBuilder_9_Message);

				Visit(dependency);
			}

			stack.Remove(name);
			visited.Add(name);
		}

		static int Rank(string kind) => kind == "infrastructure" ? 0 : kind == "application" ? 1 : 2;
	}

	internal static void ConfigureIngress(ContainerManifest manifest, IReadOnlyList<ServiceBuildContext> sources, string workspace)
	{
		var applications = manifest.Components.Where(component => component.IsApplication && (component["dependences"] ?? "").Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Any(dependence => string.Equals(dependence, "nginx", StringComparison.Ordinal))).Select(component => sources.Single(source => string.Equals(source.Plan.Id, component.Name, StringComparison.OrdinalIgnoreCase))).ToArray();
		if(applications.Length == 0)
			return;

		var ingress = sources.Single(source => source.Plan.Id == "nginx");
		ingress.Plan.Dependencies.AddRange(applications.Select(source => source.Plan.Id));

		foreach(var application in applications)
		{
			foreach(var pair in application.Ingress)
				ingress.Configuration.Add($"/etc/nginx/conf.d/{pair.Key}", pair.Value);
		}

		if(ingress.Configuration.Count > 0)
			return;
		if(applications.Length != 1)
			throw new ContainerizationException(2, Properties.Resources.NodeBuilder_10_Message);

		var app = applications[0];
		var ports = app.Plan.Ports.Select(port => port.Container).Distinct().ToArray();
		var arguments = (app.Plan.Entrypoint ?? []).Concat(app.Plan.Command ?? []).ToArray();
		var index = Array.IndexOf(arguments, "--urls");

		if(ports.Length == 0 && index >= 0 && index + 1 < arguments.Length &&
			Uri.TryCreate(arguments[index + 1], UriKind.Absolute, out var uri) &&
			uri.Scheme == "http" && uri.AbsolutePath == "/" && string.IsNullOrEmpty(uri.Query))
			ports = [uri.Port];
		if(ports.Length != 1)
			throw new ContainerizationException(2, Properties.Resources.NodeBuilder_11_Message);

		var file = Path.Combine(workspace, "nginx.generated.conf");
		Files.Write(file, $"server {{\n\tlisten 80;\n\tlocation / {{\n\t\tproxy_pass http://{app.Plan.Id}:{ports[0]};\n\t\tproxy_set_header Host $host;\n\t\tproxy_set_header X-Forwarded-For $proxy_add_x_forwarded_for;\n\t\tproxy_set_header X-Forwarded-Proto $scheme;\n\t}}\n}}\n", true);
		ingress.Configuration.Add("/etc/nginx/conf.d/default.conf", file);
	}
	#endregion

	#region 私有方法
	private static bool PortsConflict(PortPlan left, PortPlan right) =>
		left.Host == right.Host && left.Protocol == right.Protocol &&
		(left.Address == right.Address || left.Address is "0.0.0.0" or "::" || right.Address is "0.0.0.0" or "::");

	private static void CollectConfiguration(ServiceBuildContext source, string delivery)
	{
		foreach(var pair in source.Configuration)
		{
			var relative = $"config/{source.Plan.Id}/{Files.HashText(pair.Key)[..16]}/{Path.GetFileName(pair.Value)}";
			var target = Files.Below(delivery, relative);
			Directory.CreateDirectory(Path.GetDirectoryName(target));

			if(Directory.Exists(pair.Value))
				Files.CopyTree(pair.Value, target);
			else
				File.Copy(pair.Value, target);

			source.Plan.Mounts.Add(new() { Source = relative, Target = pair.Key, ReadOnly = true, Owned = true });
		}
	}

	private static void ConfigurationHash(ServiceBuildContext source, string delivery)
	{
		var hashes = new List<string>();

		foreach(var mount in source.Plan.Mounts.Where(mount => mount.ReadOnly))
		{
			var path = Files.Below(delivery, mount.Source);

			if(File.Exists(path))
				hashes.Add($"{mount.Target}{Files.Hash(path)}");
			else
				hashes.AddRange(Files.Enumerate(path).Order(StringComparer.Ordinal).Select(file => $"{mount.Target}/{Path.GetRelativePath(path, file).Replace('\\', '/')}{Files.Hash(file)}"));
		}

		var signature = JsonSerializer.Deserialize(JsonSerializer.Serialize(source.Plan, ProtocolJson.Default.ServicePlan), ProtocolJson.Default.ServicePlan);
		signature.Image = new() { Id = source.Plan.Image.Id, Platform = source.Plan.Image.Platform };
		signature.Template = null;
		signature.TemplateHash = null;
		signature.ConfigurationHash = null;
		var configuration = JsonSerializer.Serialize(signature, ProtocolJson.Default.ServicePlan);
		var environment = string.Join('\n', source.Environment.OrderBy(pair => pair.Key).Select(pair => $"{pair.Key}={pair.Value}"));
		source.Plan.ConfigurationHash = Files.HashText($"{configuration}{environment}{string.Join('\n', hashes)}");
	}
	#endregion
}
