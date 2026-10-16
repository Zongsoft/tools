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
using System.Globalization;
using System.Collections.Generic;

namespace Zongsoft.Tools.Packager.Web;

partial class Configurator
{
	partial class Nginx
	{
		private static class Bindings
		{
			internal static BindingDocument Export(IReadOnlyList<Definition.Site> sites, IReadOnlyList<Directive> blocks, BindingDocument application)
			{
				var servers = blocks.Where(block => block.Name == "server").ToArray();
				var listeners = new List<Listener>();
				var names = new List<string[]>();

				for(var index = 0; index < sites.Count; index++)
				{
					var site = sites[index];
					var children = servers[index].Children;
					var hosts = children.Single(item => item.Name == "server_name").Arguments.Select(item => Literal(item.Value)).ToArray();

					if(hosts.Any(host => host == null || host.StartsWith('~')) || hosts.Length > 1 && hosts.Contains(""))
						throw DefinitionException.Create("Capability", site.Source, ".bindings: server_name");

					names.Add(hosts.Where(host => host.Length > 0).Select(host => host.ToLowerInvariant()).Distinct(StringComparer.Ordinal).ToArray());

					var http2 = Enabled(children, "http2", false, site.Source);
					var http3 = Enabled(children, "http3", true, site.Source);

					foreach(var listener in children.Where(item => item.Name == "listen"))
					{
						var arguments = listener.Arguments.Select(item => Literal(item.Value)).ToArray();

						if(arguments.Length == 0 || arguments.Any(item => item == null) ||
						   arguments.Skip(1).Any(item => item is not ("ssl" or "default_server" or "bind" or "ipv6only=on" or "http2" or "quic" or "reuseport")))
							throw DefinitionException.Create("Capability", site.Source, ".bindings: listen");

						var address = Listen(arguments[0]);
						var separator = address?.LastIndexOf(':') ?? -1;

						if(separator < 0 || !IPAddress.TryParse(address[..separator], out var ip) ||
						   !int.TryParse(address[(separator + 1)..], NumberStyles.None, CultureInfo.InvariantCulture, out var port) || port is < 1 or > 65535 ||
						   ip.AddressFamily == System.Net.Sockets.AddressFamily.InterNetworkV6 && ip.ScopeId != 0)
							throw DefinitionException.Create("Capability", site.Source, ".bindings: listen " + arguments[0]);

						var quic = arguments.Contains("quic");
						var secure = quic || arguments.Contains("ssl");
						var versions = quic ? new[] { 3 } : http2 || arguments.Contains("http2") ? [1, 2] : [1];

						if(quic && !http3 || quic && arguments.Contains("http2") ||
						   secure && (!children.Any(item => item.Name == "ssl_certificate") || !children.Any(item => item.Name == "ssl_certificate_key")))
							throw DefinitionException.Create("Capability", site.Source, ".bindings: QUIC/TLS");

						var protocols = versions.Select(version => "http" + version.ToString(CultureInfo.InvariantCulture)).ToList();

						if(site.Routes.Any(route => route.Mapping.RequestKind == "grpc"))
							protocols.AddRange(versions.Where(version => version >= 2).Select(version => "grpc+http" + version.ToString(CultureInfo.InvariantCulture)));

						var host = ip.ToString();
						listeners.Add(new(index, (quic ? "udp" : "tcp") + ":" + host + ":" + port.ToString(CultureInfo.InvariantCulture),
							secure ? "https" : "http", host, port, protocols.ToArray(), arguments.Contains("default_server")));
					}
				}

				var bindings = application.Bindings.ToList();
				var references = new Dictionary<string, BindingDocument.Binding>(StringComparer.Ordinal);

				foreach(var socket in listeners.GroupBy(listener => listener.Socket))
				{
					if(socket.Select(listener => listener.Scheme).Distinct().Count() != 1 || socket.Count(listener => listener.Default) > 1 ||
					   socket.Select(listener => listener.Site).Distinct().Count() != socket.Count())
						throw DefinitionException.Create("Conflict", sites[socket.First().Site].Source, socket.Key);

					var first = socket.First();
					var binding = BindingDocument.CreateBinding("nginx", [],
					[
						new(first.Scheme, first.Host, first.Port, "/", socket.SelectMany(listener => listener.Protocols).Distinct(StringComparer.Ordinal).ToArray()),
					]);

					bindings.Add(binding);
					references.Add(socket.Key, binding);
				}

				return new()
				{
					Bindings = bindings.ToArray(),
					Sites = sites.Select((site, index) => new BindingDocument.Site("site:" + site.Name.ToLowerInvariant(), site.Name, names[index],
						listeners.Where(listener => listener.Site == index).Select(listener =>
							new BindingDocument.SiteListener(references[listener.Socket].Id, references[listener.Socket].Endpoints[0].Id, listener.Default)).ToArray(),
						site.Routes.Select(route => route.Mapping).ToArray())).ToArray(),
				};
			}

			private static bool Enabled(IReadOnlyList<Directive> directives, string name, bool fallback, Diagnostic.Location source)
			{
				var values = directives.Where(item => item.Name == name).ToArray();

				if(values.Length == 0)
					return fallback;
				if(values.Length > 1 || values[0].Arguments.Count != 1 || Literal(values[0].Arguments[0].Value) is not ("on" or "off"))
					throw DefinitionException.Create("Capability", source, ".bindings: " + name);

				return Literal(values[0].Arguments[0].Value) == "on";
			}

			private sealed record Listener(int Site, string Socket, string Scheme, string Host, int Port, string[] Protocols, bool Default);
		}
	}
}
