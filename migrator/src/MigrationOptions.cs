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
using System.Runtime.InteropServices;

using Zongsoft.Text.Templating;

namespace Zongsoft.Tools.Migrator;

/// <summary>提供升迁包制作选项的类型化访问、模板求值及工具默认值。</summary>
/// <remarks>选项值由评估器的变量来源提供；此类型不实现变量字典或变量提供程序。</remarks>
/// <param name="evaluator">本次命令的模板评估器；为空时创建带有内存变量来源的递归评估器。</param>
public sealed class MigrationOptions(TemplateEvaluator evaluator = null)
{
	#region 常量定义
	internal const string NAME = "name";
	internal const string TITLE = "title";
	internal const string SUMMARY = "summary";
	internal const string DESCRIPTION = "description";
	internal const string SOURCE = "source";
	internal const string OUTPUT = "output";
	internal const string EDITION = "edition";
	internal const string VERSION = "version";
	internal const string PLATFORM = "platform";
	internal const string ARCHITECTURE = "architecture";
	#endregion

	#region 公共属性
	/// <summary>获取负责本次命令原始变量来源及模板评估的 Core 评估器。</summary>
	public TemplateEvaluator Evaluator { get; } = evaluator ?? Utility.CreateEvaluator();
	/// <summary>获取求值后的选项或设置原始选项；未找到的选项返回空值。</summary>
	/// <param name="name">选项名称，其中的点号和连字符映射为下划线。</param>
	public string this[string name]
	{
		get => this.Evaluator.GetOption(name);
		set => this.Evaluator.SetVariable(name, value);
	}

	public string Name => this[NAME];
	public string Title => this[TITLE];
	public string Summary => TextSource.Read(this.Source, this.GetRaw(SUMMARY), this);
	public string Description => TextSource.Read(this.Source, this.GetRaw(DESCRIPTION), this);
	public string Source => this[SOURCE];
	public string Output => this[OUTPUT];
	public string Edition => this[EDITION];
	/// <summary>获取解析后的应用版本；没有版本选项时为空。</summary>
	public Version Version => this.Evaluator.TryGetVariable(VERSION, out _) ? Version.Parse(this[VERSION]) : null;
	/// <summary>获取目标架构；没有架构选项时为 X64。</summary>
	public Architecture Architecture => !this.Contains(ARCHITECTURE) ? Architecture.X64 : Zongsoft.Common.Convert.ConvertValue<Architecture>(this[ARCHITECTURE]);
	#endregion

	#region 公共方法
	/// <summary>判断是否存在指定选项，不对其值进行模板求值。</summary>
	/// <param name="name">选项名称；为空时返回假。</param>
	/// <returns>是否存在该选项，值为空也视为存在。</returns>
	public bool Contains(string name) => name != null && this.Evaluator.TryGetVariable(Utility.NormalizeVariableName(name), out _);
	#endregion

	#region 私有方法
	private string GetRaw(string name) => this.Evaluator.GetVariable(Utility.NormalizeVariableName(name))?.ToString();
	#endregion

}
