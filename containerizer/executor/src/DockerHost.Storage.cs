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
	public async Task PrepareDirectoriesAsync(DeliveryBundle bundle, Installation installation, CancellationToken cancellation)
	{
		using var ownershipLock = store.AcquireHostLock();

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
				if(!GetOwnerRegex().IsMatch(owner[0]))
					throw new ContainerizationException(3, Properties.Resources.DockerHost_30_Message);

				await this.RunCommandAsync("chown", [owner[0], path], null, cancellation);
			}
		}

		var logs = store.GetLogPath(installation.Name);
		this.PrepareOwnedDirectory(installation, logs);
		Files.CreatePrivateDirectory(logs);
	}

	public async Task UninstallAsync(Installation installation, bool purge, CancellationToken cancellation)
	{
		foreach(var id in await this.GetContainerIdsAsync(installation.Name, null, cancellation))
			await this.RunCommandAsync(ENGINE, ["rm", "-f", "-v", id], null, cancellation);

		var networks = await this.RunCommandAsync(ENGINE, ["network", "ls", "--filter", $"label={OWNER_LABEL}={installation.Name}", "--format", "{{.ID}}"], null, cancellation);
		foreach(var id in SplitLines(networks))
			await this.RunCommandAsync(ENGINE, ["network", "rm", id], null, cancellation);

		if(!purge)
			return;

		foreach(var images in this.ReadReleasePlans(installation).SelectMany(plan => plan.Services).Select(service => service.Image).GroupBy(image => image.Reference, StringComparer.Ordinal))
		{
			var imageInspection = await runner.RunAsync(ENGINE, ["image", "inspect", images.Key], null, cancellation);

			if(imageInspection.ExitCode == 0)
			{
				using var json = JsonDocument.Parse(imageInspection.Output);
				if(!images.Any(image => image.Id == json.RootElement[0].GetProperty("Id").GetString()))
					throw new ContainerizationException(9, Properties.Resources.DockerHost_14_Message);

				await this.RunCommandAsync(ENGINE, ["image", "rm", images.Key], null, cancellation);
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

	#region 私有方法
	private void PrepareOwnedDirectory(Installation installation, string path)
	{
		this.ValidateOwnedPath(installation, path);
		Files.EnsureNoLinks(path);
		var record = installation.Directories.FirstOrDefault(item => item.Path == path);

		if(record == null)
		{
			foreach(var other in store.List().Where(item => item.Name != installation.Name))
			{
				if(other.Directories.Any(item => PathsOverlap(path, item.Path)))
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

	private void ValidateDataPath(string path, string name)
	{
		DeliveryBundle.ValidateLinuxPath(path);
		DeliveryBundle.ValidateIdentity(name);
		path = $"/{string.Join('/', path.Split('/', StringSplitOptions.RemoveEmptyEntries))}";

		var root = Installation.Paths.GetDataPath(name);
		if(path == root || path.StartsWith($"{root}/", StringComparison.Ordinal))
			return;

		if(_forbiddenDataPaths.Contains(path, StringComparer.Ordinal) ||
			_forbiddenDataPrefixes.Any(prefix => path.StartsWith(prefix, StringComparison.Ordinal)) ||
			this.OverlapsManagedPaths(path))
			throw new ContainerizationException(3, Properties.Resources.DockerHost_28_Message);
	}
	private async Task DeleteOwnedDirectoryAsync(Installation installation, Installation.OwnedDirectory directory, CancellationToken cancellation)
	{
		if(!Directory.Exists(directory.Path))
			return;

		this.ValidateOwnedPath(installation, directory.Path);
		Files.EnsureNoLinks(directory.Path);

		if(!directory.Created)
			throw new ContainerizationException(9, Properties.Resources.DockerHost_22_Message);

		var marker = Path.Combine(directory.Path, ".containerizer-owner");
		var hasInvalidOwnership = File.Exists(marker) ?
			File.ReadAllText(marker).Trim() != $"{installation.Name}/{directory.Token}" :
			Directory.EnumerateFileSystemEntries(directory.Path).Any();

		if(hasInvalidOwnership)
			throw new ContainerizationException(9, Properties.Resources.DockerHost_23_Message);

		await this.ValidateDeletionTreeAsync(directory.Path, cancellation);
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

	private async Task ValidateDeletionTreeAsync(string path, CancellationToken cancellation)
	{
		Files.ValidateTree(path);
		var output = await this.RunCommandAsync("findmnt", ["--json", "--list", "--output", "TARGET"], null, cancellation);

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
			store.StateRoot, store.LogRoot, store.CacheRoot, store.RuntimeRoot,
		];

		return roots.Any(root => PathsOverlap(path, root));
	}

	private static bool PathsOverlap(string left, string right) => left == right || left.StartsWith($"{right.TrimEnd('/')}/", StringComparison.Ordinal) || right.StartsWith($"{left.TrimEnd('/')}/", StringComparison.Ordinal);

	private void ValidateOwnedPath(Installation installation, string path)
	{
		if(path != store.GetLogPath(installation.Name) && path != store.GetCachePath(installation.Name))
			this.ValidateDataPath(path, installation.Name);
	}

	[GeneratedRegex(@"^\d+:\d+$")]
	private static partial Regex GetOwnerRegex();
	#endregion
}
