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

internal sealed class ImagePlan
{
	#region 公共属性
	public string Repository { get; set; }
	public string SourceReference { get; set; }
	public string SourceTag { get; set; }
	public string IndexDigest { get; set; }
	public string Digest { get; set; }
	public string Id { get; set; }
	public string Platform { get; set; }
	public string Reference { get; set; }
	public string Mode { get; set; } = "offline";
	public string Archive { get; set; }

	[JsonIgnore]
	public long? Size { get; set; }
	[JsonIgnore]
	public string Timestamp { get; set; }
	[JsonIgnore]
	public string SourceRepository { get; set; }
	#endregion
}
