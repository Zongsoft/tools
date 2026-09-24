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

using Zongsoft.Components;

namespace Zongsoft.Tools.Packager;

[CommandOption("provides", typeof(string))]
[CommandOption("replaces", typeof(string))]
[CommandOption("breaks", typeof(string))]
[CommandOption("conflicts", typeof(string))]
[CommandOption("recommends", typeof(string))]
[CommandOption("suggests", typeof(string))]
public sealed class DebCommand : PackCommand<Package.Deb>
{
	#region 重写方法
	protected override Package.Deb CreatePackage(CommandContext context, Variables variables)
	{
		var package = new Package.Deb(
			variables.Name,
			variables.Edition,
			variables.Version,
			variables.Platform,
			variables.Architecture,
			variables)
		{
			Provides = ReadRelationship("provides", variables),
			Replaces = ReadRelationship("replaces", variables),
			Breaks = ReadRelationship("breaks", variables),
			Conflicts = ReadRelationship("conflicts", variables),
			Recommends = ReadRelationship("recommends", variables),
			Suggests = ReadRelationship("suggests", variables),
		};

		Configure(package, context);
		return package;
	}

	#endregion

	#region 私有方法
	private static string[] ReadRelationship(string name, Variables variables)
	{
		var value = variables[name];
		return string.IsNullOrWhiteSpace(value) ? [] : value.Split([';', ','], StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
	}
	#endregion
}
