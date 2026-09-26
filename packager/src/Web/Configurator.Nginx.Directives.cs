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
using System.Text;
using System.Collections.Generic;

namespace Zongsoft.Tools.Packager.Web;

partial class Configurator
{
	partial class Nginx
	{
		internal sealed record Directive(string Name, IReadOnlyList<Argument> Arguments, IReadOnlyList<Directive> Children = null);
		internal readonly record struct Argument(string Value, ArgumentKind Kind = ArgumentKind.Literal, bool Relative = false);
		internal enum ArgumentKind { Literal, Raw, Expression, Resource, Pattern }

		internal static IReadOnlyList<string> Tokenize(string text, Diagnostic.Location source)
		{
			if(string.IsNullOrEmpty(text))
				return [];

			var tokens = new List<string>();
			var token = new StringBuilder();
			char quote = '\0';
			var escaped = false;

			for(var index = 0; index < text.Length; index++)
			{
				var character = text[index];
				if(character is '\r' or '\n' or '\0')
					throw DefinitionException.Create("Directive", source, text);

				if(escaped)
				{
					token.Append(character);
					escaped = false;
					continue;
				}

				if(character == '\\')
				{
					token.Append(character);
					escaped = true;
					continue;
				}

				if(quote != '\0')
				{
					token.Append(character);
					if(character == quote)
						quote = '\0';

					continue;
				}

				if(character is '"' or '\'')
				{
					token.Append(character);
					quote = character;
				}
				else if(char.IsWhiteSpace(character))
				{
					if(token.Length > 0)
					{
						tokens.Add(token.ToString());
						token.Clear();
					}
				}
				else if(character is ';' or '#' or '{' or '}')
				{
					//Nginx 的 ${name} 是参数表达式，不是块边界。
					if(character == '{' && index > 0 && text[index - 1] == '$')
					{
						var end = text.IndexOf('}', index + 1);
						if(end < 0 || !text[(index + 1)..end].All(item => char.IsAsciiLetterOrDigit(item) || item == '_'))
							throw DefinitionException.Create("Directive", source, text);

						token.Append(text[index..(end + 1)]);
						index = end;
					}
					else
						throw DefinitionException.Create("Directive", source, text);
				}
				else
					token.Append(character);
			}

			if(quote != '\0' || escaped)
				throw DefinitionException.Create("Directive", source, text);

			if(token.Length > 0)
				tokens.Add(token.ToString());

			return tokens.AsReadOnly();
		}

		private static IReadOnlyList<Directive> Native(IReadOnlyList<Definition.RawDirective> raw, bool site)
		{
			var result = new List<Directive>();
			var counts = new HashSet<string>(StringComparer.Ordinal);

			foreach(var entry in raw)
			{
				if(string.IsNullOrEmpty(entry.Name) || !entry.Name.All(character => char.IsAsciiLetterOrDigit(character) || character == '_') ||
					entry.Name.StartsWith("worker_", StringComparison.Ordinal) ||
					entry.Name is "http" or "events" or "stream" or "mail" or "pid" or "daemon" or "user" or "map" or "upstream" or "match" or "server" or "location" or "if" or "limit_except" or
					"env" or "load_module" or "master_process" or "thread_pool" or "working_directory" or "debug_points" or "error_log_memory" or "timer_resolution" or "pcre_jit" or "ssl_engine" or
					"use" or "multi_accept" or "accept_mutex" or "accept_mutex_delay" or "debug_connection" or "geo" or "geoip_country" or "geoip_city" or "split_clients" or
					"log_format" or "limit_conn_zone" or "limit_req_zone" or "proxy_cache_path" or "fastcgi_cache_path" or "uwsgi_cache_path" or "scgi_cache_path")
					throw DefinitionException.Create("Directive", entry.Source, entry.Name);

				if(site && entry.Name == "proxy_pass" || !site && entry.Name is "listen" or "server_name")
					throw DefinitionException.Create("Directive", entry.Source, entry.Name);

				var parameters = new List<string>();
				if(entry.Suffix != null)
				{
					var suffix = Tokenize(entry.Suffix, entry.Source);
					if(suffix.Count != 1)
						throw DefinitionException.Create("Directive", entry.Source, entry.Name);

					parameters.Add(suffix[0]);
				}

				parameters.AddRange(Tokenize(entry.Value, entry.Source));
				var singleArgument = entry.Name is "proxy_pass" or "ssl_certificate" or "ssl_certificate_key" or "proxy_http_version" or "proxy_ssl_name" or "proxy_ssl_server_name" or "proxy_ssl_verify" or "proxy_ssl_trusted_certificate" or "proxy_ssl_verify_depth" or "proxy_ssl_session_reuse" or "proxy_ssl_crl" or "proxy_next_upstream_tries" or "proxy_next_upstream_timeout";
				var minimum = singleArgument || entry.Name is "server_name" or "listen" or "include" or "proxy_next_upstream" ? 1 : 0;
				var singleDirective = singleArgument || entry.Name is "server_name" or "proxy_next_upstream" or "proxy_next_upstream_tries";

				if(parameters.Count < minimum || singleArgument && parameters.Count != 1 || singleDirective && !counts.Add(entry.Name))
					throw DefinitionException.Create("Directive", entry.Source, entry.Name);

				result.Add(new(entry.Name, parameters.Select(item => new Argument(item, ArgumentKind.Raw)).ToArray()));
			}

			return result;
		}

		private static Directive Leaf(string name, params string[] values) => new(name, values.Select(value => new Argument(value)).ToArray());
		private static Directive Expression(string name, params string[] values) => new(name, values.Select(value => new Argument(value, ArgumentKind.Expression)).ToArray());
		private static Directive Resource(string name, Definition.ResourceReference reference) => new(name, [new(reference.Path, ArgumentKind.Resource, reference.Relative)]);
	}
}
