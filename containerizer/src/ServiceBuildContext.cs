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
using System.Collections.Generic;

using Zongsoft.Tools.Containerizer.Protocol;

namespace Zongsoft.Tools.Containerizer;

internal sealed class ServiceBuildContext(ContainerManifest.Component component)
{
	#region 公共属性
	public ContainerManifest.Component Component { get; } = component ?? throw new ArgumentNullException(nameof(component));
	public ServicePlan Plan { get; init; } = new();
	public Dictionary<string, string> Environment { get; } = new(StringComparer.Ordinal);
	public Dictionary<string, string> Settings { get; } = new(StringComparer.OrdinalIgnoreCase);
	public Dictionary<string, (string Source, string RelativePath)> Configuration { get; } = new(StringComparer.Ordinal);
	public PackageReader.Descriptor Package => this.Component.Package;
	public HashSet<string> ManagedEnvironment { get; } = new(StringComparer.Ordinal);
	public List<string> MissingSettings { get; } = [];
	public string ImageRepository { get; init; }
	#endregion
}
