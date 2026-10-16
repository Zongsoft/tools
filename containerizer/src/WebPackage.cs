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
	public List<ServicePlan.WebSite> Sites { get; private init; }
	#endregion

	#region 读取说明
	internal static WebPackage Read(PackageReader.Descriptor package)
	{
		var assets = package.Entries.Keys.Where(path => path.Contains("/.web/", StringComparison.Ordinal)).ToArray();
		if(assets.Length == 0)
		{
			if(package.Bindings.Sites.Length > 0)
				throw CreateException(package.Name, ".web/nginx");

			return null;
		}

		var root = package.InstallPath.TrimEnd('/');
		var prefix = root + "/.web/nginx/";
		if(package.Bindings.Sites.Length == 0 || assets.Any(path => !path.StartsWith(prefix, StringComparison.Ordinal) && path != root + "/.web/nginx") ||
			assets.Contains(prefix + ".bindings", StringComparer.Ordinal))
			throw CreateException(package.Name, ".bindings/sites");

		var configuration = prefix + package.Name + ".conf";
		var template = configuration + ".template";
		if(!package.WebTexts.ContainsKey(configuration) || !package.WebTexts.ContainsKey(template) ||
			assets.Any(path => path.EndsWith(".conf", StringComparison.Ordinal) && path != configuration))
			throw CreateException(package.Name, configuration);

		var bindings = package.Bindings.Bindings.ToDictionary(binding => binding.Id, StringComparer.Ordinal);
		var sites = new List<ServicePlan.WebSite>();

		foreach(var declaration in package.Bindings.Sites)
		{
			var site = new ServicePlan.WebSite { Name = declaration.Name, Hosts = [.. declaration.Hosts] };
			foreach(var listener in declaration.Listeners)
			{
				var endpoint = bindings[listener.Binding].Endpoints.Single(endpoint => endpoint.Id == listener.Endpoint);
				if(bindings[listener.Binding].Owner != "nginx" || endpoint.Transports.Length != 1)
					throw CreateException(package.Name, listener.Binding);

				site.Bindings.Add(new()
				{
					Binding = listener.Binding,
					Endpoint = listener.Endpoint,
					Scheme = endpoint.Scheme,
					Address = endpoint.Host,
					Port = endpoint.Port,
					Protocol = endpoint.Transports[0],
					IsExplicitDefault = listener.Default,
				});
			}

			sites.Add(site);
		}

		return new() { Root = root, Hoster = "nginx", Template = package.WebTexts[template], Sites = sites };
	}
	#endregion

	#region 绑定与主机名
	internal static string GetAddress(ServicePlan.WebSite.BindingInfo binding) => $"{binding.Scheme}://{(binding.Address.Contains(':') ? "[" + binding.Address + "]" : binding.Address)}:{binding.Port.ToString(CultureInfo.InvariantCulture)}";
	internal static bool IsConcreteHost(string host) => host != null && !host.Contains('*') && !host.StartsWith('.') && Uri.CheckHostName(host) is UriHostNameType.Dns or UriHostNameType.IPv4 or UriHostNameType.IPv6;
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
