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
using System.Runtime.InteropServices;

using Zongsoft.Text.Templating;

namespace Zongsoft.Tools.Packager;

/// <summary>提供安装包制作选项的类型化访问、模板求值及工具默认值。</summary>
/// <remarks>选项值由评估器的变量来源提供；此类型不实现变量字典或变量提供程序。</remarks>
/// <param name="evaluator">本次命令的模板评估器；为空时创建带有内存变量来源的递归评估器。</param>
public sealed class PackageOptions(TemplateEvaluator evaluator = null)
{
	#region 常量定义
	internal const string NAME = "name";
	internal const string TITLE = "title";
	internal const string LISTEN = "listen";
	internal const string LICENSE = "license";
	internal const string CATEGORY = "category";
	internal const string HOMEPAGE = "homepage";
	internal const string MAINTAINER = "maintainer";
	internal const string MANUFACTURER = "manufacturer";
	internal const string DEFAULT_MANUFACTURER = "Zongsoft";
	internal const string DEPENDENCIES = "dependencies";
	internal const string SUMMARY = "summary";
	internal const string DESCRIPTION = "description";
	internal const string SOURCE = "source";
	internal const string OUTPUT = "output";
	internal const string EXCLUDE = "exclude";
	internal const string EDITION = "edition";
	internal const string VERSION = "version";
	internal const string PLATFORM = "platform";
	internal const string FRAMEWORK = "framework";
	internal const string COMPILATION = "compilation";
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

	public string Homepage => this[HOMEPAGE];
	public string Name => this[NAME];
	public string Title => this[TITLE];
	public string Listen => this[LISTEN];
	public string License => this[LICENSE];
	public string Category => this[CATEGORY];
	public string Maintainer => this[MAINTAINER];
	public string Manufacturer
	{
		get
		{
			var value = this.GetRaw(MANUFACTURER);
			if(!string.IsNullOrWhiteSpace(value))
				value = this[MANUFACTURER];

			return string.IsNullOrEmpty(value) ? DEFAULT_MANUFACTURER : value;
		}
	}

	public string Summary => TextSource.Read(this.Source, this.GetRaw(SUMMARY), this);
	public string Description => TextSource.Read(this.Source, this.GetRaw(DESCRIPTION), this);
	public string Source => this[SOURCE];
	public string Output => this[OUTPUT];
	public string Exclude => this[EXCLUDE];
	public string Edition => this[EDITION];
	/// <summary>获取解析后的应用版本；没有版本选项时为空。</summary>
	public Version Version => this.Evaluator.TryGetVariable(VERSION, out _) ? Version.Parse(this[VERSION]) : null;
	/// <summary>获取目标平台；没有平台选项时为未知平台。</summary>
	public Platform Platform => !this.Contains(PLATFORM) ? Platform.Unknown : Zongsoft.Common.Convert.ConvertValue<Platform>(this[PLATFORM]);
	/// <summary>获取目标架构；没有架构选项时为 X64。</summary>
	public Architecture Architecture => !this.Contains(ARCHITECTURE) ? Architecture.X64 : Zongsoft.Common.Convert.ConvertValue<Architecture>(this[ARCHITECTURE]);
	public string Framework => this[FRAMEWORK];
	/// <summary>获取编译配置；没有相应选项时为 Release。</summary>
	public string Compilation => this.Evaluator.TryGetVariable(COMPILATION, out _) ? this[COMPILATION] : "Release";
	/// <summary>获取显式运行时标识，未指定时根据平台和架构生成。</summary>
	public string RuntimeIdentifier => this.Evaluator.TryGetVariable(nameof(this.RuntimeIdentifier), out _) ? this[nameof(this.RuntimeIdentifier)] : Utility.GetRuntimeIdentifier(this.Platform, this.Architecture);
	public string[] Dependencies => Dependency.Split(this[DEPENDENCIES]);

	public DaemonOptions Daemon => new
	(
		this[DaemonOptions.DAEMON],
		this[DaemonOptions.DAEMON_ENVIRONMENTS]
	);

	public ScriptOptions Script => new
	(
		this.GetRaw(ScriptOptions.INSTALLING),
		this.GetRaw(ScriptOptions.INSTALLED),
		this.GetRaw(ScriptOptions.UNINSTALLING),
		this.GetRaw(ScriptOptions.UNINSTALLED),
		this.GetRaw(ScriptOptions.PREINSTALLING),
		this.GetRaw(ScriptOptions.POSTINSTALLING),
		this.GetRaw(ScriptOptions.PREINSTALLED),
		this.GetRaw(ScriptOptions.POSTINSTALLED),
		this.GetRaw(ScriptOptions.PREUNINSTALLING),
		this.GetRaw(ScriptOptions.POSTUNINSTALLING),
		this.GetRaw(ScriptOptions.PREUNINSTALLED),
		this.GetRaw(ScriptOptions.POSTUNINSTALLED)
	);
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

	#region 嵌套类型
	/// <summary>描述应用服务的标识及需要传入服务进程的环境变量名称。</summary>
	public readonly struct DaemonOptions
	{
		internal const string DAEMON = "daemon";
		internal const string DAEMON_ENVIRONMENTS = "daemon-environments";

		/// <summary>创建应用服务选项。</summary>
		/// <param name="identifier">服务标识；none、disable 或 disabled 表示禁用应用服务。</param>
		/// <param name="environments">以逗号或分号分隔的环境变量名称；空值表示不附加环境变量。</param>
		public DaemonOptions(string identifier, string environments)
		{
			this.Identifier = identifier;
			this.Environments = string.IsNullOrEmpty(environments) ? [] : environments.Split([',', ';'], StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
		}

		/// <summary>获取应用服务标识。</summary>
		public readonly string Identifier;
		/// <summary>获取需要传入服务进程的环境变量名称；各名称对应的值由工具选项来源提供。</summary>
		public readonly string[] Environments;

		/// <summary>获取是否禁用应用服务。</summary>
		public bool Disabled =>
			string.Equals(this.Identifier, "none", StringComparison.OrdinalIgnoreCase) ||
			string.Equals(this.Identifier, "disable", StringComparison.OrdinalIgnoreCase) ||
			string.Equals(this.Identifier, "disabled", StringComparison.OrdinalIgnoreCase);
	}

	/// <summary>保存安装与卸载生命周期脚本的原始文本来源，由脚本生成流程解释文件或内联文本。</summary>
	public readonly struct ScriptOptions
	{
		internal const string INSTALLING = "installing";
		internal const string INSTALLED = "installed";
		internal const string UNINSTALLING = "uninstalling";
		internal const string UNINSTALLED = "uninstalled";

		internal const string PREINSTALLING = "preinstalling";
		internal const string POSTINSTALLING = "postinstalling";
		internal const string PREINSTALLED = "preinstalled";
		internal const string POSTINSTALLED = "postinstalled";
		internal const string PREUNINSTALLING = "preuninstalling";
		internal const string POSTUNINSTALLING = "postuninstalling";
		internal const string PREUNINSTALLED = "preuninstalled";
		internal const string POSTUNINSTALLED = "postuninstalled";

		/// <summary>创建安装与卸载生命周期脚本选项。</summary>
		/// <param name="installing">安装阶段的原始脚本文本或文件来源；空值表示不附加脚本。</param>
		/// <param name="installed">安装完成阶段的原始脚本文本或文件来源；空值表示不附加脚本。</param>
		/// <param name="uninstalling">卸载阶段的原始脚本文本或文件来源；空值表示不附加脚本。</param>
		/// <param name="uninstalled">卸载完成阶段的原始脚本文本或文件来源；空值表示不附加脚本。</param>
		/// <param name="preinstalling">安装阶段之前的原始脚本文本或文件来源；空值表示不附加脚本。</param>
		/// <param name="postinstalling">安装阶段之后的原始脚本文本或文件来源；空值表示不附加脚本。</param>
		/// <param name="preinstalled">安装完成阶段之前的原始脚本文本或文件来源；空值表示不附加脚本。</param>
		/// <param name="postinstalled">安装完成阶段之后的原始脚本文本或文件来源；空值表示不附加脚本。</param>
		/// <param name="preuninstalling">卸载阶段之前的原始脚本文本或文件来源；空值表示不附加脚本。</param>
		/// <param name="postuninstalling">卸载阶段之后的原始脚本文本或文件来源；空值表示不附加脚本。</param>
		/// <param name="preuninstalled">卸载完成阶段之前的原始脚本文本或文件来源；空值表示不附加脚本。</param>
		/// <param name="postuninstalled">卸载完成阶段之后的原始脚本文本或文件来源；空值表示不附加脚本。</param>
		public ScriptOptions(
			string installing,
			string installed,
			string uninstalling,
			string uninstalled,
			string preinstalling,
			string postinstalling,
			string preinstalled,
			string postinstalled,
			string preuninstalling,
			string postuninstalling,
			string preuninstalled,
			string postuninstalled)
		{
			this.Installing = installing;
			this.Installed = installed;
			this.Uninstalling = uninstalling;
			this.Uninstalled = uninstalled;
			this.PreInstalling = preinstalling;
			this.PostInstalling = postinstalling;
			this.PreInstalled = preinstalled;
			this.PostInstalled = postinstalled;
			this.PreUninstalling = preuninstalling;
			this.PostUninstalling = postuninstalling;
			this.PreUninstalled = preuninstalled;
			this.PostUninstalled = postuninstalled;
		}

		/// <summary>获取安装阶段的原始脚本来源。</summary>
		public readonly string Installing;
		/// <summary>获取安装完成阶段的原始脚本来源。</summary>
		public readonly string Installed;
		/// <summary>获取卸载阶段的原始脚本来源。</summary>
		public readonly string Uninstalling;
		/// <summary>获取卸载完成阶段的原始脚本来源。</summary>
		public readonly string Uninstalled;
		/// <summary>获取安装阶段之前的原始脚本来源。</summary>
		public readonly string PreInstalling;
		/// <summary>获取安装阶段之后的原始脚本来源。</summary>
		public readonly string PostInstalling;
		/// <summary>获取安装完成阶段之前的原始脚本来源。</summary>
		public readonly string PreInstalled;
		/// <summary>获取安装完成阶段之后的原始脚本来源。</summary>
		public readonly string PostInstalled;
		/// <summary>获取卸载阶段之前的原始脚本来源。</summary>
		public readonly string PreUninstalling;
		/// <summary>获取卸载阶段之后的原始脚本来源。</summary>
		public readonly string PostUninstalling;
		/// <summary>获取卸载完成阶段之前的原始脚本来源。</summary>
		public readonly string PreUninstalled;
		/// <summary>获取卸载完成阶段之后的原始脚本来源。</summary>
		public readonly string PostUninstalled;
	}
	#endregion
}
