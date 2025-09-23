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
	public readonly struct TargetVersion : IComparable, IComparable<TargetVersion>, IComparable<TargetVersion?>, IEquatable<TargetVersion>
	{
		#region 构造函数
		public TargetVersion(ushort major, ushort minor = 0)
		{
			this.Major = major;
			this.Minor = minor;
		}
		#endregion

		#region 公共字段
		public readonly ushort Major;
		public readonly ushort Minor;
		#endregion

		#region 公共属性
		public bool IsZero => this.Major == 0 && this.Minor == 0;
		#endregion

		#region 静态方法
		public static bool TryParse(ReadOnlySpan<char> text, out TargetVersion result)
		{
			if(text.IsEmpty)
			{
				result = default;
				return false;
			}

			var position = text.IndexOf('.');

			if(position < 0)
			{
				if(ushort.TryParse(text, out var major))
				{
					result = new TargetVersion(major);
					return true;
				}
				else
				{
					result = default;
					return false;
				}
			}

			if(position == 0)
			{
				if(ushort.TryParse(text, out var minor))
				{
					result = new TargetVersion(0, minor);
					return true;
				}
				else
				{
					result = default;
					return false;
				}
			}

			if(position > 0)
			{
				if(ushort.TryParse(text[0..position], out var major))
				{
					if(position < text.Length - 1 && ushort.TryParse(text[(position + 1)..], out var minor))
						result = new TargetVersion(major, minor);
					else
						result = new TargetVersion(major);

					return true;
				}
			}

			result = default;
			return false;
		}
		#endregion

		#region 比较方法
		public int CompareTo(object version) => version is TargetVersion other ? this.CompareTo(other) : 1;
		public int CompareTo(TargetVersion? version) => version == null ? 1 : this.CompareTo(version.Value);
		public int CompareTo(TargetVersion version)
		{
			if(this.Major > version.Major)
				return 1;
			if(this.Major < version.Major)
				return -1;

			if(this.Minor > version.Minor)
				return 1;
			if(this.Minor < version.Minor)
				return -1;

			return 0;
		}
		#endregion

		#region 重写方法
		public bool Equals(TargetVersion other) => this.Major == other.Major && this.Minor == other.Minor;
		public override bool Equals(object obj) => obj is TargetVersion other && this.Equals(other);
		public override int GetHashCode() => HashCode.Combine(this.Major, this.Minor);
		public override string ToString() => $"{this.Major}.{this.Minor}";
		#endregion

		#region 符号重写
		public static bool operator ==(TargetVersion left, TargetVersion right) => left.Equals(right);
		public static bool operator !=(TargetVersion left, TargetVersion right) => !(left == right);
		public static bool operator <(TargetVersion left, TargetVersion right) => left.CompareTo(right) < 0;
		public static bool operator <=(TargetVersion left, TargetVersion right) => left.CompareTo(right) <= 0;
		public static bool operator >(TargetVersion left, TargetVersion right) => left.CompareTo(right) > 0;
		public static bool operator >=(TargetVersion left, TargetVersion right) => left.CompareTo(right) >= 0;
		#endregion
	}
}