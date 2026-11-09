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

partial class ServicePlan
{
	#region 嵌套类型
	/// <summary>表示应用服务所使用的安装包及其运行时信息。</summary>
	internal sealed class PackageInfo
	{
		#region 公共属性
		public string Name { get; set; }
		public string Version { get; set; }
		public string Architecture { get; set; }
		public string Format { get; set; }
		public string Hash { get; set; }
		public string Runtime { get; set; }
		public string BaseImageReference { get; set; }
		#endregion
	}
	#endregion
}
