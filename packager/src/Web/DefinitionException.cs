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

namespace Zongsoft.Tools.Packager.Web;

internal sealed class DefinitionException(Diagnostic diagnostic, Exception innerException = null) : Exception(diagnostic + (innerException == null ? string.Empty : Environment.NewLine + innerException.Message), innerException)
{
	internal Diagnostic Diagnostic { get; } = diagnostic;

	internal static DefinitionException Create(string code, Diagnostic.Location source, object value = null, Exception innerException = null) =>
		new(new(code, string.Format(GetMessage(code), value), source), innerException);

	private static string GetMessage(string code) => code switch
	{
		"Field" => Properties.Resources.Web_Field_Message,
		"Scope" => Properties.Resources.Web_Scope_Message,
		"MixedServers" => Properties.Resources.Web_MixedServers_Message,
		"Option" => Properties.Resources.Web_Option_Message,
		"Hoster" => Properties.Resources.Web_Hoster_Message,
		"Variable" => Properties.Resources.Web_Variable_Message,
		"Duplicate" => Properties.Resources.Web_Duplicate_Message,
		"Required" => Properties.Resources.Web_Required_Message,
		"Directive" => Properties.Resources.Web_Directive_Message,
		"Conflict" => Properties.Resources.Web_Conflict_Message,
		"Load" => Properties.Resources.Web_Load_Message,
		"Capability" => Properties.Resources.Web_Capability_Message,
		_ => Properties.Resources.Web_Value_Message,
	};
}
