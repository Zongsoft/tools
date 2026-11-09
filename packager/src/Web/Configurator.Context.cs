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
using System.Collections.Generic;
using System.Runtime.InteropServices;

using Zongsoft.Text.Templating;

namespace Zongsoft.Tools.Packager.Web;

partial class Configurator
{
	internal sealed class Context
	{
		internal Context(string packageName, string installPath, TemplateEvaluator evaluator, BindingDocument bindings = null, Architecture architecture = Architecture.X64)
		{
			ArgumentException.ThrowIfNullOrEmpty(packageName);
			ArgumentException.ThrowIfNullOrEmpty(installPath);

			this.PackageName = packageName;
			this.InstallPath = installPath;
			this.Bindings = bindings ?? new();
			this.MaximumInteger = architecture is Architecture.X86 or Architecture.Arm ? int.MaxValue : long.MaxValue;
			this.Evaluator = evaluator ?? Utility.CreateEvaluator();
		}

		internal string PackageName { get; }
		internal string InstallPath { get; }
		internal long MaximumInteger { get; }
		internal BindingDocument Bindings { get; }
		internal string SystemTrust { get; init; } = "/etc/ssl/certs/ca-certificates.crt";
		internal TemplateEvaluator Evaluator { get; }

		internal string Expand(string value, Diagnostic.Location source)
		{
			if(!this.Evaluator.TryEvaluate(value, out var text, out var error))
				throw DefinitionException.Create("Variable", source, error.Expression, error);
			return text;
		}
	}
}
