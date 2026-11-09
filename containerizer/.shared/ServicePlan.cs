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

internal sealed partial class ServicePlan
{
	#region 公共属性
	public string Id { get; set; }
	public string Kind { get; set; } = "infrastructure";
	public string Template { get; set; }
	public string TemplateHash { get; set; }
	public string Hostname { get; set; }
	public List<string> Aliases { get; set; } = [];
	public string ConfigurationHash { get; set; }
	public ImagePlan Image { get; set; } = new();
	public PackageInfo Package { get; set; }
	public string[] Entrypoint { get; set; }
	public string[] Command { get; set; }
	public string WorkingDirectory { get; set; }
	public string User { get; set; }
	public string Restart { get; set; } = "unless-stopped";
	public string StopSignal { get; set; } = "SIGTERM";
	public int StopSeconds { get; set; } = 30;
	public List<string> Dependencies { get; set; } = [];
	public List<PortMapping> Ports { get; set; } = [];
	public List<Binding> Bindings { get; set; } = [];
	public List<WebSite> Web { get; set; } = [];
	public List<Mount> Mounts { get; set; } = [];
	public HealthCheck Health { get; set; } = new();
	public string Memory { get; set; }
	public string Cpus { get; set; }
	public string LogSize { get; set; } = "10m";
	public string LogFiles { get; set; } = "3";
	#endregion
}
