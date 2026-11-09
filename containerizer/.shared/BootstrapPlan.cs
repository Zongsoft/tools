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

internal sealed class BootstrapPlan
{
	#region 常量定义
	internal const string ENGINE = "docker";
	#endregion

	#region 公共属性
	public string Mode { get; set; } = "offline";
	public string Profile { get; set; }
	public string BaseImageReference { get; set; }
	public string EngineVersion { get; set; }
	public string ComposeVersion { get; set; }
	public string Metadata { get; set; }
	public string Repository { get; set; }
	public List<string> Requirements { get; set; } = [];
	public List<Package> Packages { get; set; } = [];
	#endregion

	#region 嵌套类型
	internal sealed class Package
	{
		#region 公共属性
		public string Name { get; set; }
		public string Version { get; set; }
		public string Architecture { get; set; }
		public string Path { get; set; }
		public string Url { get; set; }
		public string Hash { get; set; }
		public long Length { get; set; }
		public string Dependencies { get; set; }
		#endregion
	}
	#endregion
}
