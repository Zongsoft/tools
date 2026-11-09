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

using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Zongsoft.Components;
using Zongsoft.Tools.Containerizer.Protocol;

namespace Zongsoft.Tools.Containerizer;

[CommandOption("engine", typeof(string))]
public sealed class RunCommand : CommandBase<CommandContext>
{
	#region 重写方法
	protected override async ValueTask<object> OnExecuteAsync(CommandContext context, CancellationToken cancellation)
	{
		var arguments = context.Arguments.ToArray();
		var choice = context.Options.GetValue("engine", ContainerEngine.AUTO);

		if(arguments.Length != 1 ||
		   !File.Exists(arguments[0]) ||
		   !arguments[0].EndsWith(".tar.gz", StringComparison.OrdinalIgnoreCase) ||
		   choice is not (ContainerEngine.AUTO or ContainerEngine.DOCKER or ContainerEngine.PODMAN))
			throw new ContainerizationException(2, string.Format(Properties.Resources.Run_Usage_Message, ContainerEngine.OPTIONS));

		using var stopping = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
		ConsoleCancelEventHandler handler = (_, args) => { args.Cancel = true; stopping.Cancel(); };
		Console.CancelKeyPress += handler;

		try
		{
			context.Output.WriteLine(Utility.FormatOutput(Properties.Resources.Run_Verify, Path.GetFullPath(arguments[0])));

			ContainerBundle bundle;

			using(Utility.MeasureOutput(content => context.Output.WriteLine(content), Properties.Resources.Run_Elapsed, Properties.Resources.Run_StageVerify))
				bundle = ContainerBundle.Open(arguments[0]);

			using var verified = bundle;
			var engine = await ContainerEngine.ConnectAsync(choice, new ProcessRunner(), stopping.Token, requireCompose: false, error: content => Utility.WriteOutput(context.Error, content));
			engine.Mirrors = RegistryMirrorSettings.Read(Path.GetDirectoryName(Path.GetFullPath(arguments[0])));

			var run = new RunContext(engine, bundle, content => context.Output.WriteLine(content));
			var code = await run.ExecuteAsync(stopping.Token);

			if(code != 0)
				throw new ContainerizationException(code, Properties.Resources.Run_Unsuccessful_Message);

			return null;
		}
		finally { Console.CancelKeyPress -= handler; }
	}
	#endregion
}
