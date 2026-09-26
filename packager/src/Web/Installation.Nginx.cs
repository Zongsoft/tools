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

namespace Zongsoft.Tools.Packager.Web;

partial class Installation
{
	private static class Nginx
	{
		internal static string Prune(string current) => $$"""
		for hoster_web_old in "$HOSTER_WEB_TARGET"/.web/nginx/*.conf; do
			[ -e "$hoster_web_old" ] || [ -L "$hoster_web_old" ] || continue
			{{(current == null ? ":" : $"[ \"$hoster_web_old\" != \"$HOSTER_WEB_TARGET/{current}\" ] || continue")}}
			if [ -z "${DESTDIR:-}" ]; then
				hoster_web_link="/etc/nginx/conf.d/${hoster_web_old##*/}"
				if hoster_web_owned "$hoster_web_link" "$HOSTER_WEB_ROOT/.web/nginx/${hoster_web_old##*/}"; then
					rm -f -- "$hoster_web_link"
					HOSTER_WEB_CHANGED=1
				fi
			fi
			rm -f -- "$hoster_web_old"
			HOSTER_WEB_CHANGED=1
		done
		if [ -z "${DESTDIR:-}" ]; then
			for hoster_web_link in /etc/nginx/conf.d/*.conf; do
				[ -L "$hoster_web_link" ] || continue
				{{(current == null ? ":" : $"[ \"${{hoster_web_link##*/}}\" != {Quote(System.IO.Path.GetFileName(current))} ] || continue")}}
				if hoster_web_owned "$hoster_web_link" "$HOSTER_WEB_ROOT/.web/nginx/${hoster_web_link##*/}"; then
					rm -f -- "$hoster_web_link"
					HOSTER_WEB_CHANGED=1
				fi
			done
		fi
		""";

		internal static string Activate(string current)
		{
			var activation = current == null ? "hoster_web_current=''" : $"hoster_web_current=\"$HOSTER_WEB_ROOT/{current}\"";

			return activation + "\n" + Check() + "\n" + $$"""
			if [ -n "$hoster_web_current" ] || [ "${HOSTER_WEB_CHANGED:-0}" = 1 ]; then
				if hoster_web_activation; then
					hoster_web_link=''
					if [ -n "$hoster_web_current" ]; then
						hoster_web_link="/etc/nginx/conf.d/${hoster_web_current##*/}"
						mkdir -p /etc/nginx/conf.d
						if [ -d "$hoster_web_link" ] && [ ! -L "$hoster_web_link" ]; then
							printf '%s: %s\n' {{Quote(string.Format(Properties.Resources.Web_Conflict_Message, string.Empty))}} "$hoster_web_link" >&2
							exit 1
						fi
						if ! hoster_web_owned "$hoster_web_link" "$hoster_web_current"; then
							rm -f -- "$hoster_web_link"
							ln -s -- "$hoster_web_current" "$hoster_web_link"
						fi
					fi
					hoster_web_check "$hoster_web_link" || exit 1
				fi
			fi
			""";
		}

		internal static string Deactivate(bool current) => Check() + "\n" + $$"""
		HOSTER_WEB_REMOVED=0
		for hoster_web_link in /etc/nginx/conf.d/*.conf; do
			[ -L "$hoster_web_link" ] || continue
			if hoster_web_owned "$hoster_web_link" "$HOSTER_WEB_ROOT/.web/nginx/${hoster_web_link##*/}"; then
				rm -f -- "$hoster_web_link"
				HOSTER_WEB_REMOVED=1
			fi
		done
		if [ "$HOSTER_WEB_REMOVED" = 1 ] || {{(current ? "true" : "[ -d \"$HOSTER_WEB_TARGET/.web\" ]")}}; then
			if hoster_web_activation; then
				if ! hoster_web_check ''; then
					printf '%s\n' {{Quote(string.Format(Properties.Resources.Web_Load_Message, string.Empty))}} >&2
				fi
			fi
		fi
		""";

		private static string Check() => $$"""
		hoster_web_check() {
			command -v nginx >/dev/null 2>&1 || { printf '%s: nginx\n' {{Quote(string.Format(Properties.Resources.Web_Required_Message, string.Empty))}} >&2; return 1; }
			command -v systemctl >/dev/null 2>&1 || { printf '%s: systemctl\n' {{Quote(string.Format(Properties.Resources.Web_Required_Message, string.Empty))}} >&2; return 1; }
			nginx -t -c /etc/nginx/nginx.conf || return 1
			if [ -n "$1" ]; then
				hoster_web_dump=$(mktemp) || return 1
				if ! nginx -T -c /etc/nginx/nginx.conf > "$hoster_web_dump"; then
					rm -f -- "$hoster_web_dump"
					return 1
				fi
				if ! grep -F -x -- "# configuration file $1:" "$hoster_web_dump" >/dev/null; then
					rm -f -- "$hoster_web_dump"
					printf '%s: %s\n' {{Quote(string.Format(Properties.Resources.Web_Required_Message, string.Empty))}} "$1" >&2
					return 1
				fi
				rm -f -- "$hoster_web_dump" || return 1
			fi
			hoster_web_load=$(systemctl show nginx.service --property=LoadState --value) || return 1
			[ "$hoster_web_load" = loaded ] || { printf '%s: nginx.service/%s\n' {{Quote(string.Format(Properties.Resources.Web_Required_Message, string.Empty))}} "$hoster_web_load" >&2; return 1; }
			hoster_web_state=$(systemctl show nginx.service --property=ActiveState --value) || return 1
			case "$hoster_web_state" in
				active) systemctl reload nginx.service || return 1;;
				inactive|failed) printf '%s\n' 'nginx.service: inactive';;
				*) printf '%s: nginx.service/%s\n' {{Quote(string.Format(Properties.Resources.Web_Value_Message, string.Empty))}} "$hoster_web_state" >&2; return 1;;
			esac
		}
		""";
	}
}
