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

using Zongsoft.Terminals;
using Zongsoft.Components;

namespace Zongsoft.Tools.Containerizer;

partial class ContainerizeCommand
{
	#region 内部方法
	internal static void ShowHelp()
	{
		WriteSyntax("dotnet containerize", " <components...> [options]");
		WriteSyntax("dotnet containerize run", $" <archive.tar.gz> [--engine:{ContainerEngine.OPTIONS}]");
		WriteSyntax("dotnet containerize plan", " <components... | file.container> [options]");
		WriteSyntax("dotnet containerize [make]", $" <file.container> [--version:<version>] [--source:<directory>] [--output:<directory>] [--engine:{ContainerEngine.OPTIONS}] [--refresh]");

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
			$"[--engine:{ContainerEngine.OPTIONS}]",
			"[--imaging:offline|online]",
			"[--bootstrap:offline|online]",
			"[--migration:<directory>]",
			"[--title:<text>] [--description:<text>]",
			"[--refresh]",
		];

		foreach(var option in options)
			Terminal.WriteLine(Utility.HighlightSyntax($"\t{option}"));

		Terminal.WriteLine();
		Terminal.WriteLine(Properties.Resources.Run_Help);
		Terminal.WriteLine(Properties.Resources.RuntimeCache_Help);

		static void WriteSyntax(string command, string arguments) => Terminal.WriteLine(
			CommandOutletContent.Create(CommandOutletStyles.Bold, CommandOutletColor.Cyan, command).Append(Utility.HighlightSyntax(arguments)));
	}
	#endregion
}
