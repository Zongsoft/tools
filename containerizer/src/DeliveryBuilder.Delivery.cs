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
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Globalization;
using System.Collections.Generic;

using Zongsoft.Tools.Containerizer.Protocol;

namespace Zongsoft.Tools.Containerizer;

partial class DeliveryBuilder
{
	#region 私有方法
	private static string FindExecutor(string architecture, string supplied)
	{
		string[] roots = [Path.Combine(AppContext.BaseDirectory, ".containerizer"), Path.Combine(AppContext.BaseDirectory, "..", "..", ".containerizer")];
		IEnumerable<string> candidates = supplied != null ? [supplied] :
			roots.Select(root => Path.GetFullPath(Path.Combine(root, $"linux-{architecture}", "containerizer")));

		foreach(var path in candidates)
		{
			if(File.Exists(path))
			{
				using var stream = File.OpenRead(path);
				var header = new byte[20];
				stream.ReadExactly(header);

				if(header[0] != 0x7f || header[1] != 'E' || header[2] != 'L' || header[3] != 'F' ||
				header[4] != 2 || header[5] != 1 ||
				BitConverter.ToUInt16(header, 18) != (architecture == "arm64" ? 183 : 62))
					throw new ContainerizationException(3, Properties.Resources.NodeBuilder_4_Message);

				return path;
			}
		}

		throw new ContainerizationException(3, Properties.Resources.NodeBuilder_5_Message);
	}

	private static string Tag(DeliveryPlan node, string service) => $"containerizer/{node.Project}/{service}:{Files.HashText($"{node.Version}/{node.Tag}")[..16]}";

	private static string Launcher(string command) => $"#!/bin/sh\nset -eu\nbase=$(CDPATH= cd -- \"$(dirname -- \"$0\")\" && pwd -P)\nif [ \"$(id -u)\" -ne 0 ]; then printf 'Run as root.\\n' >&2; exit 3; fi\nexec \"$base/containerizer\" {command}{(command == "install" ? " \"$base\"" : " --from \"$base\"")} \"$@\"\n";

	private static string Instructions(DeliveryPlan node, string release) => FormatInstructions(node, release, CultureInfo.InvariantCulture);

	private static string InstructionsZhHans(DeliveryPlan node, string release) => FormatInstructions(node, release, CultureInfo.GetCultureInfo("zh-Hans"));

	private static string FormatInstructions(DeliveryPlan node, string release, CultureInfo culture)
	{
		var previous = CultureInfo.CurrentUICulture;

		try
		{
			CultureInfo.CurrentUICulture = culture;
			return string.Format(CultureInfo.InvariantCulture,
				Properties.Resources.NodeBuilder_Readme,
				node.Name,
				node.Version,
				node.Distribution,
				node.Architecture,
				string.Join(culture.Name == "zh-Hans" ? "、" : ", ", node.Services.Select(service => $"{service.Id}@{(service.Package?.Version ?? service.Image.Version)}")),
				string.Join(culture.Name == "zh-Hans" ? "、" : ", ", node.Migrations.Select(migration => migration.Version)),
				release);
		}
		finally
		{
			CultureInfo.CurrentUICulture = previous;
		}
	}
	#endregion
}
