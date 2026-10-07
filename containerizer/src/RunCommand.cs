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
		var choice = context.Options.GetValue<string>("engine") ?? ContainerEngine.AUTO;

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
			context.Output.WriteLine(Output.FormatMessage(Properties.Resources.Run_Verify, Path.GetFullPath(arguments[0])));

			DeliveryBundle bundle;

			using(Output.Measure(content => context.Output.WriteLine(content), Properties.Resources.Run_StageVerify))
				bundle = DeliveryBundle.Open(arguments[0]);

			using var verified = bundle;
			var engine = await ContainerEngine.ConnectAsync(choice, new ProcessRunner(), stopping.Token, requireCompose: false, error: content => context.Error.WriteLine(content));
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
