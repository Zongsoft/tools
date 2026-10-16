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
 * Copyright (C) 2015-2026 Zongsoft Corporation <http://www.zongsoft.com>
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
using System.Security;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Security.Cryptography;

using Zongsoft.Text.Templating;

namespace Zongsoft.Tools.Deployer;

/// <summary>使用本次部署变量求值文本模板，在计划阶段准备 UTF-8 输出。</summary>
public sealed class TemplateResolver : DeploymentResolverBase
{
	#region 单例字段
	/// <summary>获取无状态的模板解析器实例。</summary>
	public static readonly TemplateResolver Instance = new();
	#endregion

	#region 构造函数
	private TemplateResolver() : base("template") { }
	#endregion

	#region 重写方法
	protected override string GetFileName(string path)
	{
		var name = Path.GetFileName(path);
		return name.EndsWith(".template", StringComparison.OrdinalIgnoreCase) ? name[..^9] : name;
	}

	protected override async Task PrepareAsync(DeploymentContext context, DeploymentPlan.Operation operation, CancellationToken cancellation)
	{
		var bytes = await File.ReadAllBytesAsync(operation.Source, cancellation);
		using var reader = new StreamReader(new MemoryStream(bytes), new UTF8Encoding(false, true), true);
		var text = await reader.ReadToEndAsync(cancellation);

		//格式化只作用于本次模板内容；变量、路径和其他解析器仍使用 Core 原有规则。
		context.Evaluator.Formatting += Format;

		try
		{
			operation.Content = Encoding.UTF8.GetBytes(context.Evaluator.Evaluate(text));
		}
		catch(TemplateEvaluationException exception)
		{
			throw new InvalidDataException(string.Format(Properties.Resources.Review_Failure, operation.Source, Environment.NewLine + exception.Message), exception);
		}
		finally
		{
			context.Evaluator.Formatting -= Format;
		}

		operation.Kind = "Render";
		operation.SourceHash = Convert.ToHexString(SHA256.HashData(bytes));
		operation.ContentHash = Convert.ToHexString(SHA256.HashData(operation.Content));
	}
	#endregion

	#region 格式处理
	private static void Format(object sender, TemplateEvaluator.FormattingContext context)
	{
		if(string.Equals(context.Format, "xml", StringComparison.OrdinalIgnoreCase))
		{
			context.Text = SecurityElement
				.Escape(context.Value?.ToString() ?? string.Empty)
				.Replace("\r", "&#13;")
				.Replace("\n", "&#10;")
				.Replace("\t", "&#9;");

			context.Handled = true;
		}
		else if(string.Equals(context.Format, "json", StringComparison.OrdinalIgnoreCase))
		{
			context.Text = JsonSerializer.Serialize(context.Value);
			context.Handled = true;
		}
	}
	#endregion
}
