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

internal sealed class InstallationStore(string stateRoot = Installation.Paths.StateDirectory, string logRoot = Installation.Paths.LogDirectory, string cacheRoot = Installation.Paths.CacheDirectory, string runtimeRoot = Installation.Paths.RuntimeDirectory)
{
	#region 公共属性
	public string StateRoot { get; } = Path.GetFullPath(stateRoot);
	public string LogRoot { get; } = Path.GetFullPath(logRoot);
	public string CacheRoot { get; } = Path.GetFullPath(cacheRoot);
	public string RuntimeRoot { get; } = Path.GetFullPath(runtimeRoot);
	#endregion

	#region 公共方法
	public string GetApplicationPath(string name)
	{
		DeliveryBundle.ValidateIdentity(name);
		return Files.ResolveRelativePath(this.StateRoot, $"apps/{name}");
	}

	public string GetStatePath(string name) => Path.Combine(this.GetApplicationPath(name), "installation.json");
	public string GetMigrationPath(string name, string version) => Files.ResolveRelativePath(this.GetApplicationPath(name), $"migrations/{version}");
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
		Files.CreatePrivateDirectory(this.GetApplicationPath(installation.Name));
		Files.Save(this.GetStatePath(installation.Name), installation, ProtocolJson.Default.Installation);
	}

	public void SaveHistory(Installation installation) => Files.Save(
		Files.ResolveRelativePath(this.GetApplicationPath(installation.Name), $"releases/{installation.Current}/history.json"),
		installation, ProtocolJson.Default.Installation);

	public void DeleteAssets(string name)
	{
		var root = this.GetApplicationPath(name);
		Files.EnsureNoLinks(root);

		// Keep the registry until every asset has been removed; the lock lives outside this directory.
		foreach(var path in Directory.EnumerateFileSystemEntries(root).Where(path => path != this.GetStatePath(name)))
		{
			Files.EnsureNoLinks(path);

			if(Directory.Exists(path))
			{
				Files.ValidateTree(path);
				Directory.Delete(path, true);
			}
			else
				File.Delete(path);
		}
	}

	public void DeleteRegistration(string name)
	{
		File.Delete(this.GetStatePath(name));
		Directory.Delete(this.GetApplicationPath(name));
	}

	public FileStream AcquireApplicationLock(string name)
	{
		DeliveryBundle.ValidateIdentity(name);
		return this.AcquireLock($"{name}.lock");
	}

	public FileStream AcquireHostLock() => this.AcquireLock(".host.lock");
	public IEnumerable<Installation> List()
	{
		var directory = Path.Combine(this.StateRoot, "apps");
		if(!Directory.Exists(directory))
			yield break;

		Files.EnsureNoLinks(directory);
		foreach(var app in Directory.EnumerateDirectories(directory).Order(StringComparer.Ordinal))
			yield return this.Load(Path.GetFileName(app));
	}

	public string StageBundle(DeliveryBundle bundle)
	{
		var releases = Path.Combine(this.GetApplicationPath(bundle.Plan.Name), "releases");
		Files.CreatePrivateDirectory(releases);
		var destination = Path.Combine(releases, bundle.Id);

		if(Directory.Exists(destination))
		{
			using var existing = DeliveryBundle.Open(Path.Combine(destination, "assets"));
			if(existing.Id != bundle.Id)
				throw new ContainerizationException(4, Properties.Resources.InstallationStore_4_Message);

			SetReadOnlyMountPermissions(existing);
			return Path.Combine(destination, "assets");
		}

		var staging = Path.Combine(releases, $".stage-{Guid.NewGuid().ToString("N")}");
		Files.CreatePrivateDirectory(staging);

		try
		{
			Files.CopyTree(bundle.Directory, Path.Combine(staging, "assets"));
			using var verified = DeliveryBundle.Open(Path.Combine(staging, "assets"));
			SetReadOnlyMountPermissions(verified);

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
		DeliveryBundle.ValidateIdentity(name);
		return Files.ResolveRelativePath(root, name);
	}

	private static void SetReadOnlyMountPermissions(DeliveryBundle bundle)
	{
		if(OperatingSystem.IsWindows())
			return;

		foreach(var mount in bundle.Plan.Services.SelectMany(service => service.Mounts).Where(mount => mount.ReadOnly))
		{
			var path = Files.ResolveRelativePath(bundle.Directory, mount.Source);
			foreach(var file in Directory.Exists(path) ? Files.EnumerateFiles(path) : [path])
				File.SetUnixFileMode(file, UnixFileMode.UserRead | UnixFileMode.GroupRead | UnixFileMode.OtherRead);
		}
	}

	private FileStream AcquireLock(string filename)
	{
		var directory = this.RuntimeRoot;
		Files.EnsureNoLinks(directory);
		Files.CreatePrivateDirectory(directory);

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
