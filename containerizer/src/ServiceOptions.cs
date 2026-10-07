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
using System.Net;
using System.Globalization;
using System.Collections.Generic;

using Zongsoft.Tools.Containerizer.Protocol;

namespace Zongsoft.Tools.Containerizer;

internal static class ServiceOptions
{
	public static void ApplyEnvironment(ContainerManifest.Component component, ServiceBuildContext source, ContainerManifest manifest = null)
	{
		foreach(var pair in component.Values.Where(pair => pair.Key.StartsWith("environment!", StringComparison.OrdinalIgnoreCase)))
		{
			var name = pair.Key[12..];
			var value = manifest == null ? pair.Value ?? "" : manifest.ResolveValue(pair.Value);

			if(source.ManagedEnvironment.Contains(name) && source.Environment.TryGetValue(name, out var mapped) && mapped != value)
			{
				var deferred = manifest?.IsPlanning == true && (ContainerManifest.HasVariables(mapped) || ContainerManifest.HasVariables(value));
				if(!deferred)
					throw new ContainerizationException(2, string.Format(Properties.Resources.Settings_Conflict_Message, component.Name, pair.Key));
			}

			source.Environment[name] = value;
		}
	}

	public static void Apply(ContainerManifest.Component component, ContainerManifest manifest, ServiceBuildContext source, HashSet<string> declared)
	{
		var settings = component.Settings;

		if(source.Plan.Template == "nginx")
		{
			declared.Add("port");

			if(settings.TryGetValue("port", out var ports))
				source.Settings["port"] = manifest.ResolveValue(ports);
		}

		foreach(var port in source.Plan.Ports)
		{
			var value = Select(port.Name, port.Host == 0 ? "none" : $"{port.Address}:{port.Host.ToString(CultureInfo.InvariantCulture)}");

			if(manifest.IsPlanning && ContainerManifest.HasVariables(value))
			{
				port.Host = 0;
				continue;
			}

			if(value == "none")
			{
				port.Host = 0;
				continue;
			}

			var separator = value.LastIndexOf(':');
			var address = separator < 0 ? "127.0.0.1" : value[..separator].Trim('[', ']');
			var number = separator < 0 ? value : value[(separator + 1)..];

			if(!IPAddress.TryParse(address, out var parsed) ||
				!int.TryParse(number, NumberStyles.None, CultureInfo.InvariantCulture, out var host) ||
				host is < 1 or > 65535 ||
				parsed.AddressFamily == System.Net.Sockets.AddressFamily.InterNetworkV6 && !value.StartsWith('['))
				throw new ContainerizationException(2, Properties.Resources.ServiceSource_12_Message);

			port.Address = parsed.ToString();
			port.Host = host;
		}

		if(source.Plan.Mounts.Any(mount => !mount.ReadOnly))
		{
			var storage = Select("storage", "persistent");
			if(!(manifest.IsPlanning && ContainerManifest.HasVariables(storage)) && storage is not ("persistent" or "temporary"))
				throw new ContainerizationException(2, string.Format(Properties.Resources.ServiceSource_5_Message, component.Name, "storage"));

			foreach(var mount in source.Plan.Mounts.Where(mount => !mount.ReadOnly))
				mount.Temporary = storage == "temporary";
		}

		// Redis and Valkey share one persistence/authentication model.
		if(source.Plan.Template is "redis" or "valkey")
		{
			var persistence = Select("persistence", "both");
			var password = Select("password", "");

			if(!(manifest.IsPlanning && ContainerManifest.HasVariables(persistence)))
			{
				if(persistence is not ("none" or "rdb" or "aof" or "both"))
					throw new ContainerizationException(2, string.Format(Properties.Resources.ServiceSource_5_Message, component.Name, "persistence"));

				source.Plan.Command = [.. source.Plan.Command ?? [], "--save", persistence is "rdb" or "both" ? "3600 1 300 100 60 10000" : "", "--appendonly", persistence is "aof" or "both" ? "yes" : "no", "--appendfsync", "everysec"];
			}

			if(password.Length > 0)
			{
				source.Plan.Command = [.. source.Plan.Command, "--requirepass", password];
				source.Plan.Health.Test = ["CMD", $"{source.Plan.Template}-cli", "-a", password, "ping"];
			}
		}

		string Select(string name, string fallback)
		{
			declared.Add(name);
			return source.Settings[name] = settings.TryGetValue(name, out var supplied) ? manifest.ResolveValue(supplied) : source.Settings.GetValueOrDefault(name, fallback);
		}
	}
}
