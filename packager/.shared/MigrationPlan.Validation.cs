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

partial class MigrationPlan
{
	#region 公共方法
	public void Validate()
	{
		if(FormatVersion != 1 || string.IsNullOrWhiteSpace(Package) || Tasks == null || Tasks.Count == 0)
			throw new InvalidDataException(MigrationResources.PlanInvalid);

		var identifiers = new HashSet<string>(StringComparer.Ordinal);
		foreach(var task in Tasks)
		{
			if(task == null || string.IsNullOrWhiteSpace(task.Id) || !identifiers.Add(task.Id) || task.Parameters == null || task.Scripts == null || task.Buckets == null)
				throw new InvalidDataException(MigrationResources.PlanInvalid);

			var provider = MigrationProvider.Get(task.Provider);
			if(task.Provider != provider.Name || (provider.Name == "amazon.s3" ? task.Scripts.Count > 0 : task.Buckets.Count > 0))
				throw new InvalidDataException(MigrationResources.PlanInvalid);

			task.Parameters = new(task.Parameters, StringComparer.OrdinalIgnoreCase);
			provider.Validate(task.Parameters);

			foreach(var script in task.Scripts)
				if(script == null || string.IsNullOrWhiteSpace(script.Path) || string.IsNullOrWhiteSpace(script.Checksum))
					throw new InvalidDataException(MigrationResources.PlanInvalid);

			foreach(var bucket in task.Buckets)
			{
				if(bucket == null)
					throw new InvalidDataException(MigrationResources.PlanInvalid);

				bucket.Validate();
			}
		}
	}
	#endregion
}
