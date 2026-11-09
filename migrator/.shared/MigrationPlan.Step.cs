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

namespace Zongsoft.Tools.Migrator.Migration;

public sealed partial class MigrationPlan
{
	public sealed class Step
	{
		#region 公共属性
		public string Provider { get; set; }
		public int? DatabaseIndex { get; set; }
		public Dictionary<string, string> Settings { get; set; } = new(StringComparer.OrdinalIgnoreCase);
		public List<Script> Scripts { get; set; } = [];
		public List<Bucket> Buckets { get; set; } = [];
		#endregion
	}
}
