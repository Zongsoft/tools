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
using System.Collections.Generic;

using Zongsoft.Configuration.Profiles;
using Zongsoft.Tools.Containerizer.Protocol;

namespace Zongsoft.Tools.Containerizer;

internal static class RegistryMirrorSettings
{
	public static RegistryMirrors Read(string directory)
	{
		var mirrors = new RegistryMirrors();
		var path = Path.Combine(directory, RegistryMirrors.FILE_NAME);
		if(!File.Exists(path))
			return mirrors;

		ContainerManifest.ValidateLines(path, new HashSet<string>(StringComparer.OrdinalIgnoreCase));
		var profile = Profile.Load(path, new ProfileOptions { ImportBehavior = ProfileDirectiveBehavior.Suppress });
		if(profile.Sections.Count != 0)
			throw new ContainerizationException(2, string.Format(Properties.Resources.Mirrors_File_Message, path));

		foreach(var entry in profile.Entries)
		{
			var locations = entry.Value?.Split(';', StringSplitOptions.TrimEntries);
			if(!RegistryMirrors.IsRegistry(entry.Name) || locations == null || locations.Length == 0 || Array.Exists(locations, value => !RegistryMirrors.IsLocation(value)))
				throw new ContainerizationException(2, string.Format(Properties.Resources.Manifest_16_Message, path, entry.LineNumber + 1, Properties.Resources.Mirrors_Invalid_Message));

			mirrors.Registries.Add(entry.Name, locations);
		}

		return mirrors;
	}
}
