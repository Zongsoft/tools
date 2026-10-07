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
using System.Threading;
using System.Threading.Tasks;

using Zongsoft.Tools.Containerizer.Protocol;

namespace Zongsoft.Tools.Containerizer;

internal static class BuildStorage
{
	public static string CacheRoot => Path.Combine(OperatingSystem.IsWindows() ? Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData) :
		Path.IsPathFullyQualified(Environment.GetEnvironmentVariable("XDG_CACHE_HOME") ?? "") ? Environment.GetEnvironmentVariable("XDG_CACHE_HOME") :
		Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".cache"), "Zongsoft", "containerizer");

	public static bool IsSamePath(string left, string right) => NormalizePath(left) == NormalizePath(right);
	public static string GetBootstrapCache(string source, string profile, string cacheRoot = null) => Path.Combine(cacheRoot ?? CacheRoot, "bootstrap", GetProjectKey(source), profile);

	public static FileStream AcquireLock(string output, string cacheRoot = null)
	{
		var directory = Path.Combine(cacheRoot ?? CacheRoot, "locks");
		Files.CreatePrivateDirectory(directory);
		return new FileStream(Path.Combine(directory, $"{GetProjectKey(output)}.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
	}

	public static async Task<FileStream> AcquireLockAsync(string output, string cacheRoot, CancellationToken cancellation)
	{
		while(true)
		{
			cancellation.ThrowIfCancellationRequested();

			try { return AcquireLock(output, cacheRoot); }
			catch(IOException exception) when((exception.HResult & 0xffff) is 11 or 32 or 33)
			{
				await Task.Delay(200, cancellation);
			}
		}
	}

	public static void Publish(ContainerManifest manifest, string prepared, string archive, string defaults)
	{
		var targetArchive = Path.Combine(manifest["output"], Path.GetFileName(archive));
		var existing = File.Exists(manifest.ManifestPath);

		if(File.Exists(targetArchive) || existing && (manifest.InputPath == null ||
			!IsSamePath(manifest.InputPath, manifest.ManifestPath) || Files.Hash(manifest.ManifestPath) != manifest.InputHash ||
			manifest.IsComplete && Files.Hash(prepared) != manifest.InputHash))
			throw new ContainerizationException(2, Properties.Resources.NodeBuilder_3_Message);

		string[] names = defaults == null ? [Path.GetFileName(prepared), Path.GetFileName(archive)] : [Path.GetFileName(prepared), Path.GetFileName(archive), ".settings"];
		using var publisher = new ArtifactPublisher(manifest["output"], true, names);
		File.Copy(prepared, publisher.StagePath(names[0]));
		File.Copy(archive, publisher.StagePath(names[1]));

		if(defaults != null)
			File.Copy(defaults, publisher.StagePath(".settings"));

		// The output lock serializes tool builds; also detect edits made while copying the archive.
		if(File.Exists(targetArchive) ||
			existing && Files.Hash(manifest.ManifestPath) != manifest.InputHash ||
			!existing && File.Exists(manifest.ManifestPath))
			throw new ContainerizationException(2, Properties.Resources.NodeBuilder_3_Message);

		publisher.Commit();
	}

	private static string GetProjectKey(string source) => Files.HashText(NormalizePath(source));

	private static string NormalizePath(string path)
	{
		var value = Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
		return OperatingSystem.IsWindows() ? value.ToUpperInvariant() : value;
	}
}
