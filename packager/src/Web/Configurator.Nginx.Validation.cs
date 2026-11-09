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
					var key = (directive.Arguments.Any(argument => Literal(argument.Value) == "quic") ? "udp:" : "tcp:") + address + "/" + name;
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
	}
}
