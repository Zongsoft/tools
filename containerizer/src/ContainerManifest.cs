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
using System.Globalization;
using System.Collections.Generic;
using System.Text.RegularExpressions;

using Zongsoft.Components;
using Zongsoft.Configuration;
using Zongsoft.Configuration.Profiles;
using Zongsoft.Tools.Containerizer.Protocol;

namespace Zongsoft.Tools.Containerizer;

internal sealed partial class ContainerManifest
{
	#region 静态字段
	internal static readonly string[] RootKeys = ["name", "tag", "version", "engine", "stage", "distribution", "architecture", "bootstrap", "imaging", "source", "output", "title", "description"];
	private static readonly string[] _componentKeys = ["package", "tag", "timestamp", "repository", "size", "digest", "imaging", "settings", "dependences", "template", "identity"];
	#endregion

	#region 公共属性
	public Dictionary<string, string> Root { get; } = new(StringComparer.OrdinalIgnoreCase);
	public Dictionary<string, string> Variables { get; } = new(StringComparer.OrdinalIgnoreCase);
	public List<Component> Components { get; } = [];
	public List<string> Migrations { get; } = [];
	public string Input { get; set; }
	public string SourceOrigin { get; set; }
	public bool IsGenerated => this["stage"] == "complete";
	public bool IsPlanning { get; private set; }
	public string InputHash { get; private set; }
	public ServiceDefaults Defaults { get; private set; } = new();
	public string this[string key] { get => this.Root.GetValueOrDefault(key); set => this.Root[key] = value; }
	public string Stem => $"{this["name"]}{(string.IsNullOrEmpty(this["tag"]) ? "" : $"-{this["tag"]}")}@{this["version"]}";
	public string ReleaseName => $"{this.Stem}-{this["architecture"]}";
	public string ManifestPath => Path.Combine(this["output"], $"{this.ReleaseName}.container");
	#endregion

	#region 公共方法
	public string Resolve(string path) => Path.GetFullPath(path, this["source"]);

	public static ContainerManifest Read(string path)
	{
		var result = new ContainerManifest { Input = Path.GetFullPath(path), InputHash = Files.Hash(path) };
		var declarations = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
		ValidateLines(path, declarations);

		var migrations = new SortedDictionary<int, string>();
		var profile = Profile.Load(path, new ProfileOptions
		{
			ImportBehavior = ProfileDirectiveBehavior.Existed,
			Importing = context => ValidateLines(context.FilePath, declarations),
		});

		foreach(var entry in profile.Entries)
		{
			if(entry.Name.StartsWith("migration#", StringComparison.OrdinalIgnoreCase))
			{
				if(!int.TryParse(entry.Name.AsSpan(10), NumberStyles.None, CultureInfo.InvariantCulture, out var number) || number < 1 || !migrations.TryAdd(number, entry.Value))
					throw At(entry, Properties.Resources.Manifest_MigrationSequence_Message);
			}
			else if(RootKeys.Contains(entry.Name, StringComparer.OrdinalIgnoreCase))
			{
				result[entry.Name] = entry.Value;
				if(entry.Name.Equals("source", StringComparison.OrdinalIgnoreCase))
					result.SourceOrigin = Path.GetDirectoryName(entry.Profile.FilePath);
			}
			else
				throw At(entry, string.Format(Properties.Resources.Manifest_UnknownRootKey_Message, entry.Name));
		}

		result.Migrations.AddRange(migrations.Values);

		foreach(var section in profile.Sections)
		{
			Identity(section.Name);

			if(section.Sections.Count > 0)
				throw At(section, Properties.Resources.Manifest_NestedComponent_Message);

			var component = new Component { Name = section.Name };
			foreach(var entry in section.Entries)
			{
				if(!_componentKeys.Contains(entry.Name, StringComparer.OrdinalIgnoreCase) && !entry.Name.StartsWith("environment!", StringComparison.OrdinalIgnoreCase))
					throw At(entry, string.Format(Properties.Resources.Manifest_UnknownComponentKey_Message, entry.Name));
				if(entry.Name.StartsWith("environment!", StringComparison.OrdinalIgnoreCase) && !EnvironmentNameRegex().IsMatch(entry.Name[12..]))
					throw At(entry, Properties.Resources.Manifest_EnvironmentName_Message);

				component.Values.Add(entry.Name, entry.Value);
			}

			result.Components.Add(component);
		}

		if(result["stage"] is not (null or "plan" or "complete"))
			throw new ContainerizationException(2, Properties.Resources.Manifest_3_Message);

		foreach(var component in result.Components)
		{
			if(component["identity"] != null && component["identity"] != result.ImageIdentity(component))
			{
				string[] imageMetadata = ["digest", "timestamp", "size", "identity"];
				foreach(var key in imageMetadata)
					component.Values.Remove(key);
			}
		}

		return result;
	}

	public static ContainerManifest From(CommandContext context, bool planning = false, bool make = false)
	{
		var initial = Utility.CreateVariables(context);
		var options = context.Options.ToDictionary(item => item.Key, item => item.Value?.ToString(), StringComparer.OrdinalIgnoreCase);
		var arguments = context.Arguments.ToArray();
		var start = options.TryGetValue("source", out var source) ? Path.GetFullPath(Evaluate(source, initial)) : Environment.CurrentDirectory;
		var manifests = arguments.Where(argument => argument.EndsWith(".container", StringComparison.OrdinalIgnoreCase)).ToArray();

		if(manifests.Length > 0 && (manifests.Length != 1 || arguments.Length != 1))
			throw new ContainerizationException(2, Properties.Resources.Manifest_1_Message);

		if(make && manifests.Length != 1)
			throw new ContainerizationException(2, Properties.Resources.Make_Input);
		if(manifests.Length == 1 && options.Keys.Any(key => key is not ("version" or "source" or "output" or "engine")))
			throw new ContainerizationException(2, Properties.Resources.Make_Options);

		var result = manifests.Length == 1 ? Read(Path.GetFullPath(manifests[0], start)) : new ContainerManifest();
		result.IsPlanning = planning;
		if(!options.ContainsKey("source") && !string.IsNullOrEmpty(result["source"]))
			start = Path.GetFullPath(result.IsGenerated ? result["source"] : Evaluate(result["source"], initial), result.SourceOrigin ?? Path.GetDirectoryName(result.Input));
		if(!Directory.Exists(start))
			throw new ContainerizationException(2, string.Format(Properties.Resources.Manifest_2_Message, start));

		var variables = Utility.CreateVariables(context, start);
		variables["source"] = start;

		foreach(var key in RootKeys)
		{
			if(options.TryGetValue(key, out var value))
				result[key] = value;
			if(result.Root.ContainsKey(key) && (!result.IsGenerated || options.ContainsKey(key)))
				result[key] = Evaluate(result[key], variables);
		}

		result["source"] = start;
		result["output"] = Path.GetFullPath(string.IsNullOrEmpty(result["output"]) ? "." : result["output"], start);
		result["architecture"] = string.IsNullOrEmpty(result["architecture"]) ? "x64" : result["architecture"].ToLowerInvariant();
		result["distribution"] = Distribution.Normalize(result["distribution"]);

		string[] acquisitionOptions = ["bootstrap", "imaging", "engine"];
		foreach(var key in acquisitionOptions)
			result[key] = string.IsNullOrEmpty(result[key]) ? key == "engine" ? "auto" : "offline" : result[key].ToLowerInvariant();

		Identity(result["name"]);

		if(!string.IsNullOrEmpty(result["tag"]))
			Identity(result["tag"]);
		if(result["architecture"] is not ("x64" or "arm64") ||
			result["imaging"] is not ("online" or "offline") ||
			result["bootstrap"] is not ("online" or "offline") ||
			result["engine"] is not ("auto" or "docker" or "podman"))
			throw new ContainerizationException(2, Properties.Resources.Manifest_3_Message);

		var automatic = string.IsNullOrEmpty(result["version"]);
		result["version"] = automatic ? DateVersion(DateTime.Today) : result["version"];
		var releaseVersion = VersionNumber(result["version"]);

		for(int suffix = 1; result.ReleaseExists(); suffix++)
		{
			if(!automatic)
				throw new ContainerizationException(2, Properties.Resources.Manifest_4_Message);

			result["version"] = new Versioning.Version.Number(releaseVersion.Major, releaseVersion.Minor, releaseVersion.Patch, checked((ushort)suffix)).ToString();
		}

		foreach(var component in result.Components)
		{
			if(!result.IsGenerated)
			{
				foreach(var key in component.Values.Keys.ToArray())
				{
					if(key != "settings" && !key.StartsWith("environment!", StringComparison.OrdinalIgnoreCase))
						component[key] = Evaluate(component[key], variables, allowEscapes: true);
				}
			}
		}

		for(int index = 0; index < result.Migrations.Count; index++)
			result.Migrations[index] = result.Resolve(result.IsGenerated ? result.Migrations[index] : Evaluate(result.Migrations[index], variables));

		if(manifests.Length == 0)
		{
			foreach(var argument in arguments)
			{
				var value = Evaluate(argument, variables);
				var parts = value.Split('@', 2);
				Component component;

				if(!value.Contains('/') && !value.Contains('\\') && TemplateCatalog.Contains(parts[0]))
				{
					component = new() { Name = parts[0].ToLowerInvariant() };
					if(parts.Length == 2)
						component["tag"] = parts[1];
				}
				else
				{
					var input = result.Resolve(value);
					var package = PackageReader.Select(input, result["name"], result["distribution"], result["architecture"]);
					component = new() { Name = PackageReader.Read(package).Name };
					component["package"] = package;

				}

				result.Components.Add(component);
			}
		}

		if(options.TryGetValue("migration", out var migration))
		{
			result.Migrations.Clear();
			migration = Evaluate(migration, variables);

			if(!string.IsNullOrEmpty(migration))
				result.Migrations.AddRange(MigrationInput.Select(result.Resolve(migration), result["name"], result["architecture"]));
		}

		foreach(var pair in variables)
			result.Variables[pair.Key] = pair.Value;
		foreach(var pair in result.Root)
			result.Variables[pair.Key] = pair.Value;

		result.Validate();
		result.Defaults = result.Input != null ? new ServiceDefaults() : ServiceDefaults.Read(Path.Combine(result["output"], ".settings"));
		foreach(var component in result.Components)
			result.Defaults.Apply(component);
		return result;
	}

	public void Validate()
	{
		if(this.Components.Count == 0)
			throw new ContainerizationException(2, Properties.Resources.Manifest_5_Message);

		var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
		foreach(var component in this.Components)
		{
			Identity(component.Name);

			if(!names.Add(component.Name))
				throw new ContainerizationException(2, string.Format(Properties.Resources.Manifest_6_Message, component.Name));
			string[] prohibited = component.IsApplication ? ["tag", "timestamp", "repository", "size", "digest", "imaging", "template", "settings", "identity"] : ["dependences"];
			foreach(var key in prohibited)
			{
				if(component.Values.ContainsKey(key))
					throw new ContainerizationException(2, string.Format(Properties.Resources.Manifest_8_Message, component.Name, key));
			}

			foreach(var key in component.Values.Keys)
			{
				if(!_componentKeys.Contains(key, StringComparer.OrdinalIgnoreCase) && !key.StartsWith("environment!", StringComparison.OrdinalIgnoreCase))
					throw new ContainerizationException(2, string.Format(Properties.Resources.Manifest_8_Message, component.Name, key));
			}

			if(component.IsApplication)
			{
				component["package"] = this.Resolve(component["package"]);
				var metadata = PackageReader.Read(component["package"]);

				if(metadata.Architecture != this["architecture"])
					throw new ContainerizationException(2, string.Format(Properties.Resources.Manifest_9_Message, component.Name));
			}
			else
			{
				if(component["digest"] != null && !ContainerEngine.TryDigest(component["digest"], out _))
					throw new ContainerizationException(2, string.Format(Properties.Resources.Manifest_10_Message, component.Name));
				if(component["tag"] != null)
					ContainerEngine.ValidateTag(component["tag"]);
				if(component["repository"] != null)
					ContainerEngine.ValidateRepository(component["repository"]);
				if(component["imaging"] is not (null or "online" or "offline"))
					throw new ContainerizationException(2, Properties.Resources.NodeBuilder_2_Message);
				if(component["size"] != null && (!long.TryParse(component["size"], NumberStyles.None, CultureInfo.InvariantCulture, out var size) || size < 0) ||
					component["timestamp"] != null && !DateTimeOffset.TryParse(component["timestamp"], CultureInfo.InvariantCulture, DateTimeStyles.None, out _))
					throw new ContainerizationException(2, string.Format(Properties.Resources.Manifest_8_Message, component.Name, "size/timestamp"));
				if(!TemplateCatalog.Contains(component["template"] ?? component.Name) && string.IsNullOrEmpty(component["template"]))
					throw new ContainerizationException(2, string.Format(Properties.Resources.Manifest_8_Message, component.Name, "template"));
			}
		}

		var additions = new List<Component>();
		foreach(var component in this.Components.Where(item => item.IsApplication))
		{
			var dependencies = (component["dependences"] ?? "").Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

			for(int index = 0; index < dependencies.Length; index++)
			{
				var dependency = dependencies[index];
				if(dependency != "nginx" && !dependency.StartsWith("nginx@", StringComparison.OrdinalIgnoreCase))
				{
					if(dependency.StartsWith("runtime-", StringComparison.Ordinal))
						continue;

					throw new ContainerizationException(2, Properties.Resources.Manifest_20_Message);
				}

				var version = dependencies[index] == "nginx" ? null : dependencies[index][6..];
				var existing = this.Components.Concat(additions).FirstOrDefault(item => item.Name.Equals("nginx", StringComparison.OrdinalIgnoreCase));

				if(existing == null)
				{
					existing = new() { Name = "nginx" };
					if(version != null)
					{
						ContainerEngine.ValidateTag(version);
						existing["tag"] = version;
					}

					additions.Add(existing);
				}
				else if(existing.IsApplication ||
					!string.Equals(existing["template"] ?? existing.Name, "nginx", StringComparison.OrdinalIgnoreCase) ||
					version != null && existing["tag"] != version)
					throw new ContainerizationException(2, Properties.Resources.Manifest_11_Message);

				dependencies[index] = "nginx";
			}

			if(dependencies.Length > 0)
				component["dependences"] = string.Join(';', dependencies);
		}

		this.Components.AddRange(additions);
		MigrationInput.Validate(this.Migrations, this["architecture"]);
	}

	public string Prepare(string directory, bool planning = false)
	{
		var path = Path.Combine(directory, $"{this.ReleaseName}.container");
		Files.Write(path, this.Serialize(planning));
		return path;
	}

	#endregion

	#region 内部方法
	internal string ResolveValue(string value) => this.IsGenerated || this.IsPlanning ? value ?? "" : Evaluate(value, this.Variables, allowEscapes: true);
	internal static bool HasVariables(string value) => Expressions().IsMatch(value ?? "");
	internal static string EscapeVariables(string value) => Expressions().Replace(value ?? "", match => match.Value[0] == '$' ? $"${match.Value}" : $"%{match.Value}%");
	[GeneratedRegex(@"\$\([\w.\[\]-]+\)|%[\w.\[\]-]+%")]
	private static partial Regex Expressions();

	internal static string DateVersion(DateTime date) => new Versioning.Version.Number((ushort)(date.Year % 1000), (ushort)date.Month, (ushort)date.Day).ToString();

	internal static string Evaluate(string value, IReadOnlyDictionary<string, string> variables, bool allowEscapes = false)
	{
		var result = VariableEvaluator.Evaluate(value, variables, allowEscapes: allowEscapes);
		return result.Succeed ? result.Value : throw new ContainerizationException(2, string.Format(Properties.Resources.Manifest_13_Message, result.Variable, result.Reason));
	}

	internal static void Identity(string value)
	{
		if(string.IsNullOrEmpty(value) || !IdentityRegex().IsMatch(value) || value.Contains("..", StringComparison.Ordinal))
			throw new ContainerizationException(2, Properties.Resources.Manifest_14_Message);
	}

	internal static Versioning.Version.Number VersionNumber(string value) => Versioning.Version.Number.TryParse(value, out var version) && !version.IsZero ? version : throw new ContainerizationException(2, Properties.Resources.Manifest_15_Message);
	#endregion

	#region 私有方法
	private string Serialize(bool planning)
	{
		var assembly = typeof(ContainerManifest).Assembly.GetName();
		var version = (Versioning.Version.Number)assembly.Version;
		var text = new StringBuilder("# Generated by ").Append(assembly.Name).Append('@').Append(version.ToString()).Append("\r\n\r\n");

		foreach(var key in RootKeys)
		{
			var value = key == "stage" ? planning ? "plan" : "complete" : key == "source" ? Path.GetRelativePath(this["output"], this["source"]) : key == "output" ? Path.GetRelativePath(this["source"], this["output"]) : this[key];
			if(!string.IsNullOrEmpty(value))
				text.Append(key).Append('=').AppendLine(value);
		}

		for(int index = 0; index < this.Migrations.Count; index++)
			text.Append("migration#").Append(index + 1).Append('=').AppendLine(Path.GetRelativePath(this["source"], this.Migrations[index]));

		foreach(var component in this.Components)
		{
			text.AppendLine().Append('[').Append(component.Name).AppendLine("]");

			if(component["digest"] != null)
				component["identity"] = this.ImageIdentity(component);

			foreach(var pair in component.Values)
			{
				var value = pair.Key == "package" || pair.Key == "template" && !TemplateCatalog.Contains(pair.Value) ? Path.GetRelativePath(this["source"], this.Resolve(pair.Value)) : pair.Value;
				if(value != null || pair.Key == "settings" || pair.Key.StartsWith("environment!", StringComparison.OrdinalIgnoreCase))
					text.Append(pair.Key).Append('=').AppendLine(value);
			}
		}

		return text.ToString().Replace("\r\n", "\n", StringComparison.Ordinal).Replace("\n", "\r\n", StringComparison.Ordinal);
	}

	private string ImageIdentity(Component component) => Files.HashText(string.Join("\n", component["repository"] ?? "", component["tag"] ?? "", "linux", this["architecture"] ?? ""));

	private bool ReleaseExists() => File.Exists(Path.Combine(this["output"], $"{this.ReleaseName}.tar.gz")) ||
		File.Exists(this.ManifestPath) && (this.IsPlanning || this.Input == null || !BuildStorage.SamePath(this.Input, this.ManifestPath));
	private static ContainerizationException At(ProfileItem item, string message) => new(2, string.Format(Properties.Resources.Manifest_16_Message, item.Profile.FilePath, item.LineNumber + 1, message));

	internal static void ValidateLines(string path, HashSet<string> declarations)
	{
		var section = "";
		var number = 0;

		foreach(var line in File.ReadLines(path))
		{
			number++;
			var text = line.Trim();

			if(text.Length == 0 || text[0] is '#' or ';')
				continue;

			if(text.StartsWith('['))
			{
				if(!text.EndsWith(']'))
					throw new ContainerizationException(2, string.Format(Properties.Resources.Manifest_17_Message, path, number));

				section = text[1..^1].Trim();
				if(section.Length > 0)
					Identity(section);
			}
			else
			{
				var key = text.Split('=', 2)[0].Trim();
				if(!declarations.Add($"{section}/{key}"))
					throw new ContainerizationException(2, string.Format(Properties.Resources.Manifest_18_Message, path, number, key));
			}
		}
	}

	[GeneratedRegex(@"^[A-Za-z_][A-Za-z0-9_]*$")]
	private static partial Regex EnvironmentNameRegex();

	[GeneratedRegex(@"^[A-Za-z0-9][A-Za-z0-9_.-]*$")]
	private static partial Regex IdentityRegex();
	#endregion

	#region 嵌套类型
	internal sealed class Component
	{
		#region 公共属性
		public string Name { get; set; }
		public Dictionary<string, string> Values { get; } = new(StringComparer.OrdinalIgnoreCase);
		public string this[string key] { get => this.Values.GetValueOrDefault(key); set => this.Values[key] = value; }
		public IReadOnlyDictionary<string, string> Settings => ServiceSettings.Parse(this["settings"]);
		public bool IsApplication => !string.IsNullOrEmpty(this["package"]);
		#endregion
	}
	#endregion
}
