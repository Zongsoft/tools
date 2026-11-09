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

using System.Collections.Generic;

namespace Zongsoft.Tools.Containerizer.Protocol;

partial class ServicePlan
{
	#region 嵌套类型
	/// <summary>表示服务提供的 Web 站点及其入口绑定。</summary>
	internal sealed class WebSite
	{
		public string Application { get; set; }
		public string Name { get; set; }
		public List<string> Hosts { get; set; } = [];
		public List<string> ProbeHosts { get; set; } = [];
		public List<BindingInfo> Bindings { get; set; } = [];

		#region 嵌套类型
		/// <summary>表示站点入口的监听地址及关联的应用绑定身份。</summary>
		internal sealed class BindingInfo
		{
			public string Scheme { get; set; }
			public string Binding { get; set; }
			public string Endpoint { get; set; }
			public string Protocol { get; set; } = "tcp";
			public string Address { get; set; }
			public int Port { get; set; }
			public bool IsExplicitDefault { get; set; }
			public bool IsDefault { get; set; }
			public string Publication { get; set; }
		}
		#endregion
	}
	#endregion
}
