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

partial class DockerHost
{
	#region 静态字段
	private static readonly string[] _forbiddenDataPaths = ["/", "/bin", "/boot", "/dev", "/etc", "/home", "/lib", "/lib64", "/proc", "/root", "/run", "/sbin", "/sys", "/usr", "/var", "/var/lib", "/var/log", "/var/cache", "/srv", "/tmp"];
	private static readonly string[] _forbiddenDataPrefixes = ["/bin/", "/boot/", "/dev/", "/etc/", "/lib/", "/lib64/", "/proc/", "/root/", "/run/", "/sbin/", "/sys/", "/usr/"];
	#endregion

	#region 公共方法
	public async Task DirectoriesAsync(DeliveryBundle bundle, Installation installation, CancellationToken cancellation)
	{
		using var ownershipLock = store.HostLock();

		var paths = bundle.Plan.Services
			.SelectMany(service => service.Mounts)
			.Where(mount => !mount.ReadOnly && mount.Owned && !mount.Temporary)
			.Select(mount => mount.Source)
			.Prepend(bundle.Plan.DataRoot)
			.Distinct(StringComparer.Ordinal)
			.OrderBy(path => path.Length);

		foreach(var path in paths)
		{
			this.ValidateDataPath(path, installation.Name);
			this.PrepareOwnedDirectory(installation, path);
			var owner = bundle.Plan.Services.SelectMany(service => service.Mounts).Where(mount => mount.Source == path && !mount.ReadOnly).Select(mount => mount.User).Distinct(StringComparer.Ordinal).ToArray();

			if(owner.Length > 1)
				throw new ContainerizationException(3, Properties.Resources.DockerHost_29_Message);

			if(owner.Length == 1 && owner[0] != null)
			{
				if(!OwnerRegex().IsMatch(owner[0]))
					throw new ContainerizationException(3, Properties.Resources.DockerHost_30_Message);

				await this.RunAsync("chown", [owner[0], path], null, cancellation);
			}
		}

		var logs = store.GetLogPath(installation.Name);
		this.PrepareOwnedDirectory(installation, logs);
		Files.PrivateDirectory(logs);
	}

	public async Task UninstallAsync(Installation installation, bool purge, CancellationToken cancellation)
	{
		await this.StopApplicationsAsync(installation, cancellation);

		foreach(var id in await this.ContainersAsync(installation.Name, null, cancellation))
			await this.RunAsync("docker", ["rm", "-f", "-v", id], null, cancellation);

		var networks = await this.RunAsync("docker", ["network", "ls", "--filter", $"label={OWNER}={installation.Name}", "--format", "{{.ID}}"], null, cancellation);
		foreach(var id in Lines(networks))
			await this.RunAsync("docker", ["network", "rm", id], null, cancellation);

		if(!purge)
			return;

		foreach(var images in this.Plans(installation).SelectMany(plan => plan.Services).Select(service => service.Image).GroupBy(image => image.Tag, StringComparer.Ordinal))
		{
			var exists = await runner.RunAsync("docker", ["image", "inspect", images.Key], null, cancellation);

			if(exists.ExitCode == 0)
			{
				using var json = JsonDocument.Parse(exists.Output);
				if(!images.Any(image => image.Id == json.RootElement[0].GetProperty("Id").GetString()))
					throw new ContainerizationException(9, Properties.Resources.DockerHost_14_Message);

				await this.RunAsync("docker", ["image", "rm", images.Key], null, cancellation);
			}
		}

		foreach(var directory in installation.Directories.Where(item => item.Path != installation.DataRoot).OrderByDescending(item => item.Path.Length))
		{
			await this.DeleteOwnedDirectoryAsync(installation, directory, cancellation);
		}
	}

	public async Task FinalizePurgeAsync(Installation installation, CancellationToken cancellation)
	{
		if(!Directory.Exists(installation.DataRoot))
			return;

		var directory = installation.Directories.SingleOrDefault(item => item.Path == installation.DataRoot);
		if(directory == null || !directory.Created)
			throw new ContainerizationException(9, Properties.Resources.DockerHost_22_Message);

		await this.DeleteOwnedDirectoryAsync(installation, directory, cancellation);
	}
	#endregion

	#region 内部方法
	internal void PrepareOwnedDirectory(Installation installation, string path)
	{
		this.ValidateOwnedPath(installation, path);
		Files.NoLinks(path);
		var record = installation.Directories.FirstOrDefault(item => item.Path == path);

		if(record == null)
		{
			foreach(var other in store.List().Where(item => item.Name != installation.Name))
			{
				if(other.Directories.Any(item => Overlap(path, item.Path)))
					throw new ContainerizationException(3, Properties.Resources.DockerHost_15_Message);
			}

			if(Directory.Exists(path) && Directory.EnumerateFileSystemEntries(path).Any())
				throw new ContainerizationException(3, Properties.Resources.DockerHost_16_Message);

			record = new() { Path = path, Token = Guid.NewGuid().ToString("N"), Created = !Directory.Exists(path) };
			installation.Directories.Add(record);

			store.Save(installation);
		}

		Directory.CreateDirectory(path);
		var marker = Path.Combine(path, ".containerizer-owner");

		if(!File.Exists(marker) && Directory.EnumerateFileSystemEntries(path).Any())
			throw new ContainerizationException(3, Properties.Resources.DockerHost_16_Message);
		if(File.Exists(marker) && File.ReadAllText(marker).Trim() != $"{installation.Name}/{record.Token}")
			throw new ContainerizationException(3, Properties.Resources.DockerHost_17_Message);

		Files.Write(marker, $"{installation.Name}/{record.Token}");
	}

	internal void ValidateDataPath(string path, string name)
	{
		DeliveryBundle.LinuxPath(path);
		DeliveryBundle.Identity(name);
		path = $"/{string.Join('/', path.Split('/', StringSplitOptions.RemoveEmptyEntries))}";

		var root = Installation.Paths.GetDataPath(name);
		if(path == root || path.StartsWith($"{root}/", StringComparison.Ordinal))
			return;

		if(_forbiddenDataPaths.Contains(path, StringComparer.Ordinal) ||
			_forbiddenDataPrefixes.Any(prefix => path.StartsWith(prefix, StringComparison.Ordinal)) ||
			this.OverlapsManagedPaths(path))
			throw new ContainerizationException(3, Properties.Resources.DockerHost_28_Message);
	}
	#endregion

	#region 私有方法
	private async Task DeleteOwnedDirectoryAsync(Installation installation, Installation.OwnedDirectory directory, CancellationToken cancellation)
	{
		if(!Directory.Exists(directory.Path))
			return;

		this.ValidateOwnedPath(installation, directory.Path);
		Files.NoLinks(directory.Path);

		if(!directory.Created)
			throw new ContainerizationException(9, Properties.Resources.DockerHost_22_Message);

		var marker = Path.Combine(directory.Path, ".containerizer-owner");
		var unowned = File.Exists(marker) ?
			File.ReadAllText(marker).Trim() != $"{installation.Name}/{directory.Token}" :
			Directory.EnumerateFileSystemEntries(directory.Path).Any();

		if(unowned)
			throw new ContainerizationException(9, Properties.Resources.DockerHost_23_Message);

		await this.CheckDeleteTreeAsync(directory.Path, cancellation);
		foreach(var entry in Directory.EnumerateFileSystemEntries(directory.Path).Where(path => path != marker))
		{
			if(Directory.Exists(entry))
				Directory.Delete(entry, true);
			else
				File.Delete(entry);
		}

		File.Delete(marker);
		Directory.Delete(directory.Path);
	}

	private async Task CheckDeleteTreeAsync(string path, CancellationToken cancellation)
	{
		Files.CheckTree(path);
		var output = await this.RunAsync("findmnt", ["--json", "--list", "--output", "TARGET"], null, cancellation);

		using var json = JsonDocument.Parse(output);
		var mounts = json.RootElement.GetProperty("filesystems");

		foreach(var mount in mounts.EnumerateArray())
		{
			var target = mount.GetProperty("target").GetString();

			if(target == path || target.StartsWith($"{path.TrimEnd('/')}/", StringComparison.Ordinal))
				throw new ContainerizationException(9, Properties.Resources.DockerHost_26_Message);
		}
	}

	private bool OverlapsManagedPaths(string path)
	{
		string[] roots =
		[
			Installation.Paths.StateDirectory, Installation.Paths.LogDirectory, Installation.Paths.CacheDirectory,
			store.Root, store.LogRoot, store.CacheRoot, store.RuntimeRoot,
		];

		return roots.Any(root => Overlap(path, root));
	}

	private static bool Overlap(string left, string right) => left == right || left.StartsWith($"{right.TrimEnd('/')}/", StringComparison.Ordinal) || right.StartsWith($"{left.TrimEnd('/')}/", StringComparison.Ordinal);

	private void ValidateOwnedPath(Installation installation, string path)
	{
		if(path != store.GetLogPath(installation.Name) && path != store.GetCachePath(installation.Name))
			this.ValidateDataPath(path, installation.Name);
	}

	[GeneratedRegex(@"^\d+:\d+$")]
	private static partial Regex OwnerRegex();
	#endregion
}
