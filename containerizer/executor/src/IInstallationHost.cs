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

using System.Threading;
using System.Threading.Tasks;

using Zongsoft.Tools.Containerizer.Protocol;

namespace Zongsoft.Tools.Containerizer.Execution;

internal interface IInstallationHost
{
	#region 公共方法
	Task VerifyAsync(DeliveryBundle bundle, Installation installation, CancellationToken cancellation);
	Task InstallExecutorAsync(DeliveryBundle bundle, CancellationToken cancellation);
	Task BootstrapAsync(DeliveryBundle bundle, Installation installation, CancellationToken cancellation);
	Task ImagesAsync(DeliveryBundle bundle, CancellationToken cancellation);
	Task DirectoriesAsync(DeliveryBundle bundle, Installation installation, CancellationToken cancellation);
	Task StartAsync(DeliveryBundle bundle, ServicePlan service, CancellationToken cancellation);
	Task HealthyAsync(DeliveryBundle bundle, ServicePlan service, CancellationToken cancellation);
	Task StopApplicationsAsync(Installation installation, CancellationToken cancellation);
	Task RestorePoliciesAsync(DeliveryBundle bundle, CancellationToken cancellation);
	Task RestartAsync(DeliveryBundle bundle, ServicePlan service, CancellationToken cancellation);
	Task<int> MigrateAsync(DeliveryBundle bundle, MigrationPlan migration, string state, string operation, string log, CancellationToken cancellation);
	Task UninstallAsync(Installation installation, bool purge, CancellationToken cancellation);
	Task FinalizePurgeAsync(Installation installation, CancellationToken cancellation);
	Task LogsAsync(Installation installation, ExecutorArguments arguments, CancellationToken cancellation);
	#endregion
}
