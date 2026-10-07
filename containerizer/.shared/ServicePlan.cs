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
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Zongsoft.Tools.Containerizer.Protocol;

internal sealed class ServicePlan
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
	public PackagePlan Package { get; set; }
	public string[] Entrypoint { get; set; }
	public string[] Command { get; set; }
	public string WorkingDirectory { get; set; }
	public string User { get; set; }
	public string Restart { get; set; } = "unless-stopped";
	public string StopSignal { get; set; } = "SIGTERM";
	public int StopSeconds { get; set; } = 30;
	public List<string> Dependencies { get; set; } = [];
	public List<PortPlan> Ports { get; set; } = [];
	public List<WebSitePlan> Web { get; set; } = [];
	public List<MountPlan> Mounts { get; set; } = [];
	public HealthPlan Health { get; set; } = new();
	public string Memory { get; set; }
	public string Cpus { get; set; }
	public string LogSize { get; set; } = "10m";
	public string LogFiles { get; set; } = "3";
	#endregion
}
