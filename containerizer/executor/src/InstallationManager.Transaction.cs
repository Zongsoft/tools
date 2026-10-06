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
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Collections.Generic;

using Zongsoft.Tools.Containerizer.Protocol;

namespace Zongsoft.Tools.Containerizer.Execution;

partial class InstallationManager
{
	#region 内部方法
	internal static string GetPhaseText(string phase) => Properties.Resources.ResourceManager.GetString($"Installation.Phase.{phase}", Properties.Resources.Culture) ?? phase;
	#endregion

	#region 私有方法
	private async Task PhaseAsync(Installation installation, string phase, Func<Task> action)
	{
		installation.Pending.Phase = phase;
		installation.Pending.Events.Add(new() { Phase = phase, Result = "Started" });
		store.Save(installation);

		Console.WriteLine($"  → {installation.Name}: {GetPhaseText(phase)}");
		await action();

		if(!installation.Pending.Completed.Contains(phase))
			installation.Pending.Completed.Add(phase);

		installation.Pending.Events.Add(new() { Phase = phase, Result = "Succeeded" });
		store.Save(installation);
	}

	private async Task FailAsync(Installation installation, Exception exception, bool stop = true)
	{
		installation.Status = "Failed";
		if(stop)
			installation.Maintenance = true;

		if(installation.Pending != null)
		{
			installation.Pending.Failed = true;
			installation.Pending.Events.Add(new()
			{
				Phase = installation.Pending.Phase,
				Result = "Failed",
				ExitCode = exception is ContainerizationException failure ? failure.Code : 4
			});
		}

		store.Save(installation);

		if(!stop)
			return;

		try
		{
			await host.StopApplicationsAsync(installation, CancellationToken.None);
		}
		catch(Exception stopFailure)
		{
			installation.Residuals.Add($"Maintenance stop incomplete: {stopFailure.GetType().Name}");
			store.Save(installation);
		}
	}
	#endregion
}
