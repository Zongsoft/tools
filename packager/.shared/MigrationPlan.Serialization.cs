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
 * Copyright (C) 2020-2026 Zongsoft Corporation <http://www.zongsoft.com>
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
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Security.Cryptography;

namespace Zongsoft.Tools.Packager.Migration;

partial class MigrationPlan
{
	#region 静态字段
	private static readonly Serialization _serialization = new(new JsonSerializerOptions { WriteIndented = true, PropertyNameCaseInsensitive = true });
	#endregion

	#region 公共方法
	public string Serialize() => JsonSerializer.Serialize(this, _serialization.MigrationPlan);

	public string Fingerprint() => Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(this, Serialization.Default.MigrationPlan)));

	public static MigrationPlan Load(string path)
	{
		var plan = JsonSerializer.Deserialize(File.ReadAllText(path), _serialization.MigrationPlan) ?? throw new InvalidDataException(MigrationResources.PlanEmpty);
		plan.Validate();
		return plan;
	}
	#endregion

	#region 嵌套类型
	[JsonSourceGenerationOptions(PropertyNameCaseInsensitive = true)]
	[JsonSerializable(typeof(MigrationPlan))]
	private partial class Serialization : JsonSerializerContext;
	#endregion
}
