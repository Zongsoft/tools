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
