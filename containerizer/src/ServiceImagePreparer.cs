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

using Zongsoft.Components;

using Zongsoft.Tools.Containerizer.Protocol;

namespace Zongsoft.Tools.Containerizer;

internal sealed class ServiceImagePreparer(ContainerEngine engine, Action<CommandOutletContent> error = null)
{
	public async Task PrepareAsync(ServiceBuildContext context, string workspace, CancellationToken cancellation)
	{
		using var json = JsonDocument.Parse(await engine.InspectAsync(context.Plan.Image.Id, cancellation));
		var config = json.RootElement[0].GetProperty("Config");

		if(context.Plan.Health.Test == null && !HasHealthCheck(config))
			throw new ContainerizationException(2, Properties.Resources.Engine_9_Message);

		if(context.Plan.Health.Test?.Contains("redis-cli", StringComparer.Ordinal) == true || context.Plan.Health.Test?.Contains("valkey-cli", StringComparer.Ordinal) == true)
		{
			var command = context.Plan.Command ?? [];
			var password = Array.IndexOf(command, "--requirepass");

			if(password >= 0 && password + 1 < command.Length)
				context.Environment["REDISCLI_AUTH"] = command[password + 1];
		}

		var user = context.Plan.User ?? GetString(config, "User");
		if(string.IsNullOrEmpty(user) || user is "root" or "0" or "0:0")
			user = null;

		var mounts = context.Plan.Mounts.Where(mount => !mount.ReadOnly && !mount.Temporary && (mount.User != null || user != null)).ToArray();
		if(mounts.Length == 0)
			return;

		var directory = Path.Combine(workspace, "ownership", context.Plan.Id);
		Files.CreatePrivateDirectory(directory);
		await using var resources = new BuildResources(engine, error);
		var container = resources.RegisterContainer();
		await engine.RunAsync(["create", "--name", container, "--entrypoint", "/bin/true", context.Plan.Image.Id], null, cancellation);

		var passwd = Path.Combine(directory, "passwd");
		var groups = Path.Combine(directory, "group");
		var copy = await engine.RunProcessAsync(["cp", $"{container}:/etc/passwd", passwd], null, cancellation);

		var entries = copy.ExitCode == 0 ? File.ReadAllLines(passwd).Select(line => line.Split(':')).Where(fields => fields.Length == 7).ToArray() : [];
		var owners = new Dictionary<string, string>(StringComparer.Ordinal);

		foreach(var owner in mounts.Select(mount => mount.User ?? user).Distinct(StringComparer.Ordinal))
		{
			var parts = owner.Split(':', 2);
			var account = entries.FirstOrDefault(fields => fields[0] == parts[0] || fields[2] == parts[0]);
			var uid = account?[2] ?? parts[0];
			var gid = parts.Length == 2 ? parts[1] : account?[3] ?? "0";

			if(!uint.TryParse(gid, out _))
			{
				if(!File.Exists(groups))
					await engine.RunAsync(["cp", $"{container}:/etc/group", groups], null, cancellation);
				gid = File.ReadAllLines(groups).Select(line => line.Split(':')).FirstOrDefault(fields => fields.Length >= 3 && fields[0] == gid)?[2];
			}

			if(!uint.TryParse(uid, out _) || !uint.TryParse(gid, out _))
				throw new ContainerizationException(2, Properties.Resources.Engine_10_Message);

			owners.Add(owner, $"{uid}:{gid}");
		}

		foreach(var mount in mounts)
			mount.User = owners[mount.User ?? user];
	}

	private static bool HasHealthCheck(JsonElement configuration) =>
		configuration.TryGetProperty("Healthcheck", out var health) &&
		health.ValueKind == JsonValueKind.Object &&
		health.TryGetProperty("Test", out var test) &&
		test.GetArrayLength() > 0 && test[0].GetString() != "NONE";

	private static string GetString(JsonElement value, string key) => value.TryGetProperty(key, out var property) && property.ValueKind == JsonValueKind.String ? property.GetString() : null;
}
