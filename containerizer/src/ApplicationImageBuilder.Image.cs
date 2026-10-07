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
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

using Zongsoft.Tools.Containerizer.Protocol;

namespace Zongsoft.Tools.Containerizer;

partial class ApplicationImageBuilder
{
	#region 私有方法
	private static void PrepareImage(string path, PackageReader.Descriptor package, ServiceBuildContext source, string distribution, string directory)
	{
		var input = Path.Combine(directory, "input");
		Files.CreatePrivateDirectory(input);

		var filename = Path.GetFileName(path);
		File.Copy(path, Path.Combine(input, filename));

		if(package.Format == "tar")
			File.Copy($"{path[..^7]}.sh", Path.Combine(input, $"{filename[..^7]}.sh"));

		var nativeInstall = package.Format switch
		{
			"deb" => $"apt-get update && apt-get install -y --no-install-recommends {ShellUtility.QuoteArgument($"/input/{filename}")}",
			"rpm" => $"dnf install -y {ShellUtility.QuoteArgument($"/input/{filename}")}",
			_ => $"sh {ShellUtility.QuoteArgument($"/input/{filename[..^7]}.sh")}",
		};

		var debian = Distribution.IsDebianFamily(distribution);
		if(package.Format == "deb" && !debian || package.Format == "rpm" && debian)
			throw new ContainerizationException(2, Properties.Resources.ApplicationBuilder_1_Message);

		var root = Files.NormalizeLinuxPath(source.Plan.WorkingDirectory);
		var dockerfile = new StringBuilder()
			.AppendLine("FROM scratch AS installed")
			.AppendLine("ADD runtime.tar /")
			.AppendLine(File.ReadAllText(Path.Combine(directory, "runtime.env")))
			.AppendLine("ENV HOSTER_WEB_ACTIVATION=0")
			.AppendLine("COPY input/ /input/")
			.AppendLine($"RUN {nativeInstall}");

		dockerfile.Append("RUN rm -rf /input /tmp/* /var/tmp/* /var/log/* /var/cache/apt/* /var/lib/apt/lists/* /var/cache/dnf/* /var/lib/rpm /usr/lib/sysimage/rpm /var/lib/dpkg/info /etc/systemd/system /usr/lib/systemd/system; ")
			.AppendLine($"find {ShellUtility.QuoteArgument(root)} -type f \\( -name '*.log' -o -name '*.bak' -o -name '*.old' \\) -delete")
			.AppendLine("FROM scratch AS final")
			.AppendLine("COPY --from=installed / /")
			.AppendLine($"WORKDIR {root}")
			.AppendLine($"ENTRYPOINT {JsonSerializer.Serialize(source.Plan.Entrypoint)}");
		Files.Write(Path.Combine(directory, "Dockerfile"), dockerfile.ToString(), true);

	}
	#endregion
}
