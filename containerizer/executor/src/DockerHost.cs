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
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using System.Runtime.InteropServices;

using Zongsoft.Tools.Containerizer.Protocol;

namespace Zongsoft.Tools.Containerizer.Execution;

internal sealed partial class DockerHost(InstallationStore store, IProcessRunner runner, RegistryMirrors mirrors = null) : IInstallationHost
{
	#region 常量定义
	private const string ENGINE = BootstrapPlan.ENGINE;
	private const string OWNER = "org.zongsoft.containerizer.name";
	private const string SERVICE = "org.zongsoft.containerizer.service";
	#endregion

	#region 公共方法
	public async Task VerifyAsync(DeliveryBundle bundle, Installation installation, CancellationToken cancellation)
	{
		if(!OperatingSystem.IsLinux())
			throw new ContainerizationException(3, Properties.Resources.DockerHost_1_Message);

		var uid = await this.RunAsync("id", ["-u"], null, cancellation);
		if(uid.Trim() != "0")
			throw new ContainerizationException(3, Properties.Resources.DockerHost_2_Message);

		var target = RuntimeInformation.ProcessArchitecture switch { Architecture.X64 => "x64", Architecture.Arm64 => "arm64", _ => "unsupported" };
		if(bundle.Plan.Architecture != target)
			throw new ContainerizationException(3, Properties.Resources.DockerHost_3_Message);

		var os = File.ReadLines("/etc/os-release").Where(line => line.Contains('=')).Select(line => line.Split('=', 2)).ToDictionary(pair => pair[0], pair => pair[1].Trim('"'), StringComparer.Ordinal);
		var distribution = bundle.Plan.Distribution.Split('@');

		if(distribution.Length != 2 ||
		   os.GetValueOrDefault("ID") != distribution[0] ||
		   !(os.GetValueOrDefault("VERSION_ID") == distribution[1] || distribution[1] == "9" && os.GetValueOrDefault("VERSION_ID")?.StartsWith("9.", StringComparison.Ordinal) == true))
			throw new ContainerizationException(3, Properties.Resources.DockerHost_4_Message);

		var required = bundle.Plan.Files.Sum(file => file.Length) * 3;
		if(new DriveInfo(Path.GetPathRoot(store.Root)).AvailableFreeSpace < required)
			throw new ContainerizationException(3, Properties.Resources.DockerHost_5_Message);

		Files.NoLinks(store.Root);
		await this.CheckPortsAsync(bundle, installation, cancellation);

		foreach(var mount in bundle.Plan.Services.SelectMany(service => service.Mounts).Where(mount => !mount.ReadOnly && !mount.Temporary))
		{
			this.ValidateDataPath(mount.Source, bundle.Plan.Name);
			Files.NoLinks(mount.Source);
		}
	}

	public async Task<int> MigrateAsync(DeliveryBundle bundle, MigrationPlan migration, string state, string operation, string log, CancellationToken cancellation)
	{
		Files.PrivateDirectory(state);
		var result = await runner.RunAsync("sh", [Files.Below(bundle.Directory, migration.Script), operation, state], bundle.Directory, cancellation, 3600);

		// Do not persist arbitrary script output, which can contain credentials or SQL.
		if(log != null)
			Files.Write(log, $"migration={migration.Version}\noperation={operation}\nexit={result.ExitCode}\n");

		return result.ExitCode;
	}

	#endregion

	#region 私有方法
	private IEnumerable<DeliveryPlan> Plans(Installation installation)
	{
		foreach(var release in installation.Releases)
		{
			using var bundle = DeliveryBundle.Open(Path.Combine(store.GetApplicationPath(installation.Name), "releases", release, "assets"));
			yield return bundle.Plan;
		}
	}

	private async Task<string> RunAsync(string executable, IReadOnlyList<string> arguments, string directory, CancellationToken cancellation)
	{
		var result = await runner.RunAsync(executable, arguments, directory, cancellation);

		if(result.ExitCode != 0)
			throw new ContainerizationException(4, $"{string.Format(Properties.Resources.DockerHost_27_Message, executable, arguments[0], result.ExitCode)}{(string.IsNullOrWhiteSpace(result.Error) ? "" : $"{Environment.NewLine}{result.Error.Trim()}")}");

		return result.Output;
	}

	private static string[] Lines(string output) => output.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
	#endregion
}
