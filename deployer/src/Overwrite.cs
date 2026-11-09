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

namespace Zongsoft.Tools.Deployer;

/// <summary>表示覆盖模式的枚举。</summary>
public enum Overwrite
{
	/// <summary>始终覆盖</summary>
	Alway = 0,
	/// <summary>不覆盖</summary>
	Never = 1,
	/// <summary>如果较新，则覆盖</summary>
	Newest = 2,
}
