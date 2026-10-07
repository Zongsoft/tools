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
using System.Linq;
using System.Collections.Generic;

using Zongsoft.Components;
using Zongsoft.Tools.Containerizer.Protocol;

namespace Zongsoft.Tools.Containerizer;

internal static class ServicePlanner
{
	#region 公共方法
	public static List<ServiceBuildContext> Prepare(ContainerManifest manifest, Action<CommandOutletContent> output = null)
	{
		var sources = new List<ServiceBuildContext>();

		foreach(var component in manifest.Components)
		{
			var source = component.IsApplication ? ApplicationPlanner.Create(component, manifest) : TemplateCatalog.Read(component, manifest);
			foreach(var missing in source.MissingSettings.Distinct(StringComparer.OrdinalIgnoreCase))
			{
				var message = string.Format(Properties.Resources.NodeBuilder_1_Message, component.Name, missing);

				if(!manifest.IsPlanning)
					throw new ContainerizationException(2, message);

				output?.Invoke(Output.FormatMessage(Properties.Resources.NodeBuilder_1_Message, CommandOutletColor.Magenta, component.Name, missing));
			}

			if(!component.IsApplication)
			{
				component["tag"] = manifest.Defaults.SelectTag(component);
				component["repository"] = source.ImageRepository;
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

		WebIngress.Plan(manifest, sources);
		Validate(sources);
		return sources;
	}

	public static void Validate(IReadOnlyList<ServiceBuildContext> sources)
	{
		var names = sources.ToDictionary(source => source.Plan.Id, StringComparer.Ordinal);

		foreach(var source in sources)
		{
			for(int index = 0; index < source.Plan.Dependencies.Count; index++)
			{
				var dependency = source.Plan.Dependencies[index];

				if(!names.ContainsKey(dependency))
					throw new ContainerizationException(2, string.Format(Properties.Resources.NodeBuilder_6_Message, dependency));
			}
		}

		var visited = new HashSet<string>(StringComparer.Ordinal);
		var stack = new HashSet<string>(StringComparer.Ordinal);

		foreach(var name in names.Keys)
			Visit(name);

		var ports = sources.SelectMany(source => source.Plan.Ports).Where(port => port.Host > 0).ToArray();
		for(int index = 0; index < ports.Length; index++)
		{
			if(ports.Skip(index + 1).Any(port => HasPortConflict(ports[index], port)))
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
				if(GetRank(names[dependency].Plan.Kind) > GetRank(names[name].Plan.Kind))
					throw new ContainerizationException(2, Properties.Resources.NodeBuilder_9_Message);

				Visit(dependency);
			}

			stack.Remove(name);
			visited.Add(name);
		}

		static int GetRank(string kind) => kind == "infrastructure" ? 0 : kind == "application" ? 1 : 2;
	}
	#endregion

	#region 私有方法
	private static bool HasPortConflict(PortPlan left, PortPlan right) =>
		left.Host == right.Host && left.Protocol == right.Protocol &&
		(left.Address == right.Address || left.Address is "0.0.0.0" or "::" || right.Address is "0.0.0.0" or "::");
	#endregion
}
