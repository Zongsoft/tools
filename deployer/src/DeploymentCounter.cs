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
 * Copyright (C) 2015-2017 Zongsoft Corporation <http://www.zongsoft.com>
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

namespace Zongsoft.Tools.Deployer
{
	public class DeploymentCounter
	{
		#region 成员字段
		private int _failures;
		private int _successes;
		private readonly string _filePath;
		#endregion

		#region 构造函数
		public DeploymentCounter(string filePath) => _filePath = filePath;
		public DeploymentCounter(string filePath, int failures, int successes)
		{
			_filePath = filePath;
			_failures = failures;
			_successes = successes;
		}
		#endregion

		#region 公共属性
		public string FilePath => _filePath;
		public int Total => _failures + _successes;
		public int Failures => _failures;
		public int Successes => _successes;
		#endregion

		#region 内部方法
		internal int Fail(int interval = 1)
		{
			int result = 0;

			for(int i = 0; i < interval; i++)
				result = System.Threading.Interlocked.Increment(ref _failures);

			return result;
		}

		internal int Success(int interval = 1)
		{
			int result = 0;

			for(int i = 0; i < interval; i++)
				result = System.Threading.Interlocked.Increment(ref _successes);

			return result;
		}
		#endregion
	}
}
