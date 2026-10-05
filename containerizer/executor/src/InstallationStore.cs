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
using System.Collections.Generic;

using Zongsoft.Tools.Containerizer.Protocol;

namespace Zongsoft.Tools.Containerizer.Execution;

internal sealed class InstallationStore(string root = Installation.Paths.StateDirectory, string logs = Installation.Paths.LogDirectory, string cache = Installation.Paths.CacheDirectory, string runtime = Installation.Paths.RuntimeDirectory)
{
	#region 公共属性
	public string Root { get; } = Path.GetFullPath(root);
	public string LogRoot { get; } = Path.GetFullPath(logs);
	public string CacheRoot { get; } = Path.GetFullPath(cache);
	public string RuntimeRoot { get; } = Path.GetFullPath(runtime);
	#endregion

	#region 公共方法
	public string GetApplicationPath(string name)
	{
		DeliveryBundle.Identity(name);
		return Files.Below(this.Root, $"apps/{name}");
	}

	public string GetStatePath(string name) => Path.Combine(this.GetApplicationPath(name), "installation.json");
	public string GetMigrationPath(string name, string version) => Files.Below(this.GetApplicationPath(name), $"migrations/{version}");
	public string GetLogPath(string name) => this.GetOwnedPath(this.LogRoot, name);
	public string GetCachePath(string name) => this.GetOwnedPath(this.CacheRoot, name);
	public Installation Load(string name, bool required = true)
	{
		var path = this.GetStatePath(name);
		if(!File.Exists(path))
			return required ? throw new ContainerizationException(2, Properties.Resources.InstallationStore_1_Message) : null;

		var installation = Files.Load(path, ProtocolJson.Default.Installation);
		if(installation.Schema != DeliveryPlan.ProtocolVersion || installation.Name != name)
			throw new ContainerizationException(3, Properties.Resources.InstallationStore_2_Message);

		return installation;
	}

	public void Save(Installation installation)
	{
		Files.PrivateDirectory(this.GetApplicationPath(installation.Name));
		Files.Save(this.GetStatePath(installation.Name), installation, ProtocolJson.Default.Installation);
	}

	public FileStream Lock(string name)
	{
		DeliveryBundle.Identity(name);
		return this.AcquireLock($"{name}.lock");
	}

	public FileStream HostLock() => this.AcquireLock(".host.lock");
	public IEnumerable<Installation> List()
	{
		var directory = Path.Combine(this.Root, "apps");
		if(!Directory.Exists(directory))
			yield break;

		Files.NoLinks(directory);
		foreach(var app in Directory.EnumerateDirectories(directory).Order(StringComparer.Ordinal))
			yield return this.Load(Path.GetFileName(app));
	}

	public string Stage(DeliveryBundle bundle)
	{
		var releases = Path.Combine(this.GetApplicationPath(bundle.Plan.Name), "releases");
		Files.PrivateDirectory(releases);
		var destination = Path.Combine(releases, bundle.Id);

		if(Directory.Exists(destination))
		{
			using var existing = DeliveryBundle.Open(Path.Combine(destination, "assets"));
			if(existing.Id != bundle.Id)
				throw new ContainerizationException(4, Properties.Resources.InstallationStore_4_Message);

			ReadOnlyMounts(existing);
			return Path.Combine(destination, "assets");
		}

		var staging = Path.Combine(releases, $".stage-{Guid.NewGuid().ToString("N")}");
		Files.PrivateDirectory(staging);

		try
		{
			Files.CopyTree(bundle.Directory, Path.Combine(staging, "assets"));
			using var verified = DeliveryBundle.Open(Path.Combine(staging, "assets"));
			ReadOnlyMounts(verified);

			Directory.Move(staging, destination);
			return Path.Combine(destination, "assets");
		}
		finally
		{
			if(Directory.Exists(staging))
				Directory.Delete(staging, true);
		}
	}

	#endregion

	#region 私有方法
	private string GetOwnedPath(string root, string name)
	{
		DeliveryBundle.Identity(name);
		return Files.Below(root, name);
	}

	private static void ReadOnlyMounts(DeliveryBundle bundle)
	{
		if(OperatingSystem.IsWindows())
			return;

		foreach(var mount in bundle.Plan.Services.SelectMany(service => service.Mounts).Where(mount => mount.ReadOnly))
		{
			var path = Files.Below(bundle.Directory, mount.Source);
			foreach(var file in Directory.Exists(path) ? Files.Enumerate(path) : [path])
				File.SetUnixFileMode(file, UnixFileMode.UserRead | UnixFileMode.GroupRead | UnixFileMode.OtherRead);
		}
	}

	private FileStream AcquireLock(string filename)
	{
		var directory = this.RuntimeRoot;
		Files.NoLinks(directory);
		Files.PrivateDirectory(directory);

		try
		{
			return new FileStream(Path.Combine(directory, filename), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
		}
		catch(IOException exception)
		{
			throw new ContainerizationException(8, Properties.Resources.InstallationStore_3_Message, exception);
		}
	}
	#endregion
}
