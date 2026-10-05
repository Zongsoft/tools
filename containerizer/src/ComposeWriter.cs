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
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Collections.Generic;

using Zongsoft.Tools.Containerizer.Protocol;

namespace Zongsoft.Tools.Containerizer;

internal static class ComposeWriter
{
	#region 公共方法
	public static void Write(DeliveryPlan node, IReadOnlyList<ServiceBuildContext> sources, string directory)
	{
		var services = new JsonObject();

		foreach(var source in sources)
		{
			var service = source.Plan;
			var value = new JsonObject
			{
				["image"] = service.Image.Tag,
				["platform"] = service.Image.Platform,
				["pull_policy"] = "never",
				["restart"] = "no",
				["stop_signal"] = service.StopSignal,
				["stop_grace_period"] = $"{service.StopSeconds}s",
				["labels"] = new JsonObject { ["org.zongsoft.containerizer.name"] = node.Name, ["org.zongsoft.containerizer.service"] = service.Id },
				["logging"] = new JsonObject { ["driver"] = "json-file", ["options"] = new JsonObject { ["max-size"] = service.LogSize, ["max-file"] = service.LogFiles } },
			};

			if(service.Entrypoint != null)
				value["entrypoint"] = Array(service.Entrypoint);
			if(service.Command != null)
				value["command"] = Array(service.Command);
			if(service.WorkingDirectory != null)
				value["working_dir"] = service.WorkingDirectory;
			if(service.User != null)
				value["user"] = service.User;
			if(service.Hostname != null)
				value["hostname"] = service.Hostname;
			if(service.Memory != null)
				value["mem_limit"] = service.Memory;
			if(service.Cpus != null)
				value["cpus"] = service.Cpus;

			if(source.Environment.Count > 0)
			{
				var environment = new JsonObject();

				foreach(var pair in source.Environment)
					environment[pair.Key] = pair.Value.Replace("$", "$$", StringComparison.Ordinal);

				value["environment"] = environment;
			}

			var mounts = new JsonArray();
			foreach(var mount in service.Mounts)
			{
				if(mount.Temporary)
				{
					mounts.Add(new JsonObject { ["type"] = "volume", ["target"] = mount.Target });
					continue;
				}

				var origin = mount.ReadOnly ? $"./{mount.Source}" : mount.Source;
				var bind = new JsonObject { ["create_host_path"] = false };

				if(mount.Selinux != null)
					bind["selinux"] = mount.Selinux;

				mounts.Add(new JsonObject
				{
					["type"] = "bind",
					["source"] = origin.Replace("$", "$$", StringComparison.Ordinal),
					["target"] = mount.Target,
					["read_only"] = mount.ReadOnly,
					["bind"] = bind
				});
			}

			if(mounts.Count > 0)
				value["volumes"] = mounts;

			var ports = new JsonArray();

			foreach(var port in service.Ports.Where(port => port.Host > 0))
				ports.Add(new JsonObject
				{
					["target"] = port.Container,
					["published"] = port.Host.ToString(System.Globalization.CultureInfo.InvariantCulture),
					["host_ip"] = port.Address,
					["protocol"] = port.Protocol
				});

			if(ports.Count > 0)
				value["ports"] = ports;

			if(service.Health.Test != null)
				value["healthcheck"] = new JsonObject
				{
					["test"] = Array(service.Health.Test),
					["interval"] = $"{service.Health.IntervalSeconds}s",
					["timeout"] = $"{service.Health.TimeoutSeconds}s",
					["retries"] = service.Health.Retries,
					["start_period"] = $"{service.Health.StartSeconds}s"
				};

			value["networks"] = new JsonObject
			{
				["default"] = new JsonObject { ["aliases"] = Array(service.Aliases) }
			};

			services.Add(service.Id, value);
		}

		var document = new JsonObject
		{
			["name"] = node.Project,
			["services"] = services,
			["networks"] = new JsonObject
			{
				["default"] = new JsonObject
				{
					["labels"] = new JsonObject { ["org.zongsoft.containerizer.name"] = node.Name }
				}
			}
		};

		Files.Write(Path.Combine(directory, "compose.yaml"), document.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
	}
	#endregion

	#region 私有方法
	private static JsonArray Array(IEnumerable<string> items) => new(items.Select(item => JsonValue.Create(item.Replace("$", "$$", StringComparison.Ordinal))).Cast<JsonNode>().ToArray());
	#endregion
}
