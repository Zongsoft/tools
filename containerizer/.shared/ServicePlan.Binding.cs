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

using System.Collections.Generic;

namespace Zongsoft.Tools.Containerizer.Protocol;

partial class ServicePlan
{
	#region 嵌套类型
	/// <summary>表示服务来源安装包声明的绑定及其端点、发布策略和代理连接。</summary>
	internal sealed class Binding
	{
		#region 公共属性
		public string Id { get; set; }
		public string Owner { get; set; }
		public List<string> Aliases { get; set; } = [];
		public List<Endpoint> Endpoints { get; set; } = [];
		public bool IsProxied { get; set; }
		public string Publication { get; set; } = "auto";
		public List<Connection> Connections { get; set; } = [];
		#endregion

		#region 嵌套类型
		/// <summary>表示绑定声明的原始监听端点。</summary>
		internal sealed class Endpoint
		{
			public string Id { get; set; }
			public string Scheme { get; set; }
			public string Host { get; set; }
			public int Port { get; set; }
			public string Path { get; set; }
			public List<string> Protocols { get; set; } = [];
			public List<string> Transports { get; set; } = [];
		}

		/// <summary>表示代理连接使用的原始端点、选定端点及连接地址。</summary>
		internal sealed class Connection
		{
			public string Endpoint { get; set; }
			public string SelectedEndpoint { get; set; }
			public string Host { get; set; }
			public int Port { get; set; }
			public string Protocol { get; set; }
		}
		#endregion
	}
	#endregion
}
