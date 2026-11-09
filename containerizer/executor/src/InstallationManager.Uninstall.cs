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
	private async Task UninstallAsync(Installation installation, bool purge, CancellationToken cancellation)
	{
		installation.Status = "Uninstalling";
		store.Save(installation);

		try
		{
			if(!purge || !installation.PurgeResourcesCompleted)
			{
				await this.EnterMaintenanceAsync(installation, cancellation);
				await host.UninstallAsync(installation, purge, cancellation);
				installation.PurgeResourcesCompleted = purge;
				store.Save(installation);
			}

			installation.Status = "Uninstalled";

			if(purge)
			{
				using var registrationLock = store.AcquireHostLock();
				store.DeleteAssets(installation.Name);
				await host.FinalizePurgeAsync(installation, cancellation);
				store.DeleteRegistration(installation.Name);
			}
			else
				store.Save(installation);
		}
		catch(Exception exception)
		{
			installation.Status = "CleanupFailed";
			installation.Residuals.Add(exception is ContainerizationException failure ? failure.Message : exception.GetType().Name);
			store.Save(installation);
			throw new ContainerizationException(9, Properties.Resources.Lifecycle_16_Message, exception);
		}
	}
	#endregion
}
