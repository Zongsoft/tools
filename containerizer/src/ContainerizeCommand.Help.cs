/*
 *   _____                                ______
 *  /_   /  ____  ____  ____  _________  / __/ /_
 *    / /  / __ \/ __ \/ __ \/ ___/ __ \/ /_/ __/
 *   / /__/ /_/ / / / /_/ /\_ \/ /_/ / __/ /_
 *  /____/\____/_/ /_/\__  /____/\____/_/  \__/
 *                   /____/
 *
 * Authors:
 *   钟峰(Popeye Zhong) <zongsoft@gmail.com>
 *
 * The MIT License (MIT)
 *
 * Copyright (C) 2026 Zongsoft Corporation <http://www.zongsoft.com>
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

using Zongsoft.Terminals;
using Zongsoft.Components;

namespace Zongsoft.Tools.Containerizer;

partial class ContainerizeCommand
{
	#region 内部方法
	internal static void Help()
	{
		Syntax("dotnet containerize", " <components...> [options]");
		Syntax("dotnet containerize plan", " <components... | file.container> [options]");
		Syntax("dotnet containerize [make]", " <file.container> [--version:<version>] [--source:<directory>] [--output:<directory>] [--engine:auto|docker|podman]");

		Terminal.WriteLine();
		Terminal.WriteLine(CommandOutletStyles.Bold, "Component-list options:");

		string[] options =
		[
			"--name:<name>",
			"--distribution:<distribution[@version]>",
			"[--tag:<tag>]",
			"[--version:<version>]",
			"[--source:<directory>]",
			"[--output:<directory>]",
			"[--architecture:x64|arm64]",
			"[--engine:auto|docker|podman]",
			"[--imaging:offline|online]",
			"[--bootstrap:offline|online]",
			"[--migration:<directory>]",
			"[--title:<text>] [--description:<text>]",
		];

		foreach(var option in options)
			Terminal.WriteLine(Output.Syntax($"\t{option}"));

		static void Syntax(string command, string arguments) => Terminal.WriteLine(
			CommandOutletContent.Create(CommandOutletStyles.Bold, CommandOutletColor.Cyan, command).Append(Output.Syntax(arguments)));
	}
	#endregion
}
