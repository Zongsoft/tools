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

partial class Installation
{
	#region 嵌套类型
	internal sealed class Transaction
	{
		#region 公共属性
		public string Id { get; set; }
		public string Version { get; set; }
		public string Assets { get; set; }
		public string Phase { get; set; } = "StageInstallation";
		public bool Failed { get; set; }
		public bool IsReinstall { get; set; }
		public List<string> Completed { get; set; } = [];
		public List<OperationEvent> Events { get; set; } = [];
		#endregion
	}

	internal sealed class OperationEvent
	{
		#region 公共属性
		public DateTimeOffset Time { get; set; } = DateTimeOffset.UtcNow;
		public string Phase { get; set; }
		public string Result { get; set; }
		public int ExitCode { get; set; }
		#endregion
	}

	internal sealed class OwnedDirectory
	{
		#region 公共属性
		public string Path { get; set; }
		public string Token { get; set; }
		public bool Created { get; set; }
		#endregion
	}

	internal sealed class MigrationResult
	{
		#region 公共属性
		public string Version { get; set; }
		public string Identity { get; set; }
		public string Status { get; set; }
		public List<MigrationAttempt> Attempts { get; set; } = [];
		#endregion
	}

	internal sealed class MigrationAttempt
	{
		#region 公共属性
		public DateTimeOffset Started { get; set; } = DateTimeOffset.UtcNow;
		public DateTimeOffset? Finished { get; set; }
		public int? ExitCode { get; set; }
		public string Log { get; set; }
		#endregion
	}
	#endregion
}
