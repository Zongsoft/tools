/*
 *   _____                                ______
 *  /_   /  ____  ____  ____  _________  / __/ /_
 *    / /  / __ \/ __ \/ __ \/ ___/ __ \/ /_/ __/
 *   / /__/ /_/ / / / /_/ /\_ \/ /_/ / __/ /_
 *  /____/\____/_/ /_/\__  /____/\____/_/  \__/
 *                   /____/
 *
 * Authors:
 *   钟峰(Popeye Zhong) <zongsoft@gmail.com>
 *
 * The MIT License (MIT)
 *
 * Copyright (C) 2026 Zongsoft Corporation <http://www.zongsoft.com>
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
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace Zongsoft.Tools.Containerizer.Protocol;

internal sealed partial class RegistryMirrors
{
	#region 常量定义
	internal const string FILE_NAME = ".mirrors";
	internal const string ENVIRONMENT = "CONTAINERIZER_MIRRORS";
	#endregion

	#region 公共属性
	[JsonIgnore]
	public bool IsEmpty => this.Registries.Count == 0;
	public Dictionary<string, string[]> Registries { get; set; } = new(StringComparer.OrdinalIgnoreCase);
	#endregion

	#region 公共方法
	public IEnumerable<string> Repositories(string repository)
	{
		var slash = repository.IndexOf('/');

		if(slash > 0 && this.Registries.TryGetValue(repository[..slash], out var locations))
		{
			foreach(var location in locations.Distinct(StringComparer.Ordinal))
			{
				var value = $"{location}{repository[slash..]}";

				if(value != repository)
					yield return value;
			}
		}

		yield return repository;
	}

	public async Task<T> ExecuteAsync<T>(string repository, Func<string, Task<T>> operation, CancellationToken cancellation, Action<string, Exception> warning = null)
	{
		var candidates = this.Repositories(repository).ToArray();
		if(candidates.Length == 1)
			return await operation(repository);

		var failures = new List<string>();

		foreach(var candidate in candidates)
		{
			cancellation.ThrowIfCancellationRequested();

			try { return await operation(candidate); }
			catch(Exception exception) when(exception is ContainerizationException or JsonException || exception is OperationCanceledException && !cancellation.IsCancellationRequested)
			{
				failures.Add($"{candidate}: {exception.Message}");
				warning?.Invoke(candidate, exception);
			}
		}

		throw new ContainerizationException(4, string.Format(Properties.Resources.Mirrors_Failed_Message, repository, string.Join(Environment.NewLine, failures)));
	}

	public void Validate()
	{
		if(this.Registries == null)
			throw new ContainerizationException(2, Properties.Resources.Mirrors_Invalid_Message);

		foreach(var pair in this.Registries)
		{
			if(!IsRegistry(pair.Key) || pair.Value == null || pair.Value.Length == 0 || pair.Value.Any(value => !IsLocation(value)))
				throw new ContainerizationException(2, Properties.Resources.Mirrors_Invalid_Message);
		}
	}

	public static bool IsRegistry(string value) => IsLocation(value) && !value.Contains('/');
	public static bool IsLocation(string value)
	{
		if(value == null || !LocationRegex().IsMatch(value))
			return false;

		var host = value.Split('/')[0];
		var parts = host.Split(':');

		return (parts[0] == "localhost" || parts[0].Contains('.') || parts.Length == 2) &&
			(parts.Length == 1 || int.TryParse(parts[1], out var port) && port is > 0 and <= 65535);
	}
	#endregion

	#region 私有方法
	[GeneratedRegex(@"^[a-z0-9][a-z0-9.-]*(?::[0-9]+)?(?:/[a-z0-9]+(?:[._-]+[a-z0-9]+)*)*$")]
	private static partial Regex LocationRegex();
	#endregion
}
