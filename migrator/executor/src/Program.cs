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

namespace Zongsoft.Tools.Migrator.Migration;

internal static class Program
{
	#region 程序入口
	private static async Task<int> Main(string[] args)
	{
		#region 参数校验
		if(args.Length != 3 || args[0] is not ("apply" or "status" or "check"))
		{
			Console.Error.WriteLine(Properties.Resources.CommandUsage);
			return 2;
		}
		#endregion

		#region 升迁执行
		try
		{
			var plan = MigrationPlan.Load(args[1]);

			try
			{
				MigrationRuntime.EnsureCurrent(plan.Runtime);
			}
			catch(InvalidDataException ex)
			{
				throw new MigrationException(ex.Message);
			}

			var state = Path.GetFullPath(args[2]);

			if(args[0] == "status")
			{
				var path = Path.Combine(state, "status.json");
				Console.WriteLine(File.Exists(path) ? File.ReadAllText(path) : Properties.Resources.MigrationNotRun);
				return MigrationExecutor.IsReady(plan, state) ? 0 : 1;
			}

			if(args[0] == "check")
			{
				if(MigrationExecutor.IsReady(plan, state))
					return 0;
				Console.Error.WriteLine(Properties.Resources.MigrationNotCompleted);
				return 1;
			}

			using var cancellation = new CancellationTokenSource();
			Console.CancelKeyPress += (_, e) => { e.Cancel = true; cancellation.Cancel(); };

			await new MigrationExecutor()
				.ApplyAsync(plan, new(Path.GetDirectoryName(Path.GetFullPath(args[1])), state, Console.WriteLine), cancellation.Token);

			return 0;
		}
		catch(MigrationException ex)
		{
			Console.Error.WriteLine(ex.Message);
			return 1;
		}
		catch(MigrationPrivilegeException ex)
		{
			Console.Error.WriteLine(ex.Message);
			return 1;
		}
		catch(Exception ex)
		{
			Console.Error.WriteLine(string.Format(Properties.Resources.RunnerFailed, ex.GetType().Name));
			return 1;
		}
		#endregion
	}
	#endregion
}
