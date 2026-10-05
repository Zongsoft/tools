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

using Zongsoft.Tools.Containerizer.Protocol;

namespace Zongsoft.Tools.Containerizer;

internal static class BuildStorage
{
	public static string CacheRoot => Path.Combine(OperatingSystem.IsWindows() ? Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData) :
		Path.IsPathFullyQualified(Environment.GetEnvironmentVariable("XDG_CACHE_HOME") ?? "") ? Environment.GetEnvironmentVariable("XDG_CACHE_HOME") :
		Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".cache"), "Zongsoft", "containerizer");

	public static string ProjectKey(string source) => Files.HashText(Normalize(source));
	public static bool SamePath(string left, string right) => Normalize(left) == Normalize(right);
	public static string BootstrapCache(string source, string profile, string cacheRoot = null) => Path.Combine(cacheRoot ?? CacheRoot, "bootstrap", ProjectKey(source), profile);

	public static FileStream Lock(string output, string cacheRoot = null)
	{
		var directory = Path.Combine(cacheRoot ?? CacheRoot, "locks");
		Files.PrivateDirectory(directory);
		return new FileStream(Path.Combine(directory, $"{ProjectKey(output)}.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
	}

	public static void Publish(ContainerManifest manifest, string prepared, string archive, string defaults)
	{
		var targetArchive = Path.Combine(manifest["output"], Path.GetFileName(archive));
		var existing = File.Exists(manifest.ManifestPath);

		if(File.Exists(targetArchive) || existing && (manifest.Input == null ||
			!SamePath(manifest.Input, manifest.ManifestPath) || Files.Hash(manifest.ManifestPath) != manifest.InputHash ||
			manifest.IsGenerated && Files.Hash(prepared) != manifest.InputHash))
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

	private static string Normalize(string path)
	{
		var value = Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
		return OperatingSystem.IsWindows() ? value.ToUpperInvariant() : value;
	}
}
