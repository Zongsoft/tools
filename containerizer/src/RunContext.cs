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
using System.Collections.Generic;

using Zongsoft.Components;
using Zongsoft.Tools.Containerizer.Protocol;

namespace Zongsoft.Tools.Containerizer;

internal sealed partial class RunContext
{
	#region 常量定义
	internal const string OWNER_LABEL = "org.zongsoft.containerizer.run";
	private const string APPLICATION_LABEL = "org.zongsoft.containerizer.application";
	#endregion

	#region 成员字段
	private readonly ContainerEngine _engine;
	private readonly DeliveryBundle _bundle;
	private readonly Action<CommandOutletContent> _output;
	private readonly Func<string, IReadOnlyList<string>, string, CancellationToken, Task<int>> _stream;
	private readonly string _identity = Guid.NewGuid().ToString("N");
	private readonly string _workspace = Path.Combine(Path.GetTempPath(), $"containerizer-run-{Guid.NewGuid():N}");
	private readonly List<Endpoint> _endpoints;
	private bool _created;
	private ImageCache _cache;
	#endregion

	#region 构造函数
	internal RunContext(ContainerEngine engine, DeliveryBundle bundle, Action<CommandOutletContent> output,
		Func<string, IReadOnlyList<string>, string, CancellationToken, Task<int>> stream = null)
	{
		_engine = engine;
		_bundle = bundle;
		_output = output;
		_stream = stream ?? ProcessRunner.StreamAsync;
		_endpoints = Endpoint.Create(bundle.Plan);
	}
	#endregion

	#region 公共属性
	public string Name => $"containerizer-run-{_identity}";
	#endregion

	#region 公共方法
	public async Task<int> ExecuteAsync(CancellationToken cancellation)
	{
		using var appLock = this.Lock();

		var existing = await _engine.RunAsync(
		[
			"ps",
			"--all",
			"--filter",
			$"label={OWNER_LABEL}",
			"--filter",
			$"label={APPLICATION_LABEL}={_bundle.Plan.Name}",
			"--format",
			"{{.Names}}"
		], null, cancellation);

		if(!string.IsNullOrWhiteSpace(existing))
			throw new ContainerizationException(3, string.Format(Properties.Resources.Run_Existing_Message, existing.Trim(), _engine.Executable));

		var code = 130;
		Files.PrivateDirectory(_workspace);

		try
		{
			await this.CheckArchitectureAsync(cancellation);
			string image;

			using(Output.Measure(_output, Properties.Resources.Run_StageBase))
				image = await this.PrepareBaseAsync(cancellation);

			_cache = new ImageCache(_engine, _bundle.Plan, _identity);

			using(Output.Measure(_output, Properties.Resources.Run_StageCache))
				await _cache.PrepareAsync(image, cancellation);

			await this.StartAsync(image, cancellation);

			_output(Output.Message(Properties.Resources.Run_Container, this.Name, _engine.Executable, BootstrapPlan.ENGINE));

			using(Output.Measure(_output, Properties.Resources.Run_StageInstall))
				await this.InstallAsync(cancellation);

			using(Output.Measure(_output, Properties.Resources.Run_StageForward))
				await this.ForwardAsync(cancellation);

			_output(Output.Message(string.Empty));
			_output(Output.Message(Properties.Resources.Run_Ready, CommandOutletColor.Green, _bundle.Plan.Name));
			_output(Output.Message(Properties.Resources.Run_ReadyHint, "Ctrl+C"));
			code = 0;
			await Task.Delay(Timeout.Infinite, cancellation);
		}
		catch(OperationCanceledException) when(cancellation.IsCancellationRequested) { }
		catch(Exception exception) when(_created)
		{
			code = exception is ContainerizationException failure ? failure.Code : 4;
			_output(Output.Message(Properties.Resources.Run_Failed, CommandOutletColor.Magenta, exception.Message, this.Name));

			try
			{
				await Task.Delay(Timeout.Infinite, cancellation);
			}
			catch(OperationCanceledException) when(cancellation.IsCancellationRequested) { }
		}
		finally
		{
			try
			{
				await this.CleanupAsync();
				_output(Output.Message(Properties.Resources.Run_Cleaned, this.Name));
			}
			catch(Exception exception)
			{
				_output(Output.Message(Properties.Resources.Run_CleanupFailed, CommandOutletColor.Magenta, this.Name, exception.Message));

				if(code == 0)
					code = 4;
			}

			Directory.Delete(_workspace, true);
		}

		return code;
	}
	#endregion

	#region 私有方法
	private FileStream Lock()
	{
		try
		{
			return BuildStorage.Lock(Path.Combine(BuildStorage.CacheRoot, "run", _bundle.Plan.Name));
		}
		catch(IOException exception)
		{
			throw new ContainerizationException(3, string.Format(Properties.Resources.Run_Locked_Message, _bundle.Plan.Name), exception);
		}
	}

	private async Task CleanupAsync()
	{
		// An interrupted create can leave a container even if the CLI never returned its ID.
		if(_cache != null && _created)
		{
			using var imageCleanup = new CancellationTokenSource(TimeSpan.FromSeconds(60));

			try
			{
				await _cache.CleanAsync(this.Name, imageCleanup.Token);
			}
			catch(Exception exception)
			{
				_output(Output.Message(Properties.Resources.Run_CacheDiscarded, CommandOutletColor.Magenta, _cache.Name, exception.Message));
			}
		}

		var failures = new List<Exception>();

		try
		{
			using var cleanup = new CancellationTokenSource(TimeSpan.FromSeconds(90));
			await this.RemoveContainersAsync(cleanup.Token);
		}
		catch(Exception exception) { failures.Add(exception); }

		if(_cache != null)
		{
			try
			{
				using var cacheCleanup = new CancellationTokenSource(TimeSpan.FromSeconds(60));
				await _cache.ReleaseAsync(cacheCleanup.Token);
			}
			catch(Exception exception) { failures.Add(exception); }
		}

		if(failures.Count != 0)
			throw new AggregateException(failures);
	}

	private async Task RemoveContainersAsync(CancellationToken cancellation)
	{
		var owned = await _engine.RunAsync(["ps", "--all", "--filter", $"label={OWNER_LABEL}={_identity}", "--format", "{{.ID}}"], null, cancellation, 20);
		var failures = new List<Exception>();

		foreach(var id in owned.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
		{
			try
			{
				await _engine.RunAsync(["rm", "--force", "--volumes", id], null, cancellation, 60);
			}
			catch(Exception exception) { failures.Add(exception); }
		}

		if(failures.Count != 0)
			throw new AggregateException(failures);

		_created = false;
	}
	#endregion
}
