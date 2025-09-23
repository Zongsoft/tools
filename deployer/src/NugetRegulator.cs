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
 * Copyright (C) 2015-2023 Zongsoft Corporation <http://www.zongsoft.com>
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
using System.Collections.Generic;

namespace Zongsoft.Tools.Deployer
{
	public class NugetRegulator : IDirectoryRegulator
	{
		#region 单例字段
		public static readonly NugetRegulator Instance = new NugetRegulator();
		#endregion

		#region 私有构造
		private NugetRegulator() { }
		#endregion

		#region 公共方法
		public bool Regulate(string directory, IDictionary<string, string> variables, out string result)
		{
			result = null;

			if(variables == null || variables.Count == 0 || string.IsNullOrEmpty(directory))
				return false;

			//获取Nuget包路径
			var nugetDirectory = NugetUtility.GetPackagesDirectory(variables);

			//如果指定的路径是不是Nuget包路径的子路径则返回
			if(!directory.StartsWith(nugetDirectory, OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal))
				return false;

			//如果没有指定“目标框架”参数则无需进行路径修整
			if(!Utility.TryGetTargetFramework(variables, out var framework))
				return false;

			result = RegulateLibraryPath(directory, framework);
			return !string.IsNullOrEmpty(result);
		}
		#endregion

		#region 私有方法
		private static readonly char[] PATH_SEPARATORS = new[] { '/', '\\' };

		/// <summary>从指定的库路径中查找最适用的框架版本，并将库路径中的框架替换为适用框架。</summary>
		/// <param name="path">待修整的库路径。</param>
		/// <param name="framework">当前系统的框架版本。</param>
		/// <returns>返回被修整过的库路径，如果指定的路径不是一个有效的库路径则返回空。</returns>
		/// <example>
		///		<para>
		///			假设 %NuGet_Packages%/mysql.data/8.1.10/lib 包库目录下有：
		///		</para>
		///		<list type="bullet">
		///			<item>net6.0</item>
		///			<item>net7.0</item>
		///			<item>net8.0</item>
		///			<item>netstandard2.0</item>
		///			<item>netstandard2.1</item>
		///		</list>
		///		<para>则 path 参数与之对应的返回值：</para>
		///		<list type="bullet">
		///			<item>
		///				<term>%NuGet_Packages%\mysql.data\8.1.0\lib\net9.0</term>
		///				<description>%NuGet_Packages%\mysql.data\8.1.0\lib\net8.0</description>
		///			</item>
		///			<item>
		///				<term>%NuGet_Packages%\mysql.data\8.1.0\lib</term>
		///				<description>%NuGet_Packages%\mysql.data\8.1.0\lib\net8.0</description>
		///			</item>
		///			<item>
		///				<term>%NuGet_Packages%\mysql.data\8.1.0\lib\net9.0\*.dll</term>
		///				<description>%NuGet_Packages%\mysql.data\8.1.0\lib\net8.0\*.dll</description>
		///			</item>
		///			<item>
		///				<term>%NuGet_Packages%\mysql.data\8.1.0</term>
		///				<description><c>null</c></description>
		///			</item>
		///		</list>
		/// </example>
		private static string RegulateLibraryPath(string path, string framework)
		{
			var parts = path.Split(PATH_SEPARATORS);

			for(int i = 0; i < parts.Length; i++)
			{
				if(parts[i] == "lib")
				{
					var segment = new ArraySegment<string>(parts, 0, i);
					var libraryPath = Path.Combine(segment.ToArray());

					if(i < parts.Length - 1 && !string.IsNullOrEmpty(parts[i + 1]))
						framework = parts[i + 1];

					var result = NugetUtility.GetNearestLibraryPath(libraryPath, framework);
					if(string.IsNullOrEmpty(result))
						return null;

					if(parts.Length <= i + 2)
						return result;

					return Path.Combine(result, string.Join(Path.PathSeparator, parts, i + 2, parts.Length - i - 2));
				}
			}

			return null;
		}
		#endregion
	}
}