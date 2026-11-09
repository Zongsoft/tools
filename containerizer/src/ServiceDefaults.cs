/*
 *   _____                                ______
 *  /_   /  ____  ____  ____  _________  / __/ /_
 *    / /  / __ \/ __ \/ __ \/ ___/ __ \/ /_/ __/
 *   / /__/ /_/ / / / / /_/ /\_ \/ /_/ / __/ /_
 *  /____/\____/_/ /_/\__  /____/\____/_/  \__/
 *                   /____/
 *
 * Authors:
 *   钟峰(Popeye Zhong) <zongsoft@qq.com>
 *
 * Copyright (c) Zongsoft Studio. All rights reserved.
 * The above copyright notice and this permission notice shall be included in all
 * AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
 *
 *
 * Licensed under the MIT License (see LICENSE).
 */

using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Collections.Generic;

using Zongsoft.Text.Templating;
using Zongsoft.Configuration.Profiles;
using Zongsoft.Tools.Containerizer.Protocol;

namespace Zongsoft.Tools.Containerizer;

internal sealed class ServiceDefaults
{
	private readonly Dictionary<string, ContainerManifest.Component> _components = new(StringComparer.OrdinalIgnoreCase);

	public static ServiceDefaults Read(string path)
	{
		var defaults = new ServiceDefaults();
		if(!File.Exists(path))
			return defaults;

		ContainerManifest.ValidateLines(path, new HashSet<string>(StringComparer.OrdinalIgnoreCase));
		var profile = Profile.Load(path, new ProfileOptions { Directives = { ProfileDirectiveOptions.Import(ProfileDirectiveBehavior.Suppress) } });

		if(profile.Entries.Count > 0)
			throw new ContainerizationException(2, string.Format(Properties.Resources.Settings_File_Message, path));

		foreach(var section in profile.Sections)
		{
			ContainerManifest.ValidateIdentity(section.Name);

			if(section.Sections.Count > 0 || defaults._components.ContainsKey(section.Name))
				throw new ContainerizationException(2, string.Format(Properties.Resources.Settings_File_Message, path));

			var component = new ContainerManifest.Component { Name = section.Name };

			foreach(var entry in section.Entries)
			{
				try
				{
					switch(entry.Name.ToLowerInvariant())
					{
						case "tag":
							ImageReference.ValidateTag(entry.Value);
							break;
						case "repository":
							ImageReference.ValidateRepository(entry.Value);
							break;
						case "template":
							if(string.IsNullOrWhiteSpace(entry.Value))
								throw new ContainerizationException(2, string.Format(Properties.Resources.Manifest_8_Message, component.Name, "template"));
							break;
						case "settings":
							ServiceSettings.Parse(entry.Value);
							break;
						default:
							throw new ContainerizationException(2, string.Format(Properties.Resources.Manifest_UnknownComponentKey_Message, entry.Name));
					}

					component[entry.Name] = entry.Value;
				}
				catch(Exception exception) when(exception is ContainerizationException or ArgumentException)
				{
					throw new ContainerizationException(2, string.Format(Properties.Resources.Manifest_16_Message, path, entry.LineNumber + 1, exception.Message));
				}
			}

			defaults._components.Add(component.Name, component);
		}

		return defaults;
	}

	public string SelectTag(ContainerManifest.Component component) => component["tag"] ?? _components.GetValueOrDefault(component.Name)?["tag"] ?? "latest";
	public string SelectRepository(ContainerManifest.Component component, string fallback) => component["repository"] ?? _components.GetValueOrDefault(component.Name)?["repository"] ?? fallback;

	public void Apply(ContainerManifest.Component component, TemplateEvaluator evaluator)
	{
		if(component.IsApplication)
			return;

		component["tag"] = this.SelectTag(component);

		if(_components.TryGetValue(component.Name, out var defaults))
		{
			if(component["template"] == null && defaults["template"] != null)
			{
				var template = ContainerManifest.Evaluate(defaults["template"], evaluator);
				if(string.IsNullOrWhiteSpace(template))
					throw new ContainerizationException(2, string.Format(Properties.Resources.Manifest_8_Message, component.Name, "template"));

				component["template"] = template;
			}

			component["repository"] ??= defaults["repository"];
			var settings = ServiceSettings.Parse(defaults["settings"]);

			foreach(var pair in component.Settings)
				settings[pair.Key] = pair.Value;

			if(settings.Count > 0)
				component["settings"] = ServiceSettings.Format(settings);
		}
	}

	public static string Prepare(ContainerManifest manifest, string directory)
	{
		if(manifest.InputPath != null)
			return null;

		var destination = Path.Combine(manifest["output"], ".settings");
		var existing = Read(destination);
		var lines = File.Exists(destination) ? File.ReadAllLines(destination).ToList() : [];
		var changed = false;

		foreach(var component in manifest.Components.Where(component => !component.IsApplication))
		{
			if(existing._components.TryGetValue(component.Name, out var defaults) && defaults["tag"] != null)
				continue;

			var index = lines.FindIndex(line => line.Trim().Equals($"[{component.Name}]", StringComparison.OrdinalIgnoreCase));

			if(index < 0)
			{
				if(lines.Count > 0 && lines[^1].Length > 0)
					lines.Add("");

				lines.Add($"[{component.Name}]");
				index = lines.Count - 1;
			}

			lines.Insert(index + 1, $"tag={component["tag"]}");
			changed = true;
		}

		if(!changed)
			return null;

		var path = Path.Combine(directory, "defaults.settings");
		File.WriteAllText(path, $"{string.Join("\r\n", lines)}\r\n", new UTF8Encoding(false));
		return path;
	}
}
