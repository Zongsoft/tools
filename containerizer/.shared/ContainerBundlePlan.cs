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
using System.Text.Json.Serialization;

namespace Zongsoft.Tools.Containerizer.Protocol;

internal sealed partial class ContainerBundlePlan
{
	public const int ProtocolVersion = 1;
	public const string FileName = "containerizer.json";

	#region 公共属性
	public int Schema { get; set; } = ProtocolVersion;
	public string Name { get; set; }
	public string Tag { get; set; }
	public string Version { get; set; }
	public string Distribution { get; set; }
	public string Architecture { get; set; }
	public string Project { get; set; }
	public string DataRoot { get; set; }
	public string SourceHash { get; set; }
	public string ToolVersion { get; set; }
	public List<ServicePlan> Services { get; set; } = [];
	public List<Migration> Migrations { get; set; } = [];
	public BootstrapPlan Bootstrap { get; set; } = new();
	public List<FileEntry> Files { get; set; } = [];
	#endregion
}
