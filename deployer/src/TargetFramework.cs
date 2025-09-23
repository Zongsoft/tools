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

namespace Zongsoft.Tools.Deployer
{
	public readonly struct TargetFramework : IEquatable<TargetFramework>
	{
		#region 构造函数
		public TargetFramework(string framework, TargetVersion frameworkVersion, string platform = null, TargetVersion platformVersion = default)
		{
			if(string.IsNullOrEmpty(framework))
				throw new ArgumentNullException(nameof(framework));
			if(frameworkVersion.Major <= 0)
				throw new ArgumentOutOfRangeException(nameof(frameworkVersion));

			this.Framework = framework;
			this.FrameworkVersion = frameworkVersion;
			this.Platform = platform;
			this.PlatformVersion = platformVersion;
		}
		#endregion

		#region 公共字段
		public readonly string Framework;
		public readonly TargetVersion FrameworkVersion;
		public readonly string Platform;
		public readonly TargetVersion PlatformVersion;
		#endregion

		#region 公共方法
		public bool IsFramework(string name) => (string.IsNullOrEmpty(this.Framework) && string.IsNullOrEmpty(name)) || string.Equals(this.Framework, name);
		public bool IsPlatform(string name) => (string.IsNullOrEmpty(this.Platform) && string.IsNullOrEmpty(name)) || string.Equals(this.Platform, name);
		#endregion

		#region 静态方法
		public static TargetFramework Parse(ReadOnlySpan<char> text) => TryParse(text, out var result) ? result :
			throw new InvalidOperationException($"The specified '{text}' is an illegal target framework identifier.");

		public static bool TryParse(ReadOnlySpan<char> text, out TargetFramework result)
		{
			if(text.IsEmpty)
			{
				result = default;
				return false;
			}

			var position = text.IndexOf('-');

			if(position < 0)
			{
				if(TryParsePart(text, out var framework, out var frameworkVersion))
				{
					result = new(framework.ToString(), frameworkVersion);
					return true;
				}

				result = default;
				return false;
			}

			if(position > 0 && position < text.Length - 1)
			{
				if(TryParsePart(text[0..position], out var framework, out var frameworkVersion))
				{
					if(TryParsePart(text[(position + 1)..], out var platform, out var platformVersion))
					{
						result = new(framework.ToString(), frameworkVersion, platform.ToString(), platformVersion);
						return true;
					}
				}
			}

			result = default;
			return false;
		}

		private static bool TryParsePart(ReadOnlySpan<char> text, out ReadOnlySpan<char> name, out TargetVersion version)
		{
			if(text.IsEmpty)
			{
				name = default;
				version = default;
				return false;
			}

			for(int i = 0; i < text.Length; i++)
			{
				if(char.IsDigit(text[i]))
				{
					name = text[..i];
					TargetVersion.TryParse(i < text.Length - 1 ? text[i..] : default, out version);
					return true;
				}
			}

			name = text;
			version = default;
			return true;
		}
		#endregion

		#region 重写方法
		public bool Equals(TargetFramework other) =>
			this.IsFramework(other.Framework) &&
			this.FrameworkVersion == other.FrameworkVersion &&
			this.IsPlatform(other.Platform) &&
			this.PlatformVersion == other.PlatformVersion;

		public override bool Equals(object obj) => obj is TargetFramework other && this.Equals(other);
		public override int GetHashCode() => HashCode.Combine(this.Framework, this.FrameworkVersion, this.Platform, this.PlatformVersion);
		public override string ToString()
		{
			if(string.IsNullOrEmpty(this.Platform))
				return $"{this.Framework}{this.FrameworkVersion}";

			return this.PlatformVersion.IsZero ?
				$"{this.Framework}{this.FrameworkVersion}-{this.Platform}" :
				$"{this.Framework}{this.FrameworkVersion}-{this.Platform}{this.PlatformVersion}";
		}
		#endregion

		#region 符号重写
		public static bool operator ==(TargetFramework left, TargetFramework right) => left.Equals(right);
		public static bool operator !=(TargetFramework left, TargetFramework right) => !(left == right);
		#endregion
	}
}