/*
 *   _____                                ______
 *  /_   /  ____  ____  ____  _________  / __/ /_
 *    / /  / __ \/ __ \/ __ \/ ___/ __ \/ /_/ __/
 *   / /__/ /_/ / / / / /_/ /\_ \/ /_/ / __/ /_
 *  /____/\____/_/ /_/\__  /____/\____/_/  \__/
 *                   /____/
 *
 * Authors:
 *   钟峰(Popeye Zhong) <zongsoft@gmail.com>
 *
 * The MIT License (MIT)
 *
 * Copyright (C) 2020-2026 Zongsoft Corporation <http://www.zongsoft.com>
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

using System;
using System.IO;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Zongsoft.Tools.Packager.Migration;

public sealed partial class MigrationPlan
{
	public sealed class Bucket
	{
		#region 公共属性
		public string Name { get; set; }
		public bool Public { get; set; }

		[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
		public EncryptionOptions Encryption { get; set; }

		[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
		public string Versioning { get; set; }

		[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
		public Dictionary<string, string> Tags { get; set; }
		#endregion

		#region 公共方法
		public void Validate()
		{
			if(string.IsNullOrWhiteSpace(this.Name) ||
				this.Versioning is not (null or "enabled" or "suspended"))
				throw new InvalidDataException(MigrationResources.PlanInvalid);

			if(this.Encryption != null &&
				(this.Encryption.Mode is not ("sse-s3" or "sse-kms") ||
				this.Encryption.Key != null && (this.Encryption.Mode != "sse-kms" || string.IsNullOrWhiteSpace(this.Encryption.Key))))
				throw new InvalidDataException(MigrationResources.PlanInvalid);

			if(this.Tags == null)
				return;

			if(this.Tags.Count > 50)
				throw new InvalidDataException(MigrationResources.PlanInvalid);

			foreach(var tag in this.Tags)
			{
				if(string.IsNullOrEmpty(tag.Key) || tag.Key.Length > 128 || tag.Key.StartsWith("aws:", StringComparison.OrdinalIgnoreCase) ||
					tag.Value == null || tag.Value.Length > 256)
					throw new InvalidDataException(MigrationResources.PlanInvalid);
			}
		}
		#endregion

		#region 嵌套类型
		public sealed class EncryptionOptions
		{
			#region 公共属性
			public string Mode { get; set; }

			[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
			public string Key { get; set; }
			#endregion
		}
		#endregion
	}
}
