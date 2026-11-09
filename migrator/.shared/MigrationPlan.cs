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

namespace Zongsoft.Tools.Migrator.Migration;

public sealed partial class MigrationPlan
{
	#region 公共属性
	/// <summary>获取或设置计划名称。</summary>
	public string Name { get; set; }
	/// <summary>获取或设置升迁版本号。</summary>
	public string Version { get; set; }
	/// <summary>获取或设置目标 RID，执行前须与运行环境一致。</summary>
	public string Runtime { get; set; }
	/// <summary>获取或设置展示标题。</summary>
	public string Title { get; set; }
	/// <summary>获取或设置展示摘要。</summary>
	public string Summary { get; set; }
	/// <summary>获取或设置展示说明。</summary>
	public string Description { get; set; }

	public List<Step> Steps { get; set; } = [];
	public List<Database> Databases { get; set; } = [];
	#endregion
}
