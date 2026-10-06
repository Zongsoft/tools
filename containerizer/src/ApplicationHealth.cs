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
using System.Net.Sockets;
using System.Collections.Generic;

using Zongsoft.Tools.Containerizer.Protocol;

namespace Zongsoft.Tools.Containerizer;

internal static class ApplicationHealth
{
	#region 内部方法
	internal static ContainerizationException Invalid(string name, string field) => new(2,
		string.Format(Properties.Resources.ApplicationHealth_Invalid_Message, name, field));

	internal static void Configure(PackageReader.Descriptor package, ServiceBuildContext source)
	{
		var listeners = ParseListeners(package.Listen, package.Name);
		if(listeners.Length == 0)
		{
			source.Plan.Health.Test = ["CMD-SHELL", "kill -0 1"];
			return;
		}

		var bindings = listeners.Select(listener => Binding(listener, package.Name)).ToArray();
		var arguments = new List<string>(source.Plan.Entrypoint);
		var index = arguments.IndexOf("--urls");

		if(index >= 0)
			arguments.RemoveRange(index, Math.Min(2, arguments.Count - index));

		arguments.AddRange(["--urls", string.Join(';', bindings)]);
		source.Plan.Entrypoint = arguments.ToArray();

		foreach(var entry in listeners.Where(_ => package.Web == null).DistinctBy(listener => (listener.Port, listener.Scheme, listener.Host)))
		{
			var name = "web-" + entry.Port;
			if(!source.Plan.Ports.Any(port => port.Name == name))
				source.Plan.Ports.Add(new() { Name = name, Address = "127.0.0.1", Host = entry.Port, Container = entry.Port });

			var hostname = Uri.CheckHostName(entry.Host) == UriHostNameType.Dns ? entry.Host : "127.0.0.1";
			source.Plan.Web.Add(new()
			{
				Application = source.Plan.Id,
				Name = "listen-" + source.Plan.Web.Count,
				Hosts = hostname == "127.0.0.1" ? [] : [hostname],
				ProbeHosts = [hostname],
				Bindings = [new() { Scheme = entry.Scheme, Address = "0.0.0.0", Port = entry.Port, Publication = name, IsDefault = true }],
			});
		}

		if(source.Plan.Health.TimeoutSeconds < 1)
			throw Invalid(package.Name, "health-timeout");

		var listener = listeners.FirstOrDefault(listener => listener.Scheme == "http") ?? listeners[0];
		var address = IPAddress.TryParse(listener.Host.Trim('[', ']'), out var ip) && ip.AddressFamily == AddressFamily.InterNetworkV6 ? "[::1]" : "127.0.0.1";
		var argumentsText = "";
		var host = address;

		if(listener.Scheme == "https")
		{
			host = Uri.CheckHostName(listener.Host) == UriHostNameType.Dns ? listener.Host : null;

			if(string.IsNullOrWhiteSpace(host) || Uri.CheckHostName(host) != UriHostNameType.Dns || host.Any(char.IsWhiteSpace))
				throw Invalid(package.Name, "Listen (HTTPS DNS name)");

			argumentsText += $" --resolve {ApplicationImageBuilder.Quote($"{host}:{listener.Port}:{address}")}";

		}
		else if(Uri.CheckHostName(listener.Host) == UriHostNameType.Dns)
			argumentsText += $" --header {ApplicationImageBuilder.Quote($"Host: {listener.Authority}")}";

		var url = $"{listener.Scheme}://{host}:{listener.Port}/";
		var timeout = Math.Max(1, source.Plan.Health.TimeoutSeconds - 1);
		var command = $"curl --noproxy '*' --silent --show-error --output /dev/null --max-time {timeout}{argumentsText} {ApplicationImageBuilder.Quote(url)}";

		source.Plan.Health.Test = ["CMD-SHELL", command];
	}

	internal static Uri[] ParseListeners(string value, string name)
	{
		if(string.IsNullOrWhiteSpace(value))
			return [];

		return value.Split(';').Select(part =>
		{
			part = part.Trim();
			var separator = part.IndexOf("://", StringComparison.Ordinal);

			if(separator >= 0 && part.Length > separator + 3 && part[separator + 3] is '*' or '+')
			{
				var suffix = part[(separator + 4)..];

				if(suffix.Length != 0 && suffix[0] is not (':' or '/'))
					throw Invalid(name, "listen");

				part = $"{part[..(separator + 3)]}0.0.0.0{suffix}";
			}

			if(part.Any(char.IsWhiteSpace) || part.Contains('\\') || !Uri.TryCreate(part, UriKind.Absolute, out var uri) ||
			   uri.Scheme is not ("http" or "https") || uri.Port < 1 || uri.HostNameType == UriHostNameType.Unknown || uri.UserInfo.Length != 0 ||
			   uri.AbsolutePath != "/" || uri.Query.Length != 0 || uri.Fragment.Length != 0)
				throw Invalid(name, "listen");

			return uri;
		}).ToArray();
	}
	#endregion

	#region 私有方法
	private static string Binding(Uri listener, string name)
	{
		var host = "0.0.0.0";

		if(IPAddress.TryParse(listener.Host.Trim('[', ']'), out var ip))
		{
			if(!IPAddress.IsLoopback(ip) && !ip.Equals(IPAddress.Any) && !ip.Equals(IPAddress.IPv6Any))
				throw Invalid(name, "listen");

			if(ip.AddressFamily == AddressFamily.InterNetworkV6)
				host = "[::]";
		}

		return $"{listener.Scheme}://{host}:{listener.Port}";
	}
	#endregion
}
