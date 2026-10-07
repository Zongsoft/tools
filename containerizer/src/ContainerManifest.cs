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

using Zongsoft.Configuration.Profiles;
using Zongsoft.Tools.Containerizer.Protocol;

namespace Zongsoft.Tools.Containerizer;

internal sealed partial class ContainerManifest(bool planning = false)
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
	public string InputPath { get; private set; }
	public string SourceBaseDirectory { get; private set; }
	public bool IsComplete => this["stage"] == "complete";
	public bool IsPlanning { get; } = planning;
	public string InputHash { get; private set; }
	public ServiceDefaults Defaults { get; private set; } = new();
	public string this[string key] { get => this.Root.GetValueOrDefault(key); set => this.Root[key] = value; }
	public string Stem => $"{this["name"]}{(string.IsNullOrEmpty(this["tag"]) ? "" : $"-{this["tag"]}")}@{this["version"]}";
	public string ReleaseName => $"{this.Stem}-{this["architecture"]}";
	public string ManifestPath => Path.Combine(this["output"], $"{this.ReleaseName}.container");
	#endregion

	#region 公共方法
	public string ResolvePath(string path) => Path.GetFullPath(path, this["source"]);

	public static ContainerManifest Read(string path, bool planning = false)
	{
		var result = new ContainerManifest(planning) { InputPath = Path.GetFullPath(path), InputHash = Files.Hash(path) };
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
					throw CreateException(entry, Properties.Resources.Manifest_MigrationSequence_Message);
			}
			else if(RootKeys.Contains(entry.Name, StringComparer.OrdinalIgnoreCase))
			{
				result[entry.Name] = entry.Value;
				if(entry.Name.Equals("source", StringComparison.OrdinalIgnoreCase))
					result.SourceBaseDirectory = Path.GetDirectoryName(entry.Profile.FilePath);
			}
			else
				throw CreateException(entry, string.Format(Properties.Resources.Manifest_UnknownRootKey_Message, entry.Name));
		}

		result.Migrations.AddRange(migrations.Values);

		foreach(var section in profile.Sections)
		{
			ValidateIdentity(section.Name);

			if(section.Sections.Count > 0)
				throw CreateException(section, Properties.Resources.Manifest_NestedComponent_Message);

			var component = new Component { Name = section.Name };
			foreach(var entry in section.Entries)
			{
				if(!IsComponentKey(entry.Name))
					throw CreateException(entry, string.Format(Properties.Resources.Manifest_UnknownComponentKey_Message, entry.Name));
				if(entry.Name.StartsWith("environment!", StringComparison.OrdinalIgnoreCase) && !GetEnvironmentNameRegex().IsMatch(entry.Name[12..]))
					throw CreateException(entry, Properties.Resources.Manifest_EnvironmentName_Message);

				component.Values.Add(entry.Name, entry.Value);
			}

			result.Components.Add(component);
		}

		if(result["stage"] is not (null or "plan" or "complete"))
			throw new ContainerizationException(2, Properties.Resources.Manifest_3_Message);

		foreach(var component in result.Components)
		{
			if(component["identity"] != null && component["identity"] != result.GetImageIdentity(component))
			{
				string[] imageMetadata = ["digest", "timestamp", "size", "identity"];
				foreach(var key in imageMetadata)
					component.Values.Remove(key);
			}
		}

		return result;
	}

	public void ApplyDefaults(ServiceDefaults defaults)
	{
		this.Defaults = defaults ?? throw new ArgumentNullException(nameof(defaults));
		foreach(var component in this.Components)
			this.Defaults.Apply(component);
	}

	public void Normalize()
	{
		if(this.Components.Count == 0)
			throw new ContainerizationException(2, Properties.Resources.Manifest_5_Message);

		var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

		foreach(var component in this.Components)
		{
			Validate(component, names);
			if(component.IsApplication)
				this.NormalizeApplication(component);
		}

		this.NormalizeDependencies();
		MigrationInput.Validate(this.Migrations, this["architecture"]);
	}

	public string Prepare(string directory, bool planning = false)
	{
		foreach(var component in this.Components)
		{
			if(component["digest"] != null)
				component["identity"] = this.GetImageIdentity(component);
		}

		var path = Path.Combine(directory, $"{this.ReleaseName}.container");
		Files.Write(path, this.Serialize(planning));
		return path;
	}
	#endregion

	#region 内部方法
	internal string ResolveValue(string value) => this.IsComplete || this.IsPlanning ? value ?? "" : Evaluate(value, this.Variables, allowEscapes: true);
	internal static bool HasVariables(string value) => GetVariableExpressionRegex().IsMatch(value ?? "");
	internal static string EscapeVariables(string value) => GetVariableExpressionRegex().Replace(value ?? "", match => match.Value[0] == '$' ? $"${match.Value}" : $"%{match.Value}%");
	[GeneratedRegex(@"\$\([\w.\[\]-]+\)|%[\w.\[\]-]+%")]
	private static partial Regex GetVariableExpressionRegex();

	internal static string Evaluate(string value, IReadOnlyDictionary<string, string> variables, bool allowEscapes = false)
	{
		var result = VariableEvaluator.Evaluate(value, variables, allowEscapes: allowEscapes);
		return result.Succeed ? result.Value : throw new ContainerizationException(2, string.Format(Properties.Resources.Manifest_13_Message, result.Variable, result.Reason));
	}

	internal static void ValidateIdentity(string value)
	{
		if(string.IsNullOrEmpty(value) || !GetIdentityRegex().IsMatch(value) || value.Contains("..", StringComparison.Ordinal))
			throw new ContainerizationException(2, Properties.Resources.Manifest_14_Message);
	}

	internal static string GetDateVersion(DateTime date) => new Versioning.Version.Number((ushort)(date.Year % 1000), (ushort)date.Month, (ushort)date.Day).ToString();
	internal static Versioning.Version.Number ParseVersionNumber(string value) => Versioning.Version.Number.TryParse(value, out var version) && !version.IsZero ? version : throw new ContainerizationException(2, Properties.Resources.Manifest_15_Message);
	#endregion

	#region 私有方法
	private static void Validate(Component component, HashSet<string> names)
	{
		ValidateIdentity(component.Name);

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
			if(!IsComponentKey(key) || key.StartsWith("file!", StringComparison.OrdinalIgnoreCase) && (component["template"] ?? component.Name) != "nginx" || key.StartsWith("probe-host!", StringComparison.OrdinalIgnoreCase) && !component.IsApplication)
				throw new ContainerizationException(2, string.Format(Properties.Resources.Manifest_8_Message, component.Name, key));
		}

		if(!component.IsApplication)
		{
			if(component["digest"] != null && !ImageReference.TryParseDigest(component["digest"], out _))
				throw new ContainerizationException(2, string.Format(Properties.Resources.Manifest_10_Message, component.Name));
			if(component["tag"] != null)
				ImageReference.ValidateTag(component["tag"]);
			if(component["repository"] != null)
				ImageReference.ValidateRepository(component["repository"]);
			if(component["imaging"] is not (null or "online" or "offline"))
				throw new ContainerizationException(2, Properties.Resources.NodeBuilder_2_Message);
			if(component["size"] != null && (!long.TryParse(component["size"], NumberStyles.None, CultureInfo.InvariantCulture, out var size) || size < 0) ||
				component["timestamp"] != null && !DateTimeOffset.TryParse(component["timestamp"], CultureInfo.InvariantCulture, DateTimeStyles.None, out _))
				throw new ContainerizationException(2, string.Format(Properties.Resources.Manifest_8_Message, component.Name, "size/timestamp"));
			if(!TemplateCatalog.Contains(component["template"] ?? component.Name) && string.IsNullOrEmpty(component["template"]))
				throw new ContainerizationException(2, string.Format(Properties.Resources.Manifest_8_Message, component.Name, "template"));
		}
	}

	private void NormalizeApplication(Component component)
	{
		component["package"] = this.ResolvePath(component["package"]);
		var metadata = component.ReadPackage();

		if(metadata.Web != null && !(component["dependences"] ?? "").Split(';', StringSplitOptions.TrimEntries).Any(value => value == "nginx" || value.StartsWith("nginx@", StringComparison.Ordinal)))
			component["dependences"] = string.IsNullOrEmpty(component["dependences"]) ? "nginx" : component["dependences"] + ";nginx";

		if(metadata.Architecture != this["architecture"])
			throw new ContainerizationException(2, string.Format(Properties.Resources.Manifest_9_Message, component.Name));
	}

	private void NormalizeDependencies()
	{
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
				if(component.Package.Web == null)
					throw WebPackage.CreateException(component.Name, ".web/nginx/.bindings");

				var existing = this.Components.Concat(additions).FirstOrDefault(item => item.Name.Equals("nginx", StringComparison.OrdinalIgnoreCase));

				if(existing == null)
				{
					existing = new() { Name = "nginx" };
					if(version != null)
					{
						ImageReference.ValidateTag(version);
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
	}

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

			foreach(var pair in component.Values)
			{
				var value = pair.Key == "package" || pair.Key == "template" && !TemplateCatalog.Contains(pair.Value) ? Path.GetRelativePath(this["source"], this.ResolvePath(pair.Value)) : pair.Value;
				if(value != null || pair.Key == "settings" || pair.Key.StartsWith("environment!", StringComparison.OrdinalIgnoreCase))
					text.Append(pair.Key).Append('=').AppendLine(value);
			}
		}

		return text.ToString().Replace("\r\n", "\n", StringComparison.Ordinal).Replace("\n", "\r\n", StringComparison.Ordinal);
	}

	private static bool IsComponentKey(string key) => _componentKeys.Contains(key, StringComparer.OrdinalIgnoreCase) || key.StartsWith("environment!", StringComparison.OrdinalIgnoreCase) || key.StartsWith("file!", StringComparison.OrdinalIgnoreCase) || key.StartsWith("probe-host!", StringComparison.OrdinalIgnoreCase);
	private string GetImageIdentity(Component component) => Files.HashText(string.Join("\n", component["repository"] ?? "", component["tag"] ?? "", "linux", this["architecture"] ?? ""));
	private static ContainerizationException CreateException(ProfileItem item, string message) => new(2, string.Format(Properties.Resources.Manifest_16_Message, item.Profile.FilePath, item.LineNumber + 1, message));

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
					ValidateIdentity(section);
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
	private static partial Regex GetEnvironmentNameRegex();

	[GeneratedRegex(@"^[A-Za-z0-9][A-Za-z0-9_.-]*$")]
	private static partial Regex GetIdentityRegex();
	#endregion

	#region 嵌套类型
	internal sealed class Component
	{
		#region 成员字段
		private string _packagePath;
		#endregion

		#region 构造函数
		public Component() { }
		public Component(PackageReader.Candidate candidate)
		{
			ArgumentNullException.ThrowIfNull(candidate);
			this.Name = candidate.Package.Name;
			this["package"] = _packagePath = candidate.Path;
			this.Package = candidate.Package;
		}
		#endregion

		#region 公共属性
		public string Name { get; init; }
		public Dictionary<string, string> Values { get; } = new(ComponentKeyComparer.Instance);
		public PackageReader.Descriptor Package { get; private set; }
		public string this[string key] { get => this.Values.GetValueOrDefault(key); set => this.Values[key] = value; }
		public IReadOnlyDictionary<string, string> Settings => ServiceSettings.Parse(this["settings"]);
		public bool IsApplication => !string.IsNullOrEmpty(this["package"]);
		#endregion

		#region 公共方法
		public PackageReader.Descriptor ReadPackage()
		{
			if(this.Package == null || _packagePath != this["package"])
			{
				this.Package = PackageReader.Read(this["package"]);
				_packagePath = this["package"];
			}

			return this.Package;
		}
		#endregion
	}

	private sealed class ComponentKeyComparer : IEqualityComparer<string>
	{
		public static readonly ComponentKeyComparer Instance = new();
		public bool Equals(string left, string right) => StringComparer.Ordinal.Equals(NormalizeKey(left), NormalizeKey(right));
		public int GetHashCode(string value) => StringComparer.Ordinal.GetHashCode(NormalizeKey(value));
		private static string NormalizeKey(string value) => value.StartsWith("file!", StringComparison.OrdinalIgnoreCase) ? "FILE!" + value[5..] : value.ToUpperInvariant();
	}
	#endregion
}
