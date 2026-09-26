/*
 *   _____                                ______
 *  /_   /  ____  ____  ____  _________  / __/ /_
 *    / /  / __ \/ __ \/ __ \/ ___/ __ \/ /_/ __/
 *   / /__/ /_/ / / / / /_/ /\_ \/ /_/ / __/ /_
 *  /____/\____/_/ /_/\__  /____/\____/_/  \__/
 *                   /____/
 *
 * Authors:
 *   钟峰(Popeye Zhong) <zongsoft@gmail.com>
 *
 * The MIT License (MIT)
 *
 * Copyright (C) 2015-2026 Zongsoft Corporation <http://www.zongsoft.com>
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

using Zongsoft.Terminals;
using Zongsoft.Configuration.Profiles;

namespace Zongsoft.Tools.Deployer;

/// <summary>协调描述文件解析、NuGet 依赖求解、计划校验及部署执行。</summary>
/// <remarks>同一实例支持顺序调用并为每次调用创建独立会话，不允许并发部署。</remarks>
public partial class Deployer
{
	#region 常量定义
	internal const string EXPANSION_OPTION = "expansion";
	internal const string OVERWRITE_OPTION = "overwrite";
	internal const string VERBOSITY_OPTION = "verbosity";
	internal const string DESTINATION_OPTION = "destination";
	internal const string IGNOREDEPENDENTPREFIX_OPTION = "ignoreDependentPrefix";
	internal const string IGNOREDEPLOYMENTFILE_OPTION = "ignoreDeploymentFile";
	internal const string DEFAULT_DEPLOYMENT_FILENAME = ".deploy";
	#endregion

	#region 私有变量
	private int _running;
	#endregion

	#region 构造函数
	public Deployer(IDictionary<string, string> variables) : this(variables, Console.Out)
	{
	}

	public Deployer(IDictionary<string, string> variables, TextWriter output)
	{
		ArgumentNullException.ThrowIfNull(variables);

		var raw = variables is VariableMap map ?
			new Dictionary<string, string>(map.Raw, StringComparer.OrdinalIgnoreCase) :
			new Dictionary<string, string>(variables, StringComparer.OrdinalIgnoreCase);

		NugetUtility.Initialize(raw);
		this.Variables = new VariableMap(raw, text => Normalizer.Normalize(text, raw, name => throw new FormatException(string.Format(Properties.Resources.Review_UndefinedVariable, name))));
		this.Output = output ?? TextWriter.Null;
	}
	#endregion

	#region 公共属性
	public ITerminal Terminal => Terminals.Terminal.Console;
	public TextWriter Output { get; }
	public IDictionary<string, string> Variables { get; }
	public DeploymentPlan Plan { get; private set; }
	#endregion

	#region 内部属性
	internal DeploymentSession Session { get; private set; }
	internal Overwrite Overwrite { get; private set; }
	#endregion

	#region 公共方法
	public Task<DeploymentCounter> DeployAsync(string path, CancellationToken cancellation = default) => this.DeployAsync(path, null, cancellation);

	public Task<DeploymentCounter> DeployAsync(string path, string destinationDirectory, CancellationToken cancellation = default) => this.DeployManyAsync([path], destinationDirectory, cancellation);

	public async Task<DeploymentCounter> DeployManyAsync(IEnumerable<string> paths, string destinationDirectory = null, CancellationToken cancellation = default)
	{
		if(Interlocked.Exchange(ref _running, 1) != 0)
			throw new InvalidOperationException(string.Format(Properties.Resources.Review_Busy, "Deployer"));

		this.Plan = null;
		var counter = new DeploymentCounter();
		NugetUtility.ResetCache(this.Variables);

		try
		{
			var files = paths.ToArray();
			counter = new DeploymentCounter(string.Join(";", files));
			destinationDirectory ??= this.Variables.TryGetValue(DESTINATION_OPTION, out var target) ? target : Environment.CurrentDirectory;

			var root = Path.GetFullPath(this.Normalize(destinationDirectory));
			this.Session = new DeploymentSession(root, counter);
			this.Plan = this.Session.Plan;
			this.Overwrite = GetOverwrite(this.Variables);

			ValidateVerbosity(this.Variables);

			this.Session.Validate(root);

			if(Flag(this.Variables, "locked"))
			{
				if(!this.Variables.TryGetValue("lockFile", out var lockFile))
					throw new InvalidOperationException(string.Format(Properties.Resources.Review_Locked, "lockFile"));

				this.Session.LockedPlan = DeploymentPlan.Load(this.Normalize(lockFile));
			}

			foreach(var file in files.Distinct(DeploymentPath.Comparer))
				await this.PlanManifestAsync(file, root, cancellation);

			if(counter.Failures == 0)
			{
				this.Session.Packages = await NugetGraph.ResolveAsync(this.Variables, this.Session.Roots, cancellation, this.Session.LockedPlan?.Packages);

				foreach(var placeholder in this.Plan.Operations.Where(item => item.Expand != null).ToArray())
				{
					var index = this.Plan.Operations.IndexOf(placeholder);
					this.Session.Output = [];
					await placeholder.Expand(cancellation);
					this.Plan.Operations.RemoveAt(index);
					this.Plan.Operations.InsertRange(index, this.Session.Output);
					this.Session.Output = null;
				}

				this.ValidatePlan();
			}

			if(counter.Failures == 0)
				await this.ExecutePlanAsync(cancellation);

			this.Plan.Succeeded = counter.Failures == 0;
		}
		catch(OperationCanceledException)
		{
			throw;
		}
		catch(Exception exception)
		{
			counter.Fail();
			this.Plan?.Succeeded = false;
			this.Error(exception.Message);
		}
		finally
		{
			if(this.Session != null && this.Variables.TryGetValue("report", out var report) && !string.IsNullOrWhiteSpace(report))
			{
				try
				{
					var path = Path.GetFullPath(this.Normalize(report));
					this.ValidateOutput(path, true);
					this.Plan.Save(path);
				}
				catch(Exception exception)
				{
					counter.Fail();
					this.Plan.Succeeded = false;
					this.Error(exception.Message);
				}
			}

			this.Session = null;
			Volatile.Write(ref _running, 0);
		}

		return counter;
	}
	#endregion

	#region 虚拟方法
	protected virtual DeploymentContext CreateContext(string path, string destination)
	{
		var options = new ProfileOptions
		{
			Importing = context => this.Plan.Manifests[context.FilePath] = DeploymentSession.Hash(context.FilePath),
		};

		return new(this, Profile.Load(path, options), destination);
	}
	#endregion

	#region 清单解析
	internal async Task PlanManifestAsync(string path, string destination, CancellationToken cancellation)
	{
		cancellation.ThrowIfCancellationRequested();
		path = Path.GetFullPath(this.Normalize(path));

		if(Directory.Exists(path))
			path = Path.Combine(path, DEFAULT_DEPLOYMENT_FILENAME);

		var identity = DeploymentPath.Identity(path);

		if(this.Session.Active.Count >= 64 || !this.Session.Active.Add(identity))
			throw new InvalidOperationException(string.Format(Properties.Resources.Review_Cycle, Utility.Indent(string.Join(" ->" + Environment.NewLine, this.Session.Stack.Append(path)))));

		this.Session.Stack.Add(path);

		try
		{
			if(DeploymentPath.IsMissing(path))
			{
				this.Session.Counter.Skip();
				var warning = string.Format(Properties.Resources.Review_MissingFile, path);
				this.Plan.Diagnostics.Add(warning);
				this.Output.WriteLine(warning);
				return;
			}

			if(!File.Exists(path))
				throw new FileNotFoundException(string.Format(Properties.Resources.Review_Missing, path));

			this.Plan.Manifests[path] = DeploymentSession.Hash(path);

			var context = this.CreateContext(path, this.Session.Validate(destination));

			foreach(var item in context.Profile)
				await this.PlanItemAsync(context, item, cancellation);
		}
		finally
		{
			this.Session.Active.Remove(identity);
			this.Session.Stack.RemoveAt(this.Session.Stack.Count - 1);
		}
	}

	private async Task PlanItemAsync(DeploymentContext context, ProfileItem item, CancellationToken cancellation)
	{
		cancellation.ThrowIfCancellationRequested();

		if(item is ProfileSection section)
		{
			foreach(var child in section)
				await this.PlanItemAsync(context, child, cancellation);
		}
		else if(item is ProfileEntry entry)
		{
			try
			{
				//Evaluate filters before expanding optional branch variables.
				Utility.Requisition.GetRequisites(entry.Name, out var sourceFilter);
				Utility.Requisition.GetRequisites(entry.Value, out var targetFilter);

				if(!Utility.Requisition.IsRequisites(this.Variables, sourceFilter) || !Utility.Requisition.IsRequisites(this.Variables, targetFilter))
				{
					context.Counter.Skip();

					return;
				}

				var deployment = DeploymentEntry.Get(context, entry);
				var resolver = DeploymentResolverManager.GetResolver(deployment.Name) ?? throw new FormatException(string.Format(Properties.Resources.Review_UnknownResolver, deployment.Name));
				await resolver.ResolveAsync(context, deployment, cancellation);
			}
			catch(OperationCanceledException)
			{
				throw;
			}
			catch(Exception exception)
			{
				context.Counter.Fail();
				this.Error(string.Format(Properties.Resources.Review_Failure, $"{entry.Profile.FilePath}:{entry.LineNumber}", Environment.NewLine + exception.Message));
			}
		}
	}
	#endregion

	#region 辅助方法
	internal void Error(string text)
	{
		this.Plan?.Diagnostics.Add(text);
		this.Output.WriteLine(text);
	}

	internal string Normalize(string text) => Normalizer.Normalize(text, this.Variables, name => throw new FormatException(string.Format(Properties.Resources.Review_UndefinedVariable, name)));

	private static void ValidateVerbosity(IDictionary<string, string> variables)
	{
		if(variables.TryGetValue(VERBOSITY_OPTION, out var verbosity) &&
			!Zongsoft.Common.Convert.TryConvertValue<Verbosity>(verbosity, out _))
			throw new ArgumentException(string.Format(Properties.Resources.Review_InvalidOption, VERBOSITY_OPTION, verbosity));
	}

	internal static bool Flag(IDictionary<string, string> variables, string key)
	{
		if(!variables.TryGetValue(key, out var value))
			return false;

		if(string.IsNullOrEmpty(value))
			return true;

		// 部署器使用变量字典，按 Core Switch 的判断顺序处理展开后的值。
		if(Zongsoft.Common.Convert.TryConvertValue<bool>(value, out var result))
			return result;

		return string.Equals(value, "1", StringComparison.OrdinalIgnoreCase) ||
			string.Equals(value, "on", StringComparison.OrdinalIgnoreCase) ||
			string.Equals(value, "yes", StringComparison.OrdinalIgnoreCase) ||
			string.Equals(value, "enable", StringComparison.OrdinalIgnoreCase) ||
			string.Equals(value, "enabled", StringComparison.OrdinalIgnoreCase);
	}

	internal static Overwrite GetOverwrite(IDictionary<string, string> variables)
	{
		if(!variables.TryGetValue(OVERWRITE_OPTION, out var text))
			return Overwrite.Newest;

		if(Zongsoft.Common.Convert.TryConvertValue<Overwrite>(text, out var result))
			return result;

		throw new ArgumentException(string.Format(Properties.Resources.Review_InvalidOption, OVERWRITE_OPTION, text));
	}
	#endregion
}
