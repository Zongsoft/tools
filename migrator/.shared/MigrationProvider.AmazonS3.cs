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

partial class MigrationProvider
{
	private sealed class AmazonS3() : MigrationProvider("amazon.s3")
	{
		#region 公共方法
		public override void Validate(IReadOnlyDictionary<string, string> parameters, string runtime = "linux-x64")
		{
			base.Validate(parameters, ["Server", "Region", "AccessKey", "SecretKey", "Timeout"]);
			this.Require(parameters, "Server");
			this.Require(parameters, "Region");
			this.Require(parameters, "AccessKey");
			this.Require(parameters, "SecretKey");

			if(!Uri.TryCreate(parameters.Get("Server"), UriKind.Absolute, out var uri) || uri.Scheme is not ("https" or "http") || !string.IsNullOrEmpty(uri.UserInfo))
				throw new InvalidDataException(MigrationResources.S3ServerInvalid_Message);
		}
		#endregion
	}
}
