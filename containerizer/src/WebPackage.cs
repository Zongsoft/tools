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
using System.Net;
using System.Globalization;
using System.Collections.Generic;

using Zongsoft.Configuration.Profiles;
using Zongsoft.Tools.Containerizer.Protocol;

namespace Zongsoft.Tools.Containerizer;

internal sealed class WebPackage
{
	#region 公共属性
	public string Root { get; private init; }
	public string Hoster { get; private init; }
	public string Template { get; private init; }
	public List<WebSitePlan> Sites { get; private init; }
	#endregion

	#region 读取说明
	internal static WebPackage Read(PackageReader.Descriptor package)
	{
		var assets = package.Entries.Keys.Where(path => path.Contains("/.web/", StringComparison.Ordinal)).ToArray();
		if(assets.Length == 0)
			return null;

		var roots = assets.Select(path => path[..path.IndexOf("/.web/", StringComparison.Ordinal)]).Distinct(StringComparer.Ordinal).ToArray();
		if(roots.Length != 1)
			throw CreateException(package.Name, ".web");

		var root = roots[0];
		var prefix = root + "/.web/nginx/";
		if(assets.Any(path => !path.StartsWith(prefix, StringComparison.Ordinal) && path != root + "/.web/nginx"))
			throw CreateException(package.Name, ".web/<hoster>");

		var bindings = prefix + ".bindings";
		var configuration = prefix + package.Name + ".conf";
		var template = configuration + ".template";

		foreach(var path in new[] { bindings, configuration, template })
		{
			if(!package.WebTexts.ContainsKey(path))
				throw CreateException(package.Name, path);
		}

		if(assets.Any(path => path.EndsWith(".conf", StringComparison.Ordinal) && path != configuration))
			throw CreateException(package.Name, prefix + "*.conf");

		return new() { Root = root, Hoster = "nginx", Template = package.WebTexts[template], Sites = ParseSites(package.WebTexts[bindings], bindings) };
	}

	private static List<WebSitePlan> ParseSites(string text, string source)
	{
		var declarations = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
		var section = "";

		foreach(var line in text.Split('\n').Select(line => line.Trim()))
		{
			if(line.Length == 0 || line[0] is '#' or ';')
				continue;

			if(line.StartsWith('[') && line.EndsWith(']'))
			{
				section = line[1..^1].Trim();
				if(section.Length == 0 || section.Any(char.IsWhiteSpace) || !declarations.Add("[" + section + "]"))
					throw CreateException(source, line);
			}
			else
			{
				var separator = line.IndexOf('=');
				if(section.Length == 0 || separator <= 0 || !declarations.Add(section + "/" + line[..separator].Trim()))
					throw CreateException(source, line);
			}
		}

		using var reader = new StringReader(text);
		var profile = Profile.Load(reader, new ProfileOptions { ImportBehavior = ProfileDirectiveBehavior.Suppressed });
		var sites = new List<WebSitePlan>();

		if(profile.Entries.Count > 0 || profile.Sections.Count == 0)
			throw CreateException(source, ".bindings");

		foreach(var item in profile.Sections)
		{
			if(item.Sections.Count > 0 || item.Entries.Any(entry => entry.Name is not ("host" or "bind" or "default")))
				throw CreateException(source, item.Name);

			var fields = item.Entries.ToDictionary(entry => entry.Name, entry => entry.Value, StringComparer.Ordinal);
			var site = new WebSitePlan { Name = item.Name };

			if(fields.TryGetValue("host", out var hosts))
			{
				site.Hosts.AddRange(ParseList(hosts, source).Distinct(StringComparer.OrdinalIgnoreCase));
				if(site.Hosts.Any(host => !IsValidHost(host)))
					throw CreateException(source, "host=" + hosts);
			}

			if(!fields.TryGetValue("bind", out var bindings))
				throw CreateException(source, item.Name + "/bind");

			var addresses = new Dictionary<string, WebBindingPlan>(StringComparer.Ordinal);
			foreach(var value in ParseList(bindings, source))
			{
				var binding = ParseBinding(value, source);
				if(!addresses.TryAdd(GetAddress(binding), binding))
					throw CreateException(source, "bind=" + value);

				site.Bindings.Add(binding);
			}

			if(fields.TryGetValue("default", out var defaults))
			{
				foreach(var value in ParseList(defaults, source))
				{
					var key = GetAddress(ParseBinding(value, source));
					if(!addresses.TryGetValue(key, out var binding) || binding.IsExplicitDefault)
						throw CreateException(source, "default=" + value);

					binding.IsExplicitDefault = true;
				}
			}

			sites.Add(site);
		}

		return sites;
	}
	#endregion

	#region 绑定与主机名
	private static string[] ParseList(string text, string source)
	{
		var values = (text ?? "").Split([',', ';'], StringSplitOptions.TrimEntries);
		if(values.Any(string.IsNullOrEmpty))
			throw CreateException(source, text);

		return values;
	}

	private static WebBindingPlan ParseBinding(string text, string source)
	{
		var separator = text.LastIndexOf(':');
		if(separator < 0 || !int.TryParse(text[(separator + 1)..], NumberStyles.None, CultureInfo.InvariantCulture, out var port) || port is < 1 or > 65535 ||
			text.Contains('\\') || text.Any(char.IsWhiteSpace) || !Uri.TryCreate(text, UriKind.Absolute, out var uri) || uri.Scheme is not ("http" or "https") ||
			uri.UserInfo.Length > 0 || uri.Query.Length > 0 || uri.Fragment.Length > 0 || uri.AbsolutePath != "/" || uri.Port != port ||
			!IPAddress.TryParse(uri.Host.Trim('[', ']'), out var address) || address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetworkV6 && address.ScopeId != 0)
			throw CreateException(source, "bind=" + text);

		return new() { Scheme = uri.Scheme, Address = address.ToString(), Port = port };
	}

	internal static string GetAddress(WebBindingPlan binding) => $"{binding.Scheme}://{(binding.Address.Contains(':') ? "[" + binding.Address + "]" : binding.Address)}:{binding.Port.ToString(CultureInfo.InvariantCulture)}";
	internal static bool IsConcreteHost(string host) => host != null && !host.Contains('*') && !host.StartsWith('.') && Uri.CheckHostName(host) is UriHostNameType.Dns or UriHostNameType.IPv4 or UriHostNameType.IPv6;
	private static bool IsValidHost(string host)
	{
		if(IsConcreteHost(host))
			return true;

		var value = host.StartsWith("*.", StringComparison.Ordinal) ? host[2..] : host.StartsWith('.') ? host[1..] : host.EndsWith(".*", StringComparison.Ordinal) ? host[..^2] : null;
		return value != null && !value.Contains('*') && Uri.CheckHostName(value) == UriHostNameType.Dns;
	}

	internal static int GetHostMatchScore(string pattern, string host)
	{
		if(pattern.Equals(host, StringComparison.OrdinalIgnoreCase))
			return int.MaxValue;
		if(pattern.StartsWith("*.", StringComparison.Ordinal) && host.Length > pattern.Length - 1 && host.EndsWith(pattern[1..], StringComparison.OrdinalIgnoreCase))
			return 100000 + pattern.Length;
		if(pattern.StartsWith('.') && (host.Equals(pattern[1..], StringComparison.OrdinalIgnoreCase) || host.EndsWith(pattern, StringComparison.OrdinalIgnoreCase)))
			return 100000 + pattern.Length;
		if(pattern.EndsWith(".*", StringComparison.Ordinal) && host.Length > pattern.Length - 1 && host.StartsWith(pattern[..^1], StringComparison.OrdinalIgnoreCase))
			return pattern.Length;

		return 0;
	}

	internal static ContainerizationException CreateException(string source, string field) => new(2, string.Format(Properties.Resources.Web_Invalid_Message, source, field));
	#endregion
}
