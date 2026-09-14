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
		var destinations = new Dictionary<string, DeploymentOperation>(DeploymentPath.Comparer);

		foreach(var operation in this.Plan.Operations)
		{
			this.Session.Validate(operation.Destination);

			if(operation.Kind == "Delete")
			{
				destinations.Remove(operation.Destination);
				continue;
			}

			var resolved = DeploymentPath.Identity(operation.Source);
			operation.ResolvedSource = DeploymentPath.Comparer.Equals(resolved, Path.GetFullPath(operation.Source)) ? null : resolved;
			operation.SourceHash = DeploymentSession.Hash(operation.ResolvedSource ?? operation.Source);

			if(destinations.TryGetValue(operation.Destination, out var previous) && previous.Package != null && operation.Package != null)
			{
				if(previous.SourceHash != operation.SourceHash)
					throw new IOException(string.Format(Properties.Resources.Review_Conflict, $"{operation.Destination}: {previous.Package} / {operation.Package}"));

				operation.Status = "Duplicate";
				operation.Reason = "Identical package asset already planned for this destination";
				continue;
			}

			destinations[operation.Destination] = operation;
		}

		var packages = this.Session.Packages.Values
			.Concat(this.Session.Requested.Values)
			.DistinctBy(package => package.Identity.ToString())
			.OrderBy(package => package.Identity.Id, StringComparer.OrdinalIgnoreCase)
			.ThenBy(package => package.Identity.Version);

		foreach(var package in packages)
		{
			var selection = new PackageSelection
			{
				Id = package.Identity.Id,
				Version = package.Identity.Version.ToNormalizedString(),
				Framework = Utility.GetTargetFramework(this.Variables),
				Hash = NugetUtility.PackageHash(this.Variables, package),
			};

			if(this.Session.Requested.ContainsKey(package.Identity.ToString()))
				selection.RequiredBy.Add("manifest");

			foreach(var root in this.Session.Roots)
				if(root.Metadata.Identity.Equals(package.Identity))
					selection.RequiredBy.Add("root");

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

		foreach(var key in new[] { "report", "lockFile" })
			if(this.Variables.TryGetValue(key, out var output) && !string.IsNullOrEmpty(output))
			{
				var path = Path.GetFullPath(this.Normalize(output));
				this.ValidateOutput(path, key == "report");
			}

		if(Flag(this.Variables, "locked"))
		{
			if(!this.Variables.TryGetValue("lockFile", out var lockPath) || !File.Exists(this.Normalize(lockPath)))
				throw new InvalidOperationException(string.Format(Properties.Resources.Review_Locked, "lockFile"));

			var previous = DeploymentPlan.Load(this.Normalize(lockPath));

			if(!previous.Succeeded || !Shape(previous).SequenceEqual(Shape(this.Plan), StringComparer.Ordinal)
				|| !previous.Manifests.OrderBy(item => item.Key).SequenceEqual(this.Plan.Manifests.OrderBy(item => item.Key))
				|| !previous.Packages.Select(package => $"{package.Id}@{package.Version}:{package.Framework}:{package.Hash}").SequenceEqual(this.Plan.Packages.Select(package => $"{package.Id}@{package.Version}:{package.Framework}:{package.Hash}")))
				throw new InvalidOperationException(string.Format(Properties.Resources.Review_Locked, lockPath));
		}

		if(this.Variables.TryGetValue("previous", out var previousPath))
		{
			var previous = DeploymentPlan.Load(this.Normalize(previousPath));

			if(!previous.Succeeded || !DeploymentPath.Comparer.Equals(Path.GetFullPath(previous.Root), this.Plan.Root))
				throw new InvalidOperationException(string.Format(Properties.Resources.Review_Locked, "previous root/result"));

			foreach(var group in previous.Operations.Where(operation => operation.Status != "Duplicate").GroupBy(operation => operation.Destination, DeploymentPath.Comparer))
			{
				var item = group.Last();
				var path = this.Session.Validate(item.Destination);

				if(item.Kind != "Copy" || item.Status != "Copied")
					continue;

				if(destinations.ContainsKey(path) || !File.Exists(path))
					continue;

				var unchanged = item.Hash != null && DeploymentSession.Hash(path) == item.Hash;
				this.Plan.Operations.Add(new DeploymentOperation

				{
					Kind = Flag(this.Variables, "prune") && unchanged ? "Prune" : "Stale",
					Destination = path,
					Hash = item.Hash,
					Manifest = previousPath,
					Reason = unchanged ? "Previously deployed, no longer selected" : "Modified; preserved",
				});
			}
		}
		else if(Flag(this.Variables, "prune"))
			throw new ArgumentException(string.Format(Properties.Resources.Review_InvalidOption, "prune", "previous required"));

		static IEnumerable<string> Shape(DeploymentPlan plan) => plan.Operations.Where(operation => operation.Kind is "Copy" or "Delete").Select(operation => $"{operation.Kind}|{Path.GetRelativePath(plan.Root, operation.Destination)}|{operation.Package}|{operation.SourceHash}|{operation.ResolvedSource}");
	}
	#endregion

	#region 输出验证
	private void ValidateOutput(string path, bool report = false)
	{
		if(report && this.Variables.TryGetValue("lockFile", out var lockFile) && DeploymentPath.Comparer.Equals(path, Path.GetFullPath(this.Normalize(lockFile))))
			throw new IOException(string.Format(Properties.Resources.Review_Conflict, path));

		if(this.Plan.Manifests.ContainsKey(path) || this.Plan.Operations.Any(operation => DeploymentPath.Comparer.Equals(path, operation.Source) || DeploymentPath.Comparer.Equals(path, operation.ResolvedSource) || DeploymentPath.Comparer.Equals(path, operation.Destination)))
			throw new IOException(string.Format(Properties.Resources.Review_Conflict, path));

		DeploymentPath.Validate(Path.GetDirectoryName(path), path);
	}
	#endregion

	#region 计划执行
	private Task ExecutePlanAsync(CancellationToken cancellation)
	{
		var dryRun = Flag(this.Variables, "dry-run");

		foreach(var operation in this.Plan.Operations)
		{
			cancellation.ThrowIfCancellationRequested();

			try
			{
				this.Session.Validate(operation.Destination);

				if(operation.Status == "Duplicate")
				{
					if(!dryRun)
					{
						this.Session.Counter.Skip();
						operation.Hash = DeploymentSession.Hash(operation.Destination);
					}
				}
				else if(dryRun || operation.Kind == "Stale")
					operation.Status = operation.Kind == "Stale" ? "Preserved" : "Planned";
				else if(operation.Kind is "Delete" or "Prune")
				{
					if(operation.Kind == "Prune" && (!File.Exists(operation.Destination) || DeploymentSession.Hash(operation.Destination) != operation.Hash))
						throw new IOException(string.Format(Properties.Resources.Review_Conflict, operation.Destination));

					if(File.Exists(operation.Destination))
					{
						File.Delete(operation.Destination);
						this.Session.Counter.Delete();
						operation.Status = "Deleted";
					}
					else if(Directory.Exists(operation.Destination))
						throw new IOException(string.Format(Properties.Resources.Review_Conflict, operation.Destination));
					else
					{
						this.Session.Counter.Skip();
						operation.Status = "Skipped";
					}
				}
				else
				{
					var source = operation.ResolvedSource ?? operation.Source;
					if(!DeploymentPath.Comparer.Equals(DeploymentPath.Identity(operation.Source), Path.GetFullPath(source)))
						throw new IOException(string.Format(Properties.Resources.Review_Conflict, operation.Source));

					if(DeploymentSession.Hash(source) != operation.SourceHash)
						throw new IOException(string.Format(Properties.Resources.Review_Conflict, operation.Source));

					if(DeploymentUtility.CopyFile(source, operation.Destination, this.Overwrite))
					{
						this.Session.Counter.Success();
						operation.Status = "Copied";
					}
					else
					{
						this.Session.Counter.Skip();
						operation.Status = "Skipped";
					}

					operation.Hash = DeploymentSession.Hash(operation.Destination);
				}

				if(Flag(this.Variables, "explain") || this.IsVerbosity(Verbosity.Detail))
					this.Output.WriteLine(string.Format(Properties.Resources.Review_Planned, operation.Status, operation.Package ?? operation.Source, operation.Destination));
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

		if(!dryRun && this.Plan.Succeeded && !Flag(this.Variables, "locked") && this.Variables.TryGetValue("lockFile", out var path))
			this.Plan.Save(Path.GetFullPath(this.Normalize(path)));

		return Task.CompletedTask;
	}
	#endregion
}
