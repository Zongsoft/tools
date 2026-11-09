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

internal sealed partial class Installation
{
	#region 公共属性
	public int Schema { get; set; } = ContainerBundlePlan.ProtocolVersion;
	public string Name { get; set; }
	public string DataRoot { get; set; }
	public string Current { get; set; }
	public string CurrentVersion { get; set; }
	public string Status { get; set; } = "Preparing";
	public bool IsInMaintenance { get; set; }
	public bool PurgeResourcesCompleted { get; set; }
	public Transaction Pending { get; set; }
	public List<string> Releases { get; set; } = [];
	public List<string> Residuals { get; set; } = [];
	public List<OwnedDirectory> Directories { get; set; } = [];
	public Dictionary<string, string> RestartPolicies { get; set; } = new(StringComparer.Ordinal);
	public Dictionary<string, MigrationResult> Migrations { get; set; } = new(StringComparer.Ordinal);
	#endregion

	#region 嵌套类型
	/// <summary>FHS locations on the target host; package paths inside images are independent.</summary>
	internal static class Paths
	{
		public const string StateDirectory = "/var/lib/containerizer";
		public const string DataDirectory = $"{StateDirectory}/data";
		public const string LogDirectory = "/var/log/containerizer";
		public const string CacheDirectory = "/var/cache/containerizer";
		public const string RuntimeDirectory = "/run/containerizer";
		public const string ExecutorPath = "/usr/local/bin/containerizer";

		public static string GetDataPath(string name) => $"{DataDirectory}/{name}";
	}
	#endregion
}
