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
using System.Text.Json;
using System.Security.Cryptography;
using System.Text.Json.Serialization;

namespace Zongsoft.Tools.Migrator.Migration;

partial class MigrationPlan
{
	#region 静态字段
	private static readonly Serialization _serialization = new(new JsonSerializerOptions(Serialization.Default.Options) { WriteIndented = true });
	#endregion

	#region 公共方法
	public string Serialize() => JsonSerializer.Serialize(this, _serialization.MigrationPlan);
	public string Fingerprint() => Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(this, Serialization.Default.MigrationPlan)));
	public static MigrationPlan Load(string path)
	{
		var plan = JsonSerializer.Deserialize(File.ReadAllText(path), _serialization.MigrationPlan) ?? throw new InvalidDataException(MigrationResources.PlanEmpty_Message);
		plan.Validate();
		return plan;
	}
	#endregion

	#region 嵌套类型
	[JsonSourceGenerationOptions(PropertyNameCaseInsensitive = true, DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
	[JsonSerializable(typeof(MigrationPlan))]
	private partial class Serialization : JsonSerializerContext;
	#endregion
}
