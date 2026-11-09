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

using System.Threading;
using System.Threading.Tasks;

using Zongsoft.Tools.Containerizer.Protocol;

namespace Zongsoft.Tools.Containerizer.Execution;

internal interface IInstallationHost
{
	#region 公共方法
	Task VerifyTargetAsync(ContainerBundle bundle, Installation installation, CancellationToken cancellation);
	Task InstallExecutorAsync(ContainerBundle bundle, CancellationToken cancellation);
	Task PrepareBootstrapAsync(ContainerBundle bundle, Installation installation, CancellationToken cancellation);
	Task PrepareImagesAsync(ContainerBundle bundle, CancellationToken cancellation);
	Task PrepareDirectoriesAsync(ContainerBundle bundle, Installation installation, CancellationToken cancellation);
	Task StartServiceAsync(ContainerBundle bundle, ServicePlan service, CancellationToken cancellation);
	Task WaitForHealthAsync(ContainerBundle bundle, ServicePlan service, CancellationToken cancellation);
	// The caller persists maintenance before stopping; the host persists original restart policies before changing them.
	Task StopApplicationsAsync(Installation installation, CancellationToken cancellation);
	Task RestoreRestartPoliciesAsync(ContainerBundle bundle, CancellationToken cancellation);
	Task RestartServiceAsync(ContainerBundle bundle, ServicePlan service, CancellationToken cancellation);
	Task<int> RunMigrationAsync(ContainerBundle bundle, ContainerBundlePlan.Migration migration, string stateDirectory, string operation, string logPath, CancellationToken cancellation);
	Task UninstallAsync(Installation installation, bool purge, CancellationToken cancellation);
	Task FinalizePurgeAsync(Installation installation, CancellationToken cancellation);
	Task StreamLogsAsync(Installation installation, ExecutorArguments arguments, CancellationToken cancellation);
	#endregion
}
