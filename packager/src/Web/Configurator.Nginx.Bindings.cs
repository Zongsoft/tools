/*
 *   _____                                ______
 *  /_   /  ____  ____  ____  _________  / __/ /_
 *    / /  / __ \/ __ \/ __ \/ ___/ __ \/ /_/ __/
 *   / /__/ /_/ / / / / /_/ /\_ \/ /_/ / __/ /_
 *  /____/\____/_/ /_/\__  /____/\____/_/  \__/
 *                   /____/
 *
 * Authors:
 *   钟峰(Popeye Zhong) <zongsoft@gmail.com>
 *
 * The MIT License (MIT)
 *
 * Copyright (C) 2020-2026 Zongsoft Corporation <http://www.zongsoft.com>
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
using System.Text;
using System.Globalization;
using System.Collections.Generic;

namespace Zongsoft.Tools.Packager.Web;

partial class Configurator
{
	partial class Nginx
	{
		private static class Bindings
		{
			internal static string Export(IReadOnlyList<Definition.Site> sites, IReadOnlyList<Directive> blocks, List<Diagnostic> diagnostics)
			{
				var servers = blocks.Where(block => block.Name == "server").ToArray();
				var text = new StringBuilder();

				for(var index = 0; index < sites.Count; index++)
				{
					var site = sites[index];

					try
					{
						var children = servers[index].Children;
						var hosts = children.Single(item => item.Name == "server_name")
							.Arguments
							.Select(item => Literal(item.Value))
							.ToArray();

						if(hosts.Any(host =>
						   host == null ||
						   host.StartsWith('~') ||
						   host.IndexOfAny([',', ';', '\r', '\n']) >= 0) ||
						   hosts.Length > 1 &&
						   hosts.Contains("") ||
						   children.Any(item => item.Name == "include"))
							throw DefinitionException.Create("Capability", site.Source, ".bindings: server_name/include");

						var bindings = new List<string>();
						var defaults = new List<string>();

						foreach(var listener in children.Where(item => item.Name == "listen"))
						{
							var arguments = listener.Arguments.Select(item => Literal(item.Value)).ToArray();

							if(arguments.Any(item => item == null) || arguments.Skip(1).Any(item => item is not ("ssl" or "default_server" or "bind" or "ipv6only=on" or "http2")))
								throw DefinitionException.Create("Capability", site.Source, ".bindings: " + string.Join(' ', listener.Arguments.Select(item => item.Value)));

							var address = Listen(arguments[0]);
							var separator = address?.LastIndexOf(':') ?? -1;

							if(separator < 0 || !IPAddress.TryParse(address[..separator], out var ip) ||
							   !int.TryParse(address[(separator + 1)..], NumberStyles.None, CultureInfo.InvariantCulture, out var port) || port is < 1 or > 65535 ||
							   ip.AddressFamily == System.Net.Sockets.AddressFamily.InterNetworkV6 && ip.ScopeId != 0)
								throw DefinitionException.Create("Capability", site.Source, ".bindings: listen " + arguments[0]);

							var host = ip.ToString();
							var uri = $"{(arguments.Contains("ssl") ? "https" : "http")}://{(host.Contains(':') ? "[" + host + "]" : host)}:{port.ToString(CultureInfo.InvariantCulture)}";

							if(!bindings.Contains(uri))
								bindings.Add(uri);
							if(arguments.Contains("default_server") && !defaults.Contains(uri))
								defaults.Add(uri);
						}

						text.Append('[').Append(site.Name).Append("]\r\n");

						if(hosts.Length > 0 && hosts[0].Length > 0)
							text.Append("host=").AppendJoin(',', hosts).Append("\r\n");

						text.Append("bind=").AppendJoin(',', bindings).Append("\r\n");

						if(defaults.Count > 0)
							text.Append("default=").AppendJoin(',', defaults).Append("\r\n");

						text.Append("\r\n");
					}
					catch(DefinitionException exception)
					{
						diagnostics.Add(exception.Diagnostic with { Level = Diagnostic.Severity.Warning });
						return null;
					}
				}

				return text.ToString();
			}
		}
	}
}
