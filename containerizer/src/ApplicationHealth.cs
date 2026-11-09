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
using System.Text.Json;

using Zongsoft.Tools.Containerizer.Protocol;

namespace Zongsoft.Tools.Containerizer;

internal static class ApplicationHealth
{
	internal static ContainerizationException CreateException(string name, string field) => new(2,
		string.Format(Properties.Resources.ApplicationHealth_Invalid_Message, name, field));

	internal static void Configure(ServiceBuildContext source)
	{
		var text = source.Component["health"];
		if(text == null)
		{
			source.Plan.Health.Test = ["CMD-SHELL", "kill -0 1"];
			return;
		}

		try
		{
			var command = JsonSerializer.Deserialize(text, Protocol.ProtocolJson.Default.StringArray);
			if(command == null || command.Length < 2 || command[0] is not ("CMD" or "CMD-SHELL") ||
				Array.Exists(command, value => value == null) || command[0] == "CMD-SHELL" && command.Length != 2 || string.IsNullOrWhiteSpace(command[1]))
				throw CreateException(source.Plan.Id, "health");

			source.Plan.Health.Test = command;
		}
		catch(JsonException)
		{
			throw CreateException(source.Plan.Id, "health");
		}
	}
}
