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
using System.Linq;
using System.Collections.Generic;

#if DEPLOYER
namespace Zongsoft.Tools.Deployer;
#elif PACKAGER
namespace Zongsoft.Tools.Packager;
#elif MIGRATOR
namespace Zongsoft.Tools.Migrator;
#elif CONTAINERIZER
namespace Zongsoft.Tools.Containerizer;
#else
namespace Zongsoft.Tools;
#endif

/// <summary>解析按从左到右组合的存在、匹配及否定条件，由调用方提供取值语义。</summary>
internal sealed class FilterExpression
{
	#region 成员字段
	private readonly Clause[] _clauses;
	#endregion

	#region 构造函数
	private FilterExpression(Clause[] clauses) => _clauses = clauses;
	#endregion

	#region 公共方法
	/// <summary>解析过滤条件；可在解析时检查调用方的字段引用语法。</summary>
	public static FilterExpression Parse(ReadOnlySpan<char> text, Action<string> validate = null)
	{
		if(text.IsEmpty)
			return new([]);

		var clauses = new List<Clause>();
		var start = 0;
		var combiner = '|';

		for(var index = 0; index <= text.Length; index++)
		{
			if(index < text.Length && text[index] is not ('|' or '&'))
				continue;

			var part = text[start..index].Trim();
			if(part.IsEmpty || part.SequenceEqual("!"))
				throw new FormatException();

			var separator = part.IndexOf(':');
			var negate = part[0] == '!';
			var name = (separator < 0 ? part[(negate ? 1 : 0)..] : part[(negate ? 1 : 0)..separator]).Trim().ToString();
			var value = separator < 0 ? ReadOnlySpan<char>.Empty : part[(separator + 1)..].Trim();

			if(separator != 0)
				validate?.Invoke(name);

			clauses.Add(new(name, value.IsEmpty ? null : value.ToString().Split(',', StringSplitOptions.TrimEntries), negate, combiner, separator == 0));

			if(index < text.Length)
			{
				combiner = text[index];
				start = index + 1;
			}
		}

		return new(clauses.ToArray());
	}

	/// <summary>求值全部条件；空候选数组引用表示存在判断，不根据值的真假判断。</summary>
	public bool Evaluate(Func<string, string[], bool> match)
	{
		ArgumentNullException.ThrowIfNull(match);
		bool? result = null;

		foreach(var clause in _clauses)
		{
			var value = !clause.EmptyName && match(clause.Name, clause.Values);
			if(clause.Negate)
				value = !value;

			result = result == null ? value : clause.Combiner == '|' ? result.Value || value : result.Value && value;
		}

		return result ?? true;
	}

	/// <summary>按既有过滤语义匹配字符串候选值。</summary>
	public static bool Matches(string value, string[] candidates) => candidates.Contains(value?.Trim(), StringComparer.OrdinalIgnoreCase);
	#endregion

	#region 嵌套类型
	private readonly record struct Clause(string Name, string[] Values, bool Negate, char Combiner, bool EmptyName);
	#endregion
}
