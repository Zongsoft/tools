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

using Zongsoft.Tools.Containerizer.Protocol;

namespace Zongsoft.Tools.Containerizer;

internal static class ApplicationPlanner
{
	#region 公共方法
	public static ServiceBuildContext Create(ContainerManifest.Component component, ContainerManifest manifest)
	{
		if(component["template"] != null || component["settings"] != null)
			throw new ContainerizationException(2, string.Format(Properties.Resources.Manifest_8_Message, component.Name, component["template"] != null ? "template" : "settings"));

		component.ReadPackage();
		var source = new ServiceBuildContext(component)
		{
			Plan = new() { Id = component.Name.ToLowerInvariant(), Kind = "application", Restart = "unless-stopped", StopSignal = "SIGTERM", Health = new() },
		};

		ServiceOptions.ApplyEnvironment(component, source, manifest);
		ResolveEntry(source.Package, source);

		return source;
	}

	public static string ResolveRuntime(ServiceBuildContext source)
	{
		var package = source.Package;
		var component = source.Component;
		var entry = source.Plan.Entrypoint.FirstOrDefault(value => value.EndsWith(".dll", StringComparison.Ordinal));

		if(entry == null)
			return null;

		var name = $"{Path.GetFileNameWithoutExtension(entry)}.runtimeconfig.json";
		var candidates = package.Texts.Where(pair => pair.Key.EndsWith($"/{name}", StringComparison.Ordinal) || pair.Key == name).ToArray();

		if(candidates.Length != 1)
			throw new ContainerizationException(2, Properties.Resources.ApplicationBuilder_9_Message);

		using var json = JsonDocument.Parse(candidates[0].Value);
		var options = json.RootElement.GetProperty("runtimeOptions");
		var frameworks = options.TryGetProperty("frameworks", out var array) ? array.EnumerateArray().ToArray() : options.TryGetProperty("framework", out var framework) ? [framework] : [];

		if(frameworks.Length == 0)
			return null;

		var selected = frameworks.OrderBy(item => item.GetProperty("name").GetString() == "Microsoft.AspNetCore.App" ? 0 : 1).First();
		var runtime = $"{(selected.GetProperty("name").GetString() == "Microsoft.AspNetCore.App" ? "aspnetcore-runtime-" : "dotnet-runtime-")}{selected.GetProperty("version").GetString()}";

		foreach(var dependency in (component["dependences"] ?? "").Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Where(value => value.StartsWith("runtime-", StringComparison.Ordinal)))
		{
			var expected = dependency.StartsWith("runtime-aspnetcore-", StringComparison.Ordinal) ? $"aspnetcore-runtime-{dependency[18..]}" : $"dotnet-runtime-{dependency[8..]}";
			if(expected != runtime)
				throw new ContainerizationException(2, Properties.Resources.ApplicationBuilder_10_Message);
		}

		return runtime;
	}

	#endregion

	#region 私有方法
	private static void ResolveEntry(PackageReader.Descriptor package, ServiceBuildContext source)
	{
		var services = package.Texts.Where(pair => pair.Key.EndsWith(".service", StringComparison.Ordinal)).ToArray();
		if(services.Length != 1)
			throw new ContainerizationException(2, Properties.Resources.ApplicationBuilder_3_Message);

		var lines = services[0].Value.Split('\n').Select(line => line.Trim()).Where(line => line.Contains('=')).Select(line => line.Split('=', 2)).ToArray();
		var entries = lines.Where(pair => pair[0] == "ExecStart").ToArray();
		var directories = lines.Where(pair => pair[0] == "WorkingDirectory").ToArray();

		if(entries.Length != 1 || directories.Length != 1)
			throw new ContainerizationException(2, Properties.Resources.ApplicationBuilder_4_Message);

		var listeners = ApplicationHealth.ParseListeners(package.ListenerAddresses, package.Name);
		var listening = string.Join(';', listeners.Select(listener => listener.GetLeftPart(UriPartial.Authority)));
		var entry = listeners.Length == 0 ? entries[0][1] : entries[0][1].Replace(package.ListenerAddresses, listening, StringComparison.Ordinal);
		var command = listening.Length == 0 ? entry : entry.Replace(listening, "", StringComparison.Ordinal);

		if(command.IndexOfAny(['$', '%', '\\', '\'', '"', ';', '|', '&', '\n']) >= 0)
			throw new ContainerizationException(2, Properties.Resources.ApplicationBuilder_4_Message);

		var arguments = entry.Split(' ', StringSplitOptions.RemoveEmptyEntries);
		if(arguments.Length == 0 || arguments[0].EndsWith("sh", StringComparison.Ordinal))
			throw new ContainerizationException(2, Properties.Resources.ApplicationBuilder_5_Message);

		source.Plan.Entrypoint = arguments;
		source.Plan.WorkingDirectory = Files.NormalizeLinuxPath(directories[0][1]);

		foreach(var environment in lines.Where(pair => pair[0] == "Environment"))
		{
			var pair = environment[1].Split('=', 2);
			if(pair.Length != 2 || pair[0].Any(char.IsWhiteSpace))
				throw new ContainerizationException(2, Properties.Resources.ApplicationBuilder_6_Message);

			source.Environment.TryAdd(pair[0], pair[1]);
		}

		var signal = lines.FirstOrDefault(pair => pair[0] == "KillSignal");
		if(signal != null)
			source.Plan.StopSignal = signal[1];

		ApplicationHealth.Configure(package, source);
	}

	#endregion
}
