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
using System.Collections.Generic;

using Zongsoft.Components;
using Zongsoft.Tools.Containerizer.Protocol;

namespace Zongsoft.Tools.Containerizer;

internal static class ManifestFactory
{
	#region 公共方法
	public static ContainerManifest Create(CommandContext context, bool planning = false, bool make = false)
	{
		var initial = Utility.CreateVariables(context);
		var options = context.Options.ToDictionary(item => item.Key, item => item.Value?.ToString(), StringComparer.OrdinalIgnoreCase);
		var arguments = context.Arguments.ToArray();
		var start = options.TryGetValue("source", out var source) ? Path.GetFullPath(ContainerManifest.Evaluate(source, initial)) : Environment.CurrentDirectory;
		var manifests = arguments.Where(argument => argument.EndsWith(".container", StringComparison.OrdinalIgnoreCase)).ToArray();

		if(manifests.Length > 0 && (manifests.Length != 1 || arguments.Length != 1))
			throw new ContainerizationException(2, Properties.Resources.Manifest_1_Message);

		if(make && manifests.Length != 1)
			throw new ContainerizationException(2, Properties.Resources.Make_Input_Message);
		if(manifests.Length == 1 && options.Keys.Any(key => key is not ("version" or "source" or "output" or "engine" or "refresh")))
			throw new ContainerizationException(2, Properties.Resources.Make_Options_Message);

		var result = manifests.Length == 1 ? ContainerManifest.Read(Path.GetFullPath(manifests[0], start), planning) : new ContainerManifest(planning);

		if(!options.ContainsKey("source") && !string.IsNullOrEmpty(result["source"]))
			start = Path.GetFullPath(result.IsComplete ? result["source"] : ContainerManifest.Evaluate(result["source"], initial), result.SourceBaseDirectory ?? Path.GetDirectoryName(result.InputPath));
		if(!Directory.Exists(start))
			throw new ContainerizationException(2, string.Format(Properties.Resources.Manifest_2_Message, start));

		var variables = Utility.CreateVariables(context, start);
		variables["source"] = start;

		foreach(var key in ContainerManifest.RootKeys)
		{
			if(options.TryGetValue(key, out var value))
				result[key] = value;
			if(result.Root.ContainsKey(key) && (!result.IsComplete || options.ContainsKey(key)))
				result[key] = ContainerManifest.Evaluate(result[key], variables);
		}

		result["source"] = start;
		result["output"] = Path.GetFullPath(string.IsNullOrEmpty(result["output"]) ? "." : result["output"], start);
		result["architecture"] = string.IsNullOrEmpty(result["architecture"]) ? "x64" : result["architecture"].ToLowerInvariant();
		result["distribution"] = Distribution.Normalize(result["distribution"]);

		string[] acquisitionOptions = ["bootstrap", "imaging", "engine"];
		foreach(var key in acquisitionOptions)
			result[key] = string.IsNullOrEmpty(result[key]) ? key == "engine" ? ContainerEngine.AUTO : "offline" : result[key].ToLowerInvariant();

		ContainerManifest.ValidateIdentity(result["name"]);

		if(!string.IsNullOrEmpty(result["tag"]))
			ContainerManifest.ValidateIdentity(result["tag"]);

		if(result["architecture"] is not ("x64" or "arm64") ||
		   result["imaging"] is not ("online" or "offline") ||
		   result["bootstrap"] is not ("online" or "offline") ||
		   result["engine"] is not (ContainerEngine.AUTO or ContainerEngine.DOCKER or ContainerEngine.PODMAN))
			throw new ContainerizationException(2, Properties.Resources.Manifest_3_Message);

		var automatic = string.IsNullOrEmpty(result["version"]);
		result["version"] = automatic ? ContainerManifest.GetDateVersion(DateTime.Today) : result["version"];
		var releaseVersion = ContainerManifest.ParseVersionNumber(result["version"]);

		for(int suffix = 1; ReleaseExists(result); suffix++)
		{
			if(!automatic)
				throw new ContainerizationException(2, Properties.Resources.Manifest_4_Message);

			result["version"] = new Versioning.Version.Number(releaseVersion.Major, releaseVersion.Minor, releaseVersion.Patch, checked((ushort)suffix)).ToString();
		}

		foreach(var component in result.Components)
		{
			if(!result.IsComplete)
			{
				foreach(var key in component.Values.Keys.ToArray())
				{
					if(key != "settings" && !key.StartsWith("environment!", StringComparison.OrdinalIgnoreCase))
						component[key] = ContainerManifest.Evaluate(component[key], variables, allowEscapes: true);
				}
			}
		}

		for(int index = 0; index < result.Migrations.Count; index++)
			result.Migrations[index] = result.ResolvePath(result.IsComplete ? result.Migrations[index] : ContainerManifest.Evaluate(result.Migrations[index], variables));

		if(manifests.Length == 0)
		{
			foreach(var argument in arguments)
			{
				var value = ContainerManifest.Evaluate(argument, variables);
				var parts = value.Split('@', 2);
				ContainerManifest.Component component;

				if(!value.Contains('/') && !value.Contains('\\') && TemplateCatalog.Contains(parts[0]))
				{
					component = new() { Name = parts[0].ToLowerInvariant() };
					if(parts.Length == 2)
						component["tag"] = parts[1];
				}
				else
				{
					var input = result.ResolvePath(value);
					var package = PackageReader.Select(input, result["name"], result["distribution"], result["architecture"]);
					component = new(package);
				}

				result.Components.Add(component);
			}
		}

		if(options.TryGetValue("migration", out var migration))
		{
			result.Migrations.Clear();
			migration = ContainerManifest.Evaluate(migration, variables);

			if(!string.IsNullOrEmpty(migration))
				result.Migrations.AddRange(MigrationInput.Select(result.ResolvePath(migration), result["name"], result["architecture"]));
		}

		foreach(var pair in variables)
			result.Variables[pair.Key] = pair.Value;
		foreach(var pair in result.Root)
			result.Variables[pair.Key] = pair.Value;

		result.Normalize();
		result.ApplyDefaults(result.InputPath != null ? new ServiceDefaults() : ServiceDefaults.Read(Path.Combine(result["output"], ".settings")));

		return result;
	}
	#endregion

	#region 私有方法
	private static bool ReleaseExists(ContainerManifest manifest) =>
		File.Exists(Path.Combine(manifest["output"], $"{manifest.ReleaseName}.tar.gz")) ||
		File.Exists(manifest.ManifestPath) &&
		(manifest.IsPlanning || manifest.InputPath == null || !BuildStorage.IsSamePath(manifest.InputPath, manifest.ManifestPath));
	#endregion
}
