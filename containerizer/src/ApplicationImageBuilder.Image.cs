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
	private static bool PrepareImage(string path, string dependences, PackageReader.Descriptor package, ServiceBuildContext source, string distribution, ImagePlan baseline, string runtime, string directory)
	{
		Files.PrivateDirectory(directory);

		var filename = Path.GetFileName(path);
		File.Copy(path, Path.Combine(directory, filename));

		if(package.Format == "tar")
			File.Copy($"{path[..^7]}.sh", Path.Combine(directory, $"{filename[..^7]}.sh"));

		var nativeInstall = package.Format switch
		{
			"deb" => $"apt-get update && apt-get install -y --no-install-recommends {Quote($"/input/{filename}")}",
			"rpm" => $"dnf install -y {Quote($"/input/{filename}")}",
			_ => $"sh {Quote($"/input/{filename[..^7]}.sh")}",
		};

		var debian = Distribution.IsDebian(distribution);
		if(package.Format == "deb" && !debian || package.Format == "rpm" && debian)
			throw new ContainerizationException(2, Properties.Resources.ApplicationBuilder_1_Message);

		var installRuntime = $"{ProbeInstall(distribution, source.HealthUsesCurl)}{RuntimeInstall(distribution, runtime)}";
		var root = TemplateCatalog.LinuxPath(source.Plan.WorkingDirectory);
		var ingress = (dependences ?? "").Split(';', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries).Contains("nginx", StringComparer.Ordinal);
		var dockerfile = new StringBuilder()
			.AppendLine($"FROM {baseline.Repository}@{baseline.Digest} AS installed")
			.AppendLine("ENV HOSTER_WEB_ACTIVATION=0")
			.AppendLine("COPY . /input/")
			.AppendLine($"RUN {installRuntime}{nativeInstall}");

		if(ingress)
			dockerfile.AppendLine($"RUN mkdir -p {Quote($"{root}/.web/nginx")}");

		dockerfile.Append("RUN rm -rf /input /tmp/* /var/tmp/* /var/log/* /var/cache/apt/* /var/lib/apt/lists/* /var/cache/dnf/* /var/lib/rpm /usr/lib/sysimage/rpm /var/lib/dpkg/info /etc/systemd/system /usr/lib/systemd/system; ")
			.AppendLine($"find {Quote(root)} -type f \\( -name '*.log' -o -name '*.bak' -o -name '*.old' \\) -delete")
			.AppendLine("FROM scratch AS final")
			.AppendLine("COPY --from=installed / /")
			.AppendLine($"WORKDIR {root}")
			.AppendLine($"ENTRYPOINT {JsonSerializer.Serialize(source.Plan.Entrypoint)}");
		Files.Write(Path.Combine(directory, "Dockerfile"), dockerfile.ToString(), true);

		return ingress;
	}

	private async Task ReadIngressAsync(ServiceBuildContext source, string image, string directory, BuildResources resources, CancellationToken cancellation)
	{
		var container = resources.Container();
		await engine.RunAsync(["create", "--name", container, image], directory, cancellation);
		var output = Path.Combine(directory, "ingress");
		Directory.CreateDirectory(output);
		await engine.RunAsync(["cp", $"{container}:{source.Plan.WorkingDirectory}/.web/nginx/.", output], directory, cancellation);

		foreach(var file in Directory.EnumerateFiles(output, "*.conf"))
			source.Ingress.Add(Path.GetFileName(file), file);
	}
	#endregion
}
