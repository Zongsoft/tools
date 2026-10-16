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

namespace Zongsoft.Tools.Deployer;

partial class Deployer
{
	#region 计划验证
	private void ValidatePlan()
	{
		var outputs = new Dictionary<string, DeploymentPlan.Operation>(DeploymentPath.Comparer);

		ValidateOperations();
		CollectPackages();
		ValidateReports();
		ValidateLock();
		CollectStaleOperations();

		void ValidateOperations()
		{
			foreach(var operation in this.Plan.Operations)
			{
				this.Session.Validate(operation.Output);

				if(operation.Kind == "Delete")
				{
					outputs.Remove(operation.Output);
					continue;
				}

				var resolved = DeploymentPath.Identity(operation.Source);
				operation.ResolvedSource = DeploymentPath.Comparer.Equals(resolved, Path.GetFullPath(operation.Source)) ? null : resolved;
				var hash = DeploymentSession.Hash(operation.ResolvedSource ?? operation.Source);

				if(operation.SourceHash != null && operation.SourceHash != hash)
					throw new IOException(string.Format(Properties.Resources.Review_Conflict, operation.Source));

				operation.SourceHash = hash;

				if(outputs.TryGetValue(operation.Output, out var previous) && previous.Package != null && operation.Package != null)
				{
					if((previous.ContentHash ?? previous.SourceHash) != (operation.ContentHash ?? operation.SourceHash))
						throw new IOException(string.Format(Properties.Resources.Review_Conflict, $"{operation.Output}: {previous.Package} / {operation.Package}"));

					operation.Status = "Duplicate";
					operation.Reason = "Identical package asset already planned for this output";
					continue;
				}

				outputs[operation.Output] = operation;
			}
		}

		void CollectPackages()
		{
			var packages = this.Session.Packages.Values
				.Concat(this.Session.Requested.Values)
				.DistinctBy(package => package.Identity.ToString())
				.OrderBy(package => package.Identity.Id, StringComparer.OrdinalIgnoreCase)
				.ThenBy(package => package.Identity.Version);

			foreach(var package in packages)
			{
				var selection = new DeploymentPlan.Package
				{
					Id = package.Identity.Id,
					Version = package.Identity.Version.ToNormalizedString(),
					Framework = Utility.GetTargetFramework(this.Evaluator),
					Hash = NugetUtility.PackageHash(this.Evaluator, package),
				};

				if(this.Session.Requested.ContainsKey(package.Identity.ToString()))
					selection.RequiredBy.Add("manifest");

				foreach(var root in this.Session.Roots)
				{
					if(root.Metadata.Identity.Equals(package.Identity))
						selection.RequiredBy.Add("root");
				}

				foreach(var parent in this.Session.Packages.Values)
				{
					foreach(var framework in this.Session.Roots.Select(root => root.Framework).Distinct())
					{
						foreach(var dependency in NugetGraph.GetDependencies(parent, framework))
						{
							if(StringComparer.OrdinalIgnoreCase.Equals(dependency.Id, selection.Id))
								selection.RequiredBy.Add($"{parent.Identity} {dependency.VersionRange} ({framework})");
						}
					}
				}

				this.Plan.Packages.Add(selection);
			}
		}

		void ValidateReports()
		{
			foreach(var key in new[] { "report", "lockFile" })
			{
				if(this.Evaluator.TryGetOption(key, out var output) && !string.IsNullOrEmpty(output))
				{
					var path = this.GetPath(output);
					this.ValidateOutput(path, key == "report");
				}
			}
		}

		void ValidateLock()
		{
			if(Flag(this.Evaluator, "locked"))
			{
				if(!this.Evaluator.TryGetOption("lockFile", out var lockPath) || !File.Exists(this.GetPath(lockPath)))
					throw new InvalidOperationException(string.Format(Properties.Resources.Review_Locked, "lockFile"));

				var previous = DeploymentPlan.Load(this.GetPath(lockPath));

				if(!Matches(previous, this.Plan))
					throw new InvalidOperationException(string.Format(Properties.Resources.Review_Locked, lockPath));
			}
		}

		void CollectStaleOperations()
		{
			if(this.Evaluator.TryGetOption("previous", out var previousPath))
			{
				var previous = DeploymentPlan.Load(this.GetPath(previousPath));

				if(!previous.Succeeded || !DeploymentPath.Comparer.Equals(Path.GetFullPath(previous.Root), this.Plan.Root))
					throw new InvalidOperationException(string.Format(Properties.Resources.Review_Locked, "previous root/result"));

				foreach(var group in previous.Operations.Where(operation => operation.Status != "Duplicate").GroupBy(operation => operation.Output, DeploymentPath.Comparer))
				{
					var item = group.Last();
					var path = this.Session.Validate(item.Output);

					if(item.Kind is not ("Copy" or "Render") || item.Status is not ("Copied" or "Rendered" or "Unchanged"))
						continue;

					if(outputs.ContainsKey(path) || !File.Exists(path))
						continue;

					var unchanged = item.Hash != null && DeploymentSession.Hash(path) == item.Hash;
					this.Plan.Operations.Add(new DeploymentPlan.Operation

					{
						Kind = Flag(this.Evaluator, "prune") && unchanged ? "Prune" : "Stale",
						Output = path,
						Hash = item.Hash,
						Manifest = previousPath,
						Reason = unchanged ? "Previously deployed, no longer selected" : "Modified; preserved",
					});
				}
			}
			else if(Flag(this.Evaluator, "prune"))
				throw new ArgumentException(string.Format(Properties.Resources.Review_InvalidOption, "prune", "previous required"));
		}

		static bool Matches(DeploymentPlan previous, DeploymentPlan current) =>
			previous.Succeeded &&
			Shape(previous).SequenceEqual(Shape(current), StringComparer.Ordinal) &&
			previous.Manifests.OrderBy(item => item.Key).SequenceEqual(current.Manifests.OrderBy(item => item.Key)) &&
			Packages(previous).SequenceEqual(Packages(current));
		static IEnumerable<string> Packages(DeploymentPlan plan) => plan.Packages.Select(package => $"{package.Id}@{package.Version}:{package.Framework}:{package.Hash}");
		static IEnumerable<string> Shape(DeploymentPlan plan) => plan.Operations.Where(operation => operation.Kind is "Copy" or "Render" or "Delete").Select(operation => $"{operation.Kind}|{Path.GetRelativePath(plan.Root, operation.Output)}|{operation.Package}|{operation.SourceHash}|{operation.ContentHash}|{operation.ResolvedSource}");
	}
	#endregion

	#region 输出验证
	private void ValidateOutput(string path, bool report = false)
	{
		if(report && this.Evaluator.TryGetOption("lockFile", out var lockFile) && DeploymentPath.Comparer.Equals(path, this.GetPath(lockFile)))
			throw new IOException(string.Format(Properties.Resources.Review_Conflict, path));

		if(this.Plan.Manifests.ContainsKey(path) || this.Plan.Operations.Any(Conflicts))
			throw new IOException(string.Format(Properties.Resources.Review_Conflict, path));

		DeploymentPath.Validate(Path.GetDirectoryName(path), path);

		bool Conflicts(DeploymentPlan.Operation operation)
		{
			return DeploymentPath.Comparer.Equals(path, operation.Source) ||
				DeploymentPath.Comparer.Equals(path, operation.ResolvedSource) ||
				DeploymentPath.Comparer.Equals(path, operation.Output);
		}
	}
	#endregion

	#region 计划执行
	private Task ExecutePlanAsync(CancellationToken cancellation)
	{
		var dryRun = Flag(this.Evaluator, "dry-run");

		foreach(var operation in this.Plan.Operations)
		{
			cancellation.ThrowIfCancellationRequested();

			try
			{
				this.Session.Validate(operation.Output);

				if(operation.Status == "Duplicate")
				{
					if(!dryRun)
					{
						this.Session.Counter.Skip();
						operation.Hash = DeploymentSession.Hash(operation.Output);
					}
				}
				else if(dryRun || operation.Kind == "Stale")
					operation.Status = operation.Kind == "Stale" ? "Preserved" : "Planned";
				else if(operation.Kind is "Delete" or "Prune")
				{
					Delete(operation);
				}
				else
				{
					Write(operation);
				}

				if(Flag(this.Evaluator, "explain") || this.IsVerbosity(Verbosity.Detail))
					this.Write(Utility.FormatOutput(Properties.Resources.Review_Planned, null, operation.Status, operation.Package ?? operation.Source, operation.Output));
			}
			catch(OperationCanceledException)
			{
				throw;
			}
			catch(Exception exception)
			{
				operation.Status = "Failed";
				operation.Reason = exception.Message;
				this.Session.Counter.Fail();
				this.Error(exception.Message);
				break;
			}
		}

		this.Plan.Succeeded = this.Session.Counter.Failures == 0;

		if(!dryRun && this.Plan.Succeeded && !Flag(this.Evaluator, "locked") && this.Evaluator.TryGetOption("lockFile", out var path))
			this.Plan.Save(this.GetPath(path));

		return Task.CompletedTask;

		void Delete(DeploymentPlan.Operation operation)
		{
			if(operation.Kind == "Prune" && (!File.Exists(operation.Output) || DeploymentSession.Hash(operation.Output) != operation.Hash))
				throw new IOException(string.Format(Properties.Resources.Review_Conflict, operation.Output));

			if(File.Exists(operation.Output))
			{
				File.Delete(operation.Output);
				this.Session.Counter.Delete();
				operation.Status = "Deleted";
			}
			else if(Directory.Exists(operation.Output))
				throw new IOException(string.Format(Properties.Resources.Review_Conflict, operation.Output));
			else
			{
				this.Session.Counter.Skip();
				operation.Status = "Skipped";
			}
		}

		void Write(DeploymentPlan.Operation operation)
		{
			var source = operation.ResolvedSource ?? operation.Source;
			if(!DeploymentPath.Comparer.Equals(DeploymentPath.Identity(operation.Source), Path.GetFullPath(source)))
				throw new IOException(string.Format(Properties.Resources.Review_Conflict, operation.Source));

			if(DeploymentSession.Hash(source) != operation.SourceHash)
				throw new IOException(string.Format(Properties.Resources.Review_Conflict, operation.Source));

			if(operation.Kind == "Render")
			{
				Render(operation);
			}
			else if(DeploymentUtility.CopyFile(source, operation.Output, this.Overwrite))
			{
				this.Session.Counter.Success();
				operation.Status = "Copied";
			}
			else
			{
				this.Session.Counter.Skip();
				operation.Status = "Skipped";
			}

			operation.Hash = DeploymentSession.Hash(operation.Output);
		}

		void Render(DeploymentPlan.Operation operation)
		{
			var exists = File.Exists(operation.Output);
			if(exists && this.Overwrite == Overwrite.Never)
			{
				this.Session.Counter.Skip();
				operation.Status = "Skipped";
			}
			else if(exists && this.Overwrite != Overwrite.Alway && DeploymentSession.Hash(operation.Output) == operation.ContentHash)
			{
				this.Session.Counter.Skip();
				operation.Status = "Unchanged";
			}
			else
			{
				ArtifactPublisher.Write(operation.Output, stream => stream.Write(operation.Content));
				this.Session.Counter.Success();
				operation.Status = "Rendered";
			}
		}
	}
	#endregion
}
