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

using System.Net;
using System.Collections.Generic;

namespace Zongsoft.Tools.Containerizer.Protocol;

partial class ServicePlan
{
	#region 嵌套类型
	/// <summary>表示服务在宿主地址上的端口映射及其关联的应用绑定。</summary>
	internal sealed class PortMapping
	{
		#region 公共属性
		public string Name { get; set; }
		public int Host { get; set; }
		public int Container { get; set; }
		public string Protocol { get; set; } = "tcp";
		public string Address { get; set; } = "127.0.0.1";
		public List<BindingReference> Bindings { get; set; } = [];
		#endregion

		#region 公共方法
		public static bool Conflicts(PortMapping left, PortMapping right) =>
			left.Host == right.Host && left.Protocol == right.Protocol &&
			IPAddress.TryParse(left.Address, out var first) &&
			IPAddress.TryParse(right.Address, out var second) &&
			first.AddressFamily == second.AddressFamily &&
			(
				first.Equals(second) ||
				first.Equals(IPAddress.Any) ||
				first.Equals(IPAddress.IPv6Any) ||
				second.Equals(IPAddress.Any) ||
				second.Equals(IPAddress.IPv6Any)
			);
		#endregion

		#region 嵌套类型
		/// <summary>表示端口映射关联的应用、绑定及原始端点身份。</summary>
		internal sealed class BindingReference
		{
			public string Application { get; set; }
			public string Binding { get; set; }
			public string Endpoint { get; set; }
		}
		#endregion
	}
	#endregion
}
