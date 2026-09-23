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

namespace Zongsoft.Tools.Migrator.Migration;

partial class MigrationPlan
{
	#region 公共方法
	public void Validate()
	{
		if(string.IsNullOrWhiteSpace(this.Name) || this.Steps == null || this.Steps.Count == 0 || this.Databases == null)
			throw new InvalidDataException(MigrationResources.PlanInvalid_Message);

		MigrationRuntime.Validate(this.Runtime);

		var targets = new HashSet<string>(StringComparer.Ordinal);
		var accounts = new Dictionary<string, string>(StringComparer.Ordinal);

		foreach(var database in this.Databases)
		{
			if(database == null)
				throw new InvalidDataException(MigrationResources.PlanInvalid_Message);

			MigrationProvider.Get(database.Provider).Prepare(database, this.Runtime);

			if(!targets.Add(database.TargetKey))
				throw new InvalidDataException(MigrationResources.PlanInvalid_Message);

			foreach(var user in database.Users)
			{
				var key = database.ServerKey + "\0" + user.Name + "\0" + user.Host?.ToLowerInvariant();
				if(accounts.TryGetValue(key, out var password) && password != user.Password)
					throw new InvalidDataException(string.Format(MigrationResources.ParameterValueInvalid_Message, "Users.Password"));

				accounts[key] = user.Password;
			}
		}

		var referenced = new HashSet<int>();

		foreach(var step in this.Steps)
		{
			if(step == null || step.Settings == null || step.Scripts == null || step.Buckets == null)
				throw new InvalidDataException(MigrationResources.PlanInvalid_Message);

			var provider = MigrationProvider.Get(step.Provider);
			if(step.Provider != provider.Name || (provider.Name == "amazon.s3" ? step.Scripts.Count > 0 : step.Buckets.Count > 0))
				throw new InvalidDataException(MigrationResources.PlanInvalid_Message);

			step.Settings = new(step.Settings, StringComparer.OrdinalIgnoreCase);
			if(provider.Name == "amazon.s3")
			{
				if(step.DatabaseIndex != null)
					throw new InvalidDataException(MigrationResources.PlanInvalid_Message);
				provider.Validate(step.Settings, this.Runtime);
			}
			else
			{
				if(step.DatabaseIndex is not int index || index < 0 || index >= this.Databases.Count || this.Databases[index].Provider != provider.Name || step.Settings.Count > 0)
					throw new InvalidDataException(MigrationResources.PlanInvalid_Message);
				referenced.Add(index);
			}

			foreach(var script in step.Scripts)
				if(script == null || string.IsNullOrWhiteSpace(script.Path) || string.IsNullOrWhiteSpace(script.Checksum))
					throw new InvalidDataException(MigrationResources.PlanInvalid_Message);

			foreach(var bucket in step.Buckets)
			{
				if(bucket == null)
					throw new InvalidDataException(MigrationResources.PlanInvalid_Message);

				bucket.Validate();
			}
		}

		if(referenced.Count != this.Databases.Count)
			throw new InvalidDataException(MigrationResources.PlanInvalid_Message);
	}
	#endregion
}
