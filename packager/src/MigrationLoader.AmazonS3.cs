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
using System.Text;
using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace Zongsoft.Tools.Packager.Migration;

partial class MigrationLoader
{
	private sealed partial class AmazonS3
	{
		#region 成员字段
		private readonly MigrationPlan.Step _task;
		private readonly HashSet<string> _names = new(StringComparer.Ordinal);
		#endregion

		#region 构造函数
		public AmazonS3(MigrationPlan.Step task) => _task = task;
		#endregion

		#region 公共方法
		public void Add(string name, string options)
		{
			var bucket = ParseBucket(name, options);
			if(!_names.Add(bucket.Name))
				throw new InvalidDataException(Properties.Resources.MigrationBucketDuplicate);

			_task.Buckets.Add(bucket);
		}
		#endregion

		#region 私有方法
		private static MigrationPlan.Bucket ParseBucket(string name, string options)
		{
			if(!Regex.IsMatch(name, @"^[a-z0-9][a-z0-9.-]{1,61}[a-z0-9]$") || name.Contains("..") || System.Net.IPAddress.TryParse(name, out _))
				throw new InvalidDataException(string.Format(Properties.Resources.MigrationBucketNameInvalid, name));

			var bucket = new MigrationPlan.Bucket { Name = name };
			var keys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

			foreach(var token in GetOptions(name, options))
			{
				var key = token.Key;
				var value = token.Value;

				if(key.StartsWith("tag.", StringComparison.OrdinalIgnoreCase))
				{
					bucket.Tags ??= new(StringComparer.Ordinal);
					if(value == null || !bucket.Tags.TryAdd(key[4..], value))
						throw new InvalidDataException(string.Format(Properties.Resources.MigrationBucketOptionsInvalid, name));

					continue;
				}

				var option = key.ToLowerInvariant();
				if(!keys.Add(option is "public" or "private" ? "access" : option))
					throw new InvalidDataException(string.Format(Properties.Resources.MigrationBucketOptionsInvalid, name));

				switch(option)
				{
					case "public" when value == null:
					case "private" when value == null:
						bucket.Public = option == "public";
						break;
					case "encryption" when !string.IsNullOrWhiteSpace(value):
						(bucket.Encryption ??= new()).Mode = value.ToLowerInvariant();
						break;
					case "encryption.key" when !string.IsNullOrWhiteSpace(value):
						(bucket.Encryption ??= new()).Key = value;
						break;
					case "versioning" when !string.IsNullOrWhiteSpace(value):
						bucket.Versioning = value.ToLowerInvariant();
						break;
					default:
						throw new InvalidDataException(string.Format(Properties.Resources.MigrationBucketOptionsInvalid, name));
				}
			}

			try { bucket.Validate(); }
			catch(InvalidDataException) { throw new InvalidDataException(string.Format(Properties.Resources.MigrationBucketOptionsInvalid, name)); }

			return bucket;
		}

		private static IEnumerable<KeyValuePair<string, string>> GetOptions(string name, string options)
		{
			var text = new StringBuilder();
			var quote = '\0';
			var separator = -1;

			for(var i = 0; i < (options?.Length ?? 0); i++)
			{
				var character = options[i];
				if(quote != '\0')
				{
					if(character != quote)
						text.Append(character);
					else if(i + 1 < options.Length && options[i + 1] == quote)
					{
						text.Append(quote);
						i++;
					}
					else
						quote = '\0';
				}
				else if(character is '\'' or '"')
					quote = character;
				else if(character is ',' or '|')
				{
					if(text.Length > 0 && !string.IsNullOrWhiteSpace(text.ToString()))
						yield return GetOption(text, separator);

					text.Clear();
					separator = -1;
				}
				else
				{
					if(character == ':' && separator < 0)
						separator = text.Length;

					text.Append(character);
				}
			}

			if(quote != '\0')
				throw new InvalidDataException(string.Format(Properties.Resources.MigrationBucketOptionsInvalid, name));

			if(text.Length > 0 && !string.IsNullOrWhiteSpace(text.ToString()))
				yield return GetOption(text, separator);
		}

		private static KeyValuePair<string, string> GetOption(StringBuilder text, int separator)
		{
			var token = text.ToString();
			return new(
				(separator < 0 ? token : token[..separator]).Trim(),
				separator < 0 ? null : token[(separator + 1)..].Trim());
		}
		#endregion
	}
}
