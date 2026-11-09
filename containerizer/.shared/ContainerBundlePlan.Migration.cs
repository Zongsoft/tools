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

partial class ContainerBundlePlan
{
	#region 嵌套类型
	/// <summary>表示容器包携带的升迁归档及其执行脚本。</summary>
	internal sealed class Migration
	{
		#region 公共属性
		public string Version { get; set; }
		public string Archive { get; set; }
		public string Script { get; set; }
		public string Identity { get; set; }
		#endregion
	}
	#endregion
}
