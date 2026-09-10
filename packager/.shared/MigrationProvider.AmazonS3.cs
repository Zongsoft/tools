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

namespace Zongsoft.Tools.Packager.Migration;

partial class MigrationProvider
{
	private sealed class AmazonS3() : MigrationProvider("amazon.s3")
	{
		#region 公共方法
		public override void Validate(IReadOnlyDictionary<string, string> parameters)
		{
			base.Validate(parameters, ["Server", "Region", "AccessKey", "SecretKey", "Timeout"]);
			Require(parameters, "Server");
			Require(parameters, "Region");
			Require(parameters, "AccessKey");
			Require(parameters, "SecretKey");

			if(!Uri.TryCreate(parameters.Get("Server"), UriKind.Absolute, out var uri) || uri.Scheme is not ("https" or "http") || !string.IsNullOrEmpty(uri.UserInfo))
				throw new InvalidDataException(MigrationResources.S3ServerInvalid);
		}
		#endregion
	}
}
