/*
 *   _____                                ______
 *  /_   /  ____  ____  ____  _________  / __/ /_
 *    / /  / __ \/ __ \/ __ \/ ___/ __ \/ /_/ __/
 *   / /__/ /_/ / / / / /_/ /\_ \/ /_/ / __/ /_
 *  /____/\____/_/ /_/\__  /____/\____/_/  \__/
 *                   /____/
 *
 * Authors:
 *   钟峰(Popeye Zhong) <zongsoft@qq.com>
 *
 * Copyright (c) Zongsoft Studio. All rights reserved.
 * The above copyright notice and this permission notice shall be included in all
 * AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
 *
 *
 * Licensed under the MIT License (see LICENSE).
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
		var profile = Profile.Load(path, new ProfileOptions { Directives = { ProfileDirectiveOptions.Import(ProfileDirectiveBehavior.Suppress) } });
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
