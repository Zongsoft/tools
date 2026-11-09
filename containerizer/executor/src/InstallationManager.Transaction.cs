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
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Collections.Generic;

using Zongsoft.Tools.Containerizer.Protocol;

namespace Zongsoft.Tools.Containerizer.Execution;

partial class InstallationManager
{
	#region 私有方法
	private static string GetPhaseText(string phase) => Properties.Resources.ResourceManager.GetString($"Installation.Phase.{phase}", Properties.Resources.Culture) ?? phase;

	private async Task EnterMaintenanceAsync(Installation installation, CancellationToken cancellation)
	{
		installation.IsInMaintenance = true;
		store.Save(installation);
		await host.StopApplicationsAsync(installation, cancellation);
	}

	private async Task ExecutePhaseAsync(Installation installation, string phase, Func<Task> action)
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

	private async Task FailAsync(Installation installation, Exception exception, bool stopApplications = true)
	{
		installation.Status = "Failed";
		if(stopApplications)
			installation.IsInMaintenance = true;

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

		if(!stopApplications)
			return;

		try
		{
			await this.EnterMaintenanceAsync(installation, CancellationToken.None);
		}
		catch(Exception stopFailure)
		{
			installation.Residuals.Add($"Maintenance stop incomplete: {stopFailure.GetType().Name}");
			store.Save(installation);
		}
	}
	#endregion
}
