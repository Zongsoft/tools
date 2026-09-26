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
using System.Net;
using System.Linq;
using System.Globalization;
using System.Collections.Generic;

namespace Zongsoft.Tools.Packager.Web;

partial class Configurator
{
	partial class Nginx
	{
		internal static string Literal(string token)
		{
			if(token == null || token.Contains('$') || token.Contains('\\'))
				return null;

			return token.Length >= 2 && token[0] is '\'' or '"' && token[^1] == token[0] ? token[1..^1] : token;
		}

		private static IReadOnlyList<string> Names(Definition.Site site)
		{
			var native = Native(site.Directives, true).FirstOrDefault(item => item.Name == "server_name");

			return native == null ? site.Hosts.Count == 0 ? [""] : site.Hosts :
				native.Arguments.Select(item => Literal(item.Value)).Where(item => item != null && !item.StartsWith('~')).ToArray();
		}

		private static void ValidateListeners(Definition.Site site, IReadOnlyList<Directive> directives, Dictionary<string, Diagnostic.Location> listeners)
		{
			foreach(var directive in directives.Where(item => item.Name == "listen"))
			{
				var address = Listen(Literal(directive.Arguments[0].Value));
				if(address == null)
					continue;

				foreach(var name in Names(site))
				{
					var key = address + "/" + name;
					if(listeners.TryGetValue(key, out var previous))
					{
						var error = DefinitionException.Create("Duplicate", site.Source, key);
						throw new DefinitionException(error.Diagnostic with { Related = previous });
					}

					listeners.Add(key, site.Source);
				}
			}
		}

		private static string Listen(string address)
		{
			if(string.IsNullOrEmpty(address) || address.StartsWith("unix:", StringComparison.Ordinal))
				return address;

			var host = "0.0.0.0";
			var port = 80;

			if(int.TryParse(address, NumberStyles.None, CultureInfo.InvariantCulture, out var number))
				port = number;
			else
			{
				var separator = address.LastIndexOf(':');
				if(separator >= 0 && (!address.StartsWith('[') || separator > address.IndexOf(']')))
				{
					host = address[..separator];
					if(!int.TryParse(address[(separator + 1)..], NumberStyles.None, CultureInfo.InvariantCulture, out port))
						return null;
				}
				else
					host = address;

				if(host == "*")
					host = "0.0.0.0";
				else if(IPAddress.TryParse(host.Trim('[', ']'), out var ip))
					host = ip.ToString();
				else
					return null;
			}

			return host + ":" + port.ToString(CultureInfo.InvariantCulture);
		}

		private static void ValidateProxy(Definition.Route route, IReadOnlyList<Directive> directives)
		{
			if(route.Match != Definition.MatchKind.Regex)
				return;

			var proxy = directives.FirstOrDefault(item => item.Name == "proxy_pass");
			var address = proxy == null ? null : Literal(proxy.Arguments[0].Value);
			if(address == null)
				return;

			var scheme = address.IndexOf("://", StringComparison.Ordinal);
			if(scheme >= 0 && address.IndexOfAny(['/', '?', '#'], scheme + 3) >= 0)
				throw DefinitionException.Create("Directive", route.Source, "proxy_pass " + address);
		}
	}
}
