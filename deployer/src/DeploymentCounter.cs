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
 * Copyright (C) 2015-2025 Zongsoft Corporation <http://www.zongsoft.com>
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

namespace Zongsoft.Tools.Deployer;

/// <summary>分别统计部署复制成功、跳过、删除和失败的数量。</summary>
public class DeploymentCounter
{
	#region 成员字段
	private int _skipped;
	private int _deleted;
	private int _failures;
	private int _successes;
	private readonly string _filePath;
	#endregion

	#region 构造函数
	/// <summary>创建部署计数器。</summary>
	/// <param name="filePath">本次部署的 .deploy 文件路径描述。</param>
	public DeploymentCounter(string filePath = null) => _filePath = filePath;
	#endregion

	#region 公共属性
	/// <summary>获取本次部署的文件路径描述。</summary>
	public string FilePath => _filePath;
	/// <summary>获取跳过的操作数。</summary>
	public int Skipped => _skipped;
	/// <summary>获取成功删除的文件数。</summary>
	public int Deleted => _deleted;
	/// <summary>获取已统计的操作总数。</summary>
	public int Total => _failures + _successes + _skipped + _deleted;
	/// <summary>获取失败次数。</summary>
	public int Failures => _failures;
	/// <summary>获取成功写入的文件数。</summary>
	public int Successes => _successes;
	#endregion

	#region 内部方法
	internal void Skip() => System.Threading.Interlocked.Increment(ref _skipped);
	internal void Delete() => System.Threading.Interlocked.Increment(ref _deleted);
	internal void Fail() => System.Threading.Interlocked.Increment(ref _failures);
	internal void Success() => System.Threading.Interlocked.Increment(ref _successes);
	#endregion
}
