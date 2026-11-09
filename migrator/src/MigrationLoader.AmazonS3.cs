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
using System.Text;
using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace Zongsoft.Tools.Migrator.Migration;

partial class MigrationLoader
{
	private sealed partial class AmazonS3
	{
		#region 成员字段
		private readonly MigrationPlan.Step _step;
		private readonly HashSet<string> _names;
		#endregion

		#region 构造函数
		public AmazonS3(MigrationPlan.Step step, HashSet<string> names)
		{
			_step = step;
			_names = names;
		}
		#endregion

		#region 公共方法
		public void Add(string name, string options)
		{
			var bucket = ParseBucket(name, options);
			if(!_names.Add(bucket.Name))
				throw new InvalidDataException(Properties.Resources.MigrationBucketDuplicate_Message);

			_step.Buckets.Add(bucket);
		}
		#endregion

		#region 私有方法
		private static MigrationPlan.Bucket ParseBucket(string name, string options)
		{
			if(!Regex.IsMatch(name, @"^[a-z0-9][a-z0-9.-]{1,61}[a-z0-9]$") || name.Contains("..") || System.Net.IPAddress.TryParse(name, out _))
				throw new InvalidDataException(string.Format(Properties.Resources.MigrationBucketNameInvalid_Message, name));

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
						throw new InvalidDataException(string.Format(Properties.Resources.MigrationBucketOptionsInvalid_Message, name));

					continue;
				}

				var option = key.ToLowerInvariant();
				if(!keys.Add(option is "public" or "private" ? "access" : option))
					throw new InvalidDataException(string.Format(Properties.Resources.MigrationBucketOptionsInvalid_Message, name));

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
						throw new InvalidDataException(string.Format(Properties.Resources.MigrationBucketOptionsInvalid_Message, name));
				}
			}

			try { bucket.Validate(); }
			catch(InvalidDataException) { throw new InvalidDataException(string.Format(Properties.Resources.MigrationBucketOptionsInvalid_Message, name)); }

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
				throw new InvalidDataException(string.Format(Properties.Resources.MigrationBucketOptionsInvalid_Message, name));

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
