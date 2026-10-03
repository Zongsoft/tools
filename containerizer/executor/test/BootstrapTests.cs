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

using Xunit;

using Zongsoft.Tools.Containerizer.Execution;
using Zongsoft.Tools.Containerizer.Protocol;

namespace Zongsoft.Tools.Containerizer.Executor.Tests;

public sealed class BootstrapTests
{
	[Fact]
	public void AptCachePreservesOriginalBytesAndRejectsCollidingPackageNames()
	{
		var root = Path.Combine(Path.GetTempPath(), "containerizer-bootstrap-" + Guid.NewGuid().ToString("N"));
		Directory.CreateDirectory(root);

		try
		{
			var package = Path.Combine(root, "dependency.deb");
			File.WriteAllBytes(package, [0, 1, 2, 255]);

			var cache = DockerHost.PrepareAptCache([package], Path.Combine(root, "cache"));
			Assert.Equal(File.ReadAllBytes(package), File.ReadAllBytes(Path.Combine(cache, "dependency.deb")));

			Directory.CreateDirectory(Path.Combine(root, "other"));
			var collision = Path.Combine(root, "other", "dependency.deb");
			File.WriteAllText(collision, "conflicting content");

			Assert.Equal(4, Assert.Throws<ContainerizationException>(() => DockerHost.PrepareAptCache([package, collision], cache)).Code);
			Assert.Equal([0, 1, 2, 255], File.ReadAllBytes(Path.Combine(cache, "dependency.deb")));
			Assert.Equal([0, 1, 2, 255], File.ReadAllBytes(package));
		}
		finally { Directory.Delete(root, true); }
	}
}
