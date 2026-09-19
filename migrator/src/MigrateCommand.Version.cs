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

using Zongsoft.Services;

namespace Zongsoft.Tools.Migrator;

public sealed partial class MigrateCommand
{
	#region 嵌套子类
	private static class VersionSource
	{
		#region 公共方法
		public static (Version Version, string Edition) Load(string value, Variables variables)
		{
			var defaultSource = string.IsNullOrWhiteSpace(value);
			var result = Normalizer.Normalize(defaultSource ? ".version" : value, variables);
			if(!result.Succeed)
				throw new InvalidOperationException(string.Format(Properties.Resources.VariableResolutionFailed_Message, result.Value));

			if(Version.TryParse(result.Value, out var version))
			{
				if(version.IsZero())
					throw new InvalidOperationException(Properties.Resources.MigrateVersionInvalid_Message);

				variables[Variables.VERSION] = version.ToString();
				return (version, NormalizeEdition(variables.Edition));
			}

			var path = Path.GetFullPath(Path.Combine(Environment.CurrentDirectory, result.Value));
			if(!defaultSource && Directory.Exists(path))
				path = Path.Combine(path, ".version");

			ApplicationVersion application;
			try
			{
				using var stream = File.OpenRead(path);
				application = ApplicationVersion.Load(stream);
			}
			catch(Exception exception) when(exception is IOException or UnauthorizedAccessException or FormatException)
			{
				throw new InvalidDataException(string.Format(Properties.Resources.MigrateVersionLoadFailed_Message, path), exception);
			}

			var edition = NormalizeEdition(variables.Edition);
			if(edition == null)
			{
				if(application.Editions.Count > 1)
					throw new InvalidOperationException(string.Format(Properties.Resources.MigrateVersionEditionRequired_Message, path));

				if(application.Editions.Count == 1)
					edition = application.Editions[0].Name;
			}

			if(edition == null)
				version = application.Version;
			else
			{
				if(!application.Editions.TryGetValue(edition, out var selected))
					throw new InvalidOperationException(string.Format(Properties.Resources.MigrateVersionEditionMissing_Message, path, edition));

				edition = selected.Name;
				version = selected.Version;
			}

			if(version.IsZero())
				throw new InvalidOperationException(string.Format(Properties.Resources.MigrateVersionFileInvalid_Message, path));

			return (version, edition);
		}
		#endregion

		#region 私有方法
		private static string NormalizeEdition(string edition) => string.IsNullOrWhiteSpace(edition) ? null : edition.Trim();
		#endregion
	}
	#endregion
}
