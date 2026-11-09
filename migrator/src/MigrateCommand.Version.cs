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

using Zongsoft.Services;

namespace Zongsoft.Tools.Migrator;

public sealed partial class MigrateCommand
{
	#region 嵌套子类
	private static class VersionSource
	{
		#region 公共方法
		public static (Version Version, string Edition) Load(string value, MigrationOptions options)
		{
			var text = options.Evaluator.Evaluate(string.IsNullOrWhiteSpace(value) ? "." : value);

			if(Version.TryParse(text, out var version))
			{
				if(version.IsZero())
					throw new InvalidOperationException(Properties.Resources.MigrateVersionInvalid_Message);

				options[MigrationOptions.VERSION] = version.ToString();
				return (version, NormalizeEdition(options.Edition));
			}

			var path = Path.GetFullPath(Path.Combine(Environment.CurrentDirectory, text));
			var directory = Directory.Exists(path);
			if(directory)
				path = Path.Combine(path, ".edition");

			ApplicationManifest application = null;
			ApplicationIdentifier identifier = default;

			try
			{
				Stream stream;

				try { stream = File.OpenRead(path); }
				catch(FileNotFoundException) when(directory)
				{
					path = Path.Combine(Path.GetDirectoryName(path), ".version");
					stream = File.OpenRead(path);
				}

				using(stream)
				{
					if(path.EndsWith(".version", StringComparison.OrdinalIgnoreCase))
						identifier = ApplicationIdentifier.Load(stream);
					else
						application = ApplicationManifest.Load(stream);
				}
			}
			catch(Exception exception) when(exception is IOException or UnauthorizedAccessException or FormatException)
			{
				throw new InvalidDataException(string.Format(Properties.Resources.MigrateVersionLoadFailed_Message, path), exception);
			}

			var edition = NormalizeEdition(options.Edition);
			if(application == null)
				version = identifier.Version;
			else
			{
				// 文件的默认发行版仅决定版本号，不能成为未显式指定的产物 Edition。
				var selection = edition ?? application.Editions.Current.Name;
				if(selection == null && application.Editions.Count > 1)
					throw new InvalidOperationException(string.Format(Properties.Resources.MigrateVersionEditionRequired_Message, path));

				if(selection == null && application.Editions.Count == 1)
					selection = application.Editions[0].Name;

				if(selection == null)
					version = application.Version;
				else
				{
					if(!application.Editions.TryGetValue(selection, out var selected))
						throw new InvalidOperationException(string.Format(Properties.Resources.MigrateVersionEditionMissing_Message, path, selection));

					if(edition != null)
						edition = selected.Name;

					version = selected.Version;
				}
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
