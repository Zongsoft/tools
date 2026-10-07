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
using System.Collections.Generic;

using Zongsoft.Configuration.Profiles;
using Zongsoft.Serialization.Json;
using Zongsoft.Tools.Containerizer.Protocol;

namespace Zongsoft.Tools.Containerizer;

internal static class TemplateCatalog
{
	#region 静态字段
	private static readonly HashSet<string> _names = new(StringComparer.OrdinalIgnoreCase)
	{
		"mysql", "mariadb", "postgresql", "sqlserver", "mongodb", "clickhouse", "tdengine", "influxdb",
		"redis", "valkey", "memcached", "rabbitmq", "kafka", "nats", "mosquitto", "emqx", "etcd", "consul", "zookeeper", "nacos", "rustfs",
		"nginx", "caddy", "haproxy", "prometheus", "grafana", "loki", "otel", "opensearch", "elasticsearch",
	};
	#endregion

	#region 公共方法
	public static bool Contains(string name) => _names.Contains(name);
	public static ServiceBuildContext Read(ContainerManifest.Component component, ContainerManifest manifest)
	{
		if(component.IsApplication)
			throw new ContainerizationException(2, string.Format(Properties.Resources.Manifest_8_Message, component.Name, "template"));

		var template = component["template"] ?? component.Name;
		if(Contains(template))
			template = template.ToLowerInvariant();
		var path = Contains(template) ? Find($"{template.ToLowerInvariant()}.template") : manifest.ResolvePath(template);

		if(!File.Exists(path))
			throw new ContainerizationException(2, string.Format(Properties.Resources.ServiceSource_1_Message, component.Name, path));

		var profile = Profile.Load(path, new ProfileOptions { ImportBehavior = ProfileDirectiveBehavior.Suppressed });
		var root = profile.Entries.ToDictionary(
			entry => entry.Name,
			entry => entry.Name.ToLowerInvariant() is "entrypoint" or "command" or "health" ? entry.Value : Resolve(entry.Value), StringComparer.OrdinalIgnoreCase);

		if(root.GetValueOrDefault("version") != "1")
			throw new ContainerizationException(2, Properties.Resources.ServiceSource_13_Message);
		if(root.GetValueOrDefault("kind") == "application")
			throw new ContainerizationException(2, string.Format(Properties.Resources.Manifest_8_Message, component.Name, "kind=application"));

		string[] permitted =
		[
			"image",
			"kind",
			"platforms",
			"version",
			"entrypoint",
			"command",
			"workdir",
			"user",
			"data-owner",
			"restart",
			"stop-signal",
			"health",
			"health-timeout",
			"dependencies"
		];

		foreach(var key in root.Keys)
		{
			if(!permitted.Contains(key, StringComparer.OrdinalIgnoreCase))
				throw new ContainerizationException(2, string.Format(Properties.Resources.ServiceSource_2_Message, path, key));
		}

		if(!(root.GetValueOrDefault("platforms") ?? "x64;arm64").Split(';').Contains(manifest["architecture"]))
			throw new ContainerizationException(3, string.Format(Properties.Resources.ServiceSource_3_Message, component.Name));

		var source = new ServiceBuildContext(component)
		{
			ImageRepository = manifest.Defaults.SelectRepository(component, root.GetValueOrDefault("image")),

			Plan = new()
			{
				Id = component.Name.ToLowerInvariant(),
				User = root.GetValueOrDefault("user"),
				Kind = root.GetValueOrDefault("kind") ?? "infrastructure",
				Entrypoint = ParseArguments(root.GetValueOrDefault("entrypoint")),
				Command = ParseArguments(root.GetValueOrDefault("command")),
				WorkingDirectory = root.GetValueOrDefault("workdir"),
				Restart = root.GetValueOrDefault("restart") ?? "unless-stopped",
				StopSignal = root.GetValueOrDefault("stop-signal") ?? "SIGTERM",
				Template = template,
				TemplateHash = Files.Hash(path),
				Health = new()
				{
					Test = ParseArguments(
						root.GetValueOrDefault("health"))?.Select(value => value.Replace("$$", "$", StringComparison.Ordinal)).ToArray(),
					TimeoutSeconds = int.Parse(root.GetValueOrDefault("health-timeout") ?? "5", System.Globalization.CultureInfo.InvariantCulture)
				},
				Dependencies = [.. (root.GetValueOrDefault("dependencies") ?? "").Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)],
			},
		};

		ImageReference.ValidateRepository(source.ImageRepository);
		var settings = component.Settings;
		var declared = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

		foreach(var section in profile.Sections)
		{
			if(section.Name.Equals("environment", StringComparison.OrdinalIgnoreCase))
			{
				foreach(var entry in section.Entries)
				{
					if(!component.Values.ContainsKey($"environment!{entry.Name}"))
						source.Environment.Add(entry.Name, Resolve(entry.Value) ?? "");
				}
			}
			else if(section.Name.Equals("settings", StringComparison.OrdinalIgnoreCase))
			{
				foreach(var parameter in section.Sections)
				{
					declared.Add(parameter.Name);

					var fields = parameter.Entries.ToDictionary(
						entry => entry.Name,
						entry => entry.Name.Equals("default", StringComparison.OrdinalIgnoreCase) ? entry.Value : Resolve(entry.Value), StringComparer.OrdinalIgnoreCase);

					foreach(var key in fields.Keys)
					{
						if(key is not ("environment" or "argument" or "variable" or "default" or "required" or "choices"))
							throw new ContainerizationException(2, string.Format(Properties.Resources.ServiceSource_4_Message, key));
					}

					if(fields.TryGetValue("environment", out var managed))
						source.ManagedEnvironment.Add(managed);

					string value;
					if(settings.TryGetValue(parameter.Name, out var supplied))
						value = manifest.ResolveValue(supplied);
					else if(fields.TryGetValue("environment", out var binding) && component.Values.TryGetValue($"environment!{binding}", out var explicitValue))
						value = manifest.ResolveValue(explicitValue);
					else if(fields.TryGetValue("variable", out var variable))
						value = manifest.IsPlanning ? $"$({variable})" : manifest.Variables.TryGetValue(variable, out var referenced) ? Resolve(referenced) : Resolve(fields.GetValueOrDefault("default"));
					else
						value = Resolve(fields.GetValueOrDefault("default"));

					if(fields.GetValueOrDefault("required") == "true")
					{
						var missing = string.IsNullOrEmpty(value);
						if(!missing && manifest.IsPlanning)
						{
							var evaluated = VariableEvaluator.Evaluate(value, manifest.Variables, allowEscapes: true);
							missing = !evaluated.Succeed || string.IsNullOrEmpty(evaluated.Value);
						}

						if(missing)
							source.MissingSettings.Add(parameter.Name);
					}

					if(value == null)
					{
						if(manifest.IsPlanning && fields.GetValueOrDefault("required") == "true")
							source.Settings[parameter.Name] = "";
						continue;
					}

					source.Settings[parameter.Name] = value;
					var deferred = manifest.IsPlanning && ContainerManifest.HasVariables(value);
					if(!deferred && fields.TryGetValue("choices", out var choices) && !choices.Split(';').Contains(value, StringComparer.Ordinal))
						throw new ContainerizationException(2, string.Format(Properties.Resources.ServiceSource_5_Message, component.Name, parameter.Name));

					if(fields.TryGetValue("environment", out var environment))
					{
						source.Environment[environment] = value;
					}

					if(fields.TryGetValue("argument", out var argument))
					{
						source.Plan.Command = [.. source.Plan.Command ?? [], argument, value];
					}
				}
			}
			else if(section.Name.Equals("ports", StringComparison.OrdinalIgnoreCase))
			{
				foreach(var entry in section.Entries)
				{
					var port = ParsePort(Resolve(entry.Value));
					port.Name = entry.Name == "default" ? "port" : $"{entry.Name}-port";
					if(entry.Name != "default")
						port.Host = 0;
					source.Plan.Ports.Add(port);
				}
			}
			else if(section.Name.Equals("data", StringComparison.OrdinalIgnoreCase))
			{
				var directory = $"{Installation.Paths.GetDataPath(manifest["name"])}/{source.Plan.Id}";

				foreach(var entry in section.Entries)
				{
					ContainerManifest.ValidateIdentity(entry.Name);

					source.Plan.Mounts.Add(new()
					{
						Source = section.Entries.Count == 1 ? directory : $"{directory}/{entry.Name}",
						Target = Files.NormalizeLinuxPath(Resolve(entry.Value)),
						Owned = true,
						User = root.GetValueOrDefault("data-owner")
					});
				}
			}
			else if(section.Name.Equals("configuration", StringComparison.OrdinalIgnoreCase))
			{
				foreach(var entry in section.Entries)
				{
					var input = Path.GetFullPath(Resolve(entry.Value), Path.GetDirectoryName(path));

					if(!File.Exists(input))
						throw new ContainerizationException(2, string.Format(Properties.Resources.ServiceSource_6_Message, input));

					source.Configuration.Add(Files.NormalizeLinuxPath(entry.Name), (input, null));
				}
			}
			else
				throw new ContainerizationException(2, string.Format(Properties.Resources.ServiceSource_7_Message, section.Name));
		}

		ServiceOptions.Apply(component, manifest, source, declared);

		foreach(var key in settings.Keys)
		{
			if(!declared.Contains(key))
				throw new ContainerizationException(2, string.Format(Properties.Resources.ServiceSource_8_Message, component.Name, key));
		}

		ServiceOptions.ApplyEnvironment(component, source, manifest);
		return source;

		string Resolve(string value) => value == null || manifest.IsPlanning ? value : ContainerManifest.Evaluate(value, manifest.Variables, allowEscapes: true);
		string[] ParseArguments(string value) => ParseArray(value)?.Select(Resolve).ToArray();
	}

	public static string Find(string name)
	{
		string[] roots = [Path.Combine(AppContext.BaseDirectory, "templates"), Path.Combine(AppContext.BaseDirectory, "..", "..", "templates")];
		foreach(var root in roots)
		{
			if(File.Exists(Path.Combine(root, name)))
				return Path.GetFullPath(Path.Combine(root, name));
		}

		throw new ContainerizationException(3, string.Format(Properties.Resources.ServiceSource_9_Message, name));
	}

	private static string[] ParseArray(string value)
	{
		if(string.IsNullOrEmpty(value))
			return null;

		using var document = JsonDocument.Parse(value);
		return document.RootElement.GetArray(item => item.GetString() ?? throw new ContainerizationException(2, Properties.Resources.ServiceSource_10_Message)) ??
			throw new ContainerizationException(2, Properties.Resources.ServiceSource_10_Message);
	}

	private static PortPlan ParsePort(string value)
	{
		var protocol = value.Split('/', 2);
		var parts = protocol[0].Split(':');

		if(parts.Length is < 2 or > 3 ||
		   !int.TryParse(parts[^1], out var target) ||
		   !int.TryParse(parts[^2], out var published) ||
		   target is < 1 or > 65535 ||
		   published is < 1 or > 65535 ||
		   protocol.Length == 2 &&
		   protocol[1] is not ("tcp" or "udp"))
			throw new ContainerizationException(2, Properties.Resources.ServiceSource_12_Message);

		if(parts.Length == 3 && (!System.Net.IPAddress.TryParse(parts[0], out var address) || address.AddressFamily != System.Net.Sockets.AddressFamily.InterNetwork))
			throw new ContainerizationException(2, Properties.Resources.ServiceSource_12_Message);

		return new()
		{
			Address = parts.Length == 3 ? parts[0] : "0.0.0.0",
			Host = published,
			Container = target,
			Protocol = protocol.Length == 2 ? protocol[1] : "tcp"
		};
	}
	#endregion
}
