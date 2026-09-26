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
using System.IO;
using System.Linq;
using System.Text;
using System.Collections.Generic;

namespace Zongsoft.Tools.Packager.Web;

internal static partial class Installation
{
	#region 交付入口
	internal static void Attach(Package package, Configurator.Result result)
	{
		package.Web = result;
		Validate(package, false);

		foreach(var file in result.Files)
			package.Entries.AddGeneratedContent(file.Path, file.Content.Render(package.InstallPath), file.Mode);
	}

	internal static void Validate(Package package, bool includesGenerated = true)
	{
		if(package.Web == null)
			return;

		foreach(var file in package.Web.Files)
		{
			var target = Normalize(package.InstallPath + "/" + file.Path);

			foreach(var entry in package.Entries)
			{
				var path = Normalize(entry.Rooted || package is not Package.Tar ? "/" + entry.EntryName : package.InstallPath + "/" + entry.EntryName);
				if((path == target && (!includesGenerated || entry.Source != null || entry.IsDirectory)) || path.StartsWith(target + "/", StringComparison.Ordinal) || !entry.IsDirectory && target.StartsWith(path + "/", StringComparison.Ordinal))
					throw DefinitionException.Create("Conflict", new(entry.Source, Entry: file.Path), target);
			}
		}
	}

	internal static void ValidateEntry(Package package, string name, bool rooted, string source, bool directory)
	{
		if(package.Web == null)
			return;

		var path = Normalize(rooted || package is not Package.Tar ? "/" + name : package.InstallPath + "/" + name);

		foreach(var file in package.Web.Files)
		{
			var target = Normalize(package.InstallPath + "/" + file.Path);
			if(path == target || path.StartsWith(target + "/", StringComparison.Ordinal) || !directory && target.StartsWith(path + "/", StringComparison.Ordinal))
				throw DefinitionException.Create("Conflict", new(source, Entry: file.Path), target);
		}
	}

	internal static Scripts CreateScripts(Package package)
	{
		var current = package.Web?.Files.Single().Path;
		var setup = Context(package);
		var delivered = setup + "\nHOSTER_WEB_CHANGED=0\n" + Nginx.Prune(current);

		if(package is Package.Tar && package.Web != null)
		{
			foreach(var file in package.Web.Files.Where(item => item.Content.Relocatable))
				delivered += "\n" + Relocate(file);
		}

		return new(delivered,
			setup + "\n" + Nginx.Activate(current),
			setup + "\n" + Nginx.Deactivate(current != null),
			setup + "\n" + """
			if [ -e "$HOSTER_WEB_TARGET/.web" ] || [ -L "$HOSTER_WEB_TARGET/.web" ]; then
				rm -rf -- "$HOSTER_WEB_TARGET/.web"
			fi
			""");
	}
	#endregion

	#region 路径处理
	private static string Normalize(string path)
	{
		var parts = new List<string>();

		foreach(var part in path.Replace('\\', '/').Split('/', StringSplitOptions.RemoveEmptyEntries))
		{
			if(part == ".")
				continue;

			if(part == "..")
			{
				if(parts.Count == 0)
					throw DefinitionException.Create("Value", default, path);

				parts.RemoveAt(parts.Count - 1);
			}
			else
				parts.Add(part);
		}

		return "/" + string.Join('/', parts);
	}

	private static string Quote(string value) => "'" + value.Replace("'", "'\"'\"'") + "'";
	private static string Context(Package package)
	{
		var root = package is Package.Tar ? "\"${INSTALL_PATH}\"" : Quote(package.InstallPath);
		var target = package is Package.Tar ? "\"${TARGET}\"" : Quote(package.InstallPath);

		return $$"""
		HOSTER_WEB_ROOT=$(readlink -m -- {{root}})
		HOSTER_WEB_TARGET=$(readlink -m -- {{target}})
		case "$HOSTER_WEB_ROOT:$HOSTER_WEB_TARGET" in /:*|*:/|:*) exit 1;; esac
		for hoster_web_directory in "$HOSTER_WEB_TARGET/.web" "$HOSTER_WEB_TARGET/.web/nginx"; do
			if [ -L "$hoster_web_directory" ]; then
				printf '%s: %s\n' {{Quote(string.Format(Properties.Resources.Web_Conflict_Message, string.Empty))}} "$hoster_web_directory" >&2
				exit 1
			fi
		done
		hoster_web_activation() {
			case "${HOSTER_WEB_ACTIVATION-1}" in
				1|[Tt][Rr][Uu][Ee]) return 0;;
				0|[Ff][Aa][Ll][Ss][Ee]) return 1;;
				*) printf '%s: HOSTER_WEB_ACTIVATION=%s\n' {{Quote(string.Format(Properties.Resources.Web_Value_Message, string.Empty))}} "$HOSTER_WEB_ACTIVATION" >&2; exit 1;;
			esac
		}
		hoster_web_owned() {
			[ -L "$1" ] || return 1
			hoster_web_destination=$(readlink -m -- "$1") || return 1
			[ "$hoster_web_destination" = "$2" ]
		}
		""";
	}

	private static string Relocate(Configurator.Result.File file)
	{
		var builder = new StringBuilder();

		builder.AppendLine("case \"$HOSTER_WEB_ROOT\" in *'$'*|*'").AppendLine("'*) exit 1;; esac");
		builder.AppendLine("HOSTER_WEB_ESCAPED=$(printf '%s' \"$HOSTER_WEB_ROOT\" | sed 's/\\\\/\\\\\\\\/g; s/\"/\\\\\"/g')");
		builder.AppendLine($"hoster_web_file=\"$HOSTER_WEB_TARGET/{file.Path}\"");
		builder.AppendLine("hoster_web_temp=$(mktemp \"$hoster_web_file.XXXXXX\")");
		builder.AppendLine("trap 'rm -f -- \"$hoster_web_temp\"' EXIT HUP INT TERM");
		builder.AppendLine("{");

		foreach(var part in file.Content.Parts)
			builder.AppendLine(part.InstallRoot ? "printf '%s' \"$HOSTER_WEB_ESCAPED\"" : "printf '%b' " + Quote(string.Concat(Encoding.UTF8.GetBytes(part.Text).Select(value => "\\0" + Convert.ToString(value, 8).PadLeft(3, '0')))));

		builder.AppendLine("} > \"$hoster_web_temp\"");
		builder.AppendLine("chmod 0644 \"$hoster_web_temp\"");
		builder.AppendLine("mv -f -- \"$hoster_web_temp\" \"$hoster_web_file\"");
		builder.AppendLine("trap - EXIT HUP INT TERM");

		return builder.ToString();
	}
	#endregion

	#region 嵌套类型
	internal readonly record struct Scripts(string Delivered, string Activate, string Deactivate, string Cleanup);
	#endregion
}
