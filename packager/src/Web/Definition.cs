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
using System.Collections.Generic;

using Zongsoft.Text.Templating;

namespace Zongsoft.Tools.Packager.Web;

internal sealed partial class Definition
{
	#region 成员字段
	private readonly Scope _root;
	private readonly IReadOnlyList<Scope> _sites;
	#endregion

	#region 构造函数
	private Definition(string filePath, Scope root, IReadOnlyList<Scope> sites)
	{
		this.FilePath = filePath;
		_root = root;
		_sites = sites;
	}
	#endregion

	#region 公共属性
	public string FilePath { get; }
	#endregion

	#region 公共方法
	/// <summary>加载 Web 定义，并在指令执行前求值指令参数。</summary>
	/// <param name="filePath">Web 定义文件路径。</param>
	/// <param name="evaluator">指令参数的外部模板变量来源；为空时仅使用当前 Profile 已读取的条目。</param>
	/// <returns>保留声明来源及覆盖顺序的 Web 定义。</returns>
	public static Definition Load(string filePath, TemplateEvaluator evaluator = null) => new Loader().Load(filePath, evaluator);
	#endregion

	#region 内部方法
	internal Model Resolve(Configurator.Context context, Rules rules) => new Resolver(context, rules).Resolve(_root, _sites);
	#endregion

	#region 嵌套类型
	private sealed class Scope(string name, int depth, Diagnostic.Location source)
	{
		internal string Name { get; } = name;
		internal int Depth { get; } = depth;
		internal Diagnostic.Location Source { get; } = source;
		internal List<Declaration> Declarations { get; } = [];
		internal List<Scope> Children { get; } = [];
	}

	private sealed record Declaration(string Name, string Value, Diagnostic.Location Source, object Owner)
	{
		internal bool IsServer => this.Name.Equals("server", StringComparison.OrdinalIgnoreCase) || this.Name.StartsWith("server!", StringComparison.OrdinalIgnoreCase);
	}
	#endregion
}
