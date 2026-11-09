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
using System.IO;
using System.Collections.Generic;

namespace Zongsoft.Tools.Migrator.Migration;

public sealed partial class MigrationPlan
{
	public sealed class Bucket
	{
		#region 公共属性
		public string Name { get; set; }
		public bool Public { get; set; }
		public EncryptionOptions Encryption { get; set; }
		public string Versioning { get; set; }
		public Dictionary<string, string> Tags { get; set; }
		#endregion

		#region 公共方法
		public void Validate()
		{
			if(string.IsNullOrWhiteSpace(this.Name) ||
				this.Versioning is not (null or "enabled" or "suspended"))
				throw new InvalidDataException(MigrationResources.PlanInvalid_Message);

			if(this.Encryption != null &&
				(this.Encryption.Mode is not ("sse-s3" or "sse-kms") ||
				this.Encryption.Key != null && (this.Encryption.Mode != "sse-kms" || string.IsNullOrWhiteSpace(this.Encryption.Key))))
				throw new InvalidDataException(MigrationResources.PlanInvalid_Message);

			if(this.Tags == null)
				return;

			if(this.Tags.Count > 50)
				throw new InvalidDataException(MigrationResources.PlanInvalid_Message);

			foreach(var tag in this.Tags)
			{
				if(string.IsNullOrEmpty(tag.Key) || tag.Key.Length > 128 || tag.Key.StartsWith("aws:", StringComparison.OrdinalIgnoreCase) ||
					tag.Value == null || tag.Value.Length > 256)
					throw new InvalidDataException(MigrationResources.PlanInvalid_Message);
			}
		}
		#endregion

		#region 嵌套类型
		public sealed class EncryptionOptions
		{
			#region 公共属性
			public string Mode { get; set; }
			public string Key { get; set; }
			#endregion
		}
		#endregion
	}
}
