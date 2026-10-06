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
using System.Text.RegularExpressions;
using System.Collections.Generic;

using Zongsoft.Tools.Containerizer.Protocol;

namespace Zongsoft.Tools.Containerizer;

partial class WebIngress
{
	private static void ValidateFiles(ContainerManifest manifest, ServiceBuildContext ingress, IEnumerable<ServiceBuildContext> applications)
	{
		var component = manifest.Components.Single(component => component.Name.Equals(ingress.Plan.Id, StringComparison.OrdinalIgnoreCase));

		foreach(var pair in component.Values.Where(pair => pair.Key.StartsWith("file!", StringComparison.OrdinalIgnoreCase)))
		{
			var target = Target(pair.Key[5..], component.Name);
			var input = manifest.Resolve(pair.Value);
			Files.NoLinks(input);

			if(!File.Exists(input))
				throw WebPackage.Invalid(component.Name, pair.Key);

			AddFile(ingress, target, input);
		}

		foreach(var application in applications.Where(application => application.Package.Web.Hoster == ingress.Plan.Id))
		{
			RenderTemplate(application, path =>
			{
				var owned = OwnedFile(application, path);

				if(owned == null && !ingress.Configuration.ContainsKey(Target(path, application.Plan.Id)))
					throw WebPackage.Invalid(application.Plan.Id, path);

				return owned == null ? path : ResourceTarget(application, owned);
			});
		}
	}

	internal static void Render(ContainerManifest manifest, IReadOnlyList<ServiceBuildContext> sources, string workspace)
	{
		foreach(var ingress in sources.Where(source => source.Plan.Template == "nginx" && source.Plan.Web.Count > 0))
		{
			var directory = Path.Combine(workspace, "web", ingress.Plan.Id);
			Files.PrivateDirectory(directory);
			var main = new StringBuilder("events {}\nhttp {\n\tinclude /etc/nginx/mime.types;\n");

			foreach(var application in sources.Where(source => source.Package?.Web?.Hoster == ingress.Plan.Id).OrderBy(source => source.Plan.Id, StringComparer.Ordinal))
			{
				var extraction = new Dictionary<string, string>(StringComparer.Ordinal);
				var text = RenderTemplate(application, path =>
				{
					var owned = OwnedFile(application, path);
					if(owned == null)
						return path;

					if(!extraction.TryGetValue(owned, out var local))
					{
						local = Path.Combine(directory, Files.HashText(application.Plan.Id + "/" + owned));
						extraction.Add(owned, local);
						AddFile(ingress, ResourceTarget(application, owned), local);
					}

					return ResourceTarget(application, owned);
				});

				var component = manifest.Components.Single(component => component.Name.Equals(application.Plan.Id, StringComparison.OrdinalIgnoreCase));

				if(extraction.Count > 0)
				{
					PackageReader.Read(component["package"], extraction);
					if(extraction.Values.Any(path => !File.Exists(path)))
						throw WebPackage.Invalid(application.Plan.Id, "file");
				}

				var target = $"/etc/nginx/containerizer/{application.Plan.Id}.conf";
				var file = Path.Combine(directory, "sites", application.Plan.Id + ".conf");
				Files.Write(file, text);
				AddFile(ingress, target, file, $"sites/{application.Plan.Id}.conf");
				main.Append("\tinclude ").Append(target).Append(";\n");
			}

			main.Append("}\n");
			var configuration = Path.Combine(directory, "nginx.conf");
			Files.Write(configuration, main.ToString());
			AddFile(ingress, "/etc/nginx/nginx.conf", configuration, "nginx.conf");
		}
	}

	private static string OwnedFile(ServiceBuildContext application, string path)
	{
		var target = path.StartsWith("./", StringComparison.Ordinal) ? application.Package.Web.Root + "/" + path[2..] : path;
		Target(target, application.Plan.Id);

		if(application.Package.Entries.TryGetValue(target, out var regular))
		{
			if(!regular || application.Package.Links.Any(link => target.StartsWith(link + "/", StringComparison.Ordinal)))
				throw WebPackage.Invalid(application.Plan.Id, path);

			return target;
		}

		if(path.StartsWith("./", StringComparison.Ordinal))
			throw WebPackage.Invalid(application.Plan.Id, path);

		return null;
	}

	private static string ResourceTarget(ServiceBuildContext application, string path) => $"/etc/nginx/containerizer-files/{application.Plan.Id}/{Files.HashText(path)}/{path[(path.LastIndexOf('/') + 1)..]}";

	private static string RenderTemplate(ServiceBuildContext application, Func<string, string> file)
	{
		var result = Markers().Replace(application.Package.Web.Template, match =>
		{
			if(match.Groups[1].Value == "application")
				return application.Plan.Id;

			string path;

			try { path = new UTF8Encoding(false, true).GetString(Convert.FromBase64String(match.Groups[2].Value)); }
			catch(Exception exception) when(exception is FormatException or DecoderFallbackException)
			{
				throw WebPackage.Invalid(application.Plan.Id, "template/file");
			}

			return file(path).Replace("\\", "\\\\", StringComparison.Ordinal).Replace("\"", "\\\"", StringComparison.Ordinal);
		});
		if(result.Contains("{{zongsoft:", StringComparison.Ordinal))
			throw WebPackage.Invalid(application.Plan.Id, "template");

		return result;
	}

	private static string Target(string path, string source)
	{
		if(!Files.IsLinuxPath(path) || path.Contains("//", StringComparison.Ordinal) || path.EndsWith('/') || path.Any(character => char.IsControl(character) || character is '$' or '"' or '*' or '?' or '[' or ']'))
			throw WebPackage.Invalid(source, path);

		return path;
	}

	private static void AddFile(ServiceBuildContext source, string target, string file, string relativePath = null)
	{
		if(source.Configuration.Keys.Concat(source.Plan.Mounts.Select(mount => mount.Target)).Any(existing => existing == target || existing.StartsWith(target + "/", StringComparison.Ordinal) || target.StartsWith(existing + "/", StringComparison.Ordinal)))
			throw WebPackage.Invalid(source.Plan.Id, target);

		source.Configuration.Add(target, (file, relativePath));
	}

	[GeneratedRegex(@"\{\{zongsoft:(application|file:([^}]+))\}\}")]
	private static partial Regex Markers();
}
