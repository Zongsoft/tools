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
using System.Runtime.InteropServices;

namespace Zongsoft.Tools.Packager;

partial class Package
{
	public sealed class Tar : Package
	{
		internal const string EXTENSION = ".tar.gz";

		/// <summary>根据应用身份、目标平台及制作选项初始化tar.gz 安装包。</summary>
		/// <param name="name">应用名称，不能为空。</param>
		/// <param name="edition">应用发行版；为空时不附加发行版标识。</param>
		/// <param name="version">非零的应用版本。</param>
		/// <param name="platform">目标操作系统平台。</param>
		/// <param name="architecture">目标处理器架构。</param>
		/// <param name="options">本次制作的类型化选项；为空时使用独立的默认选项。</param>
		public Tar(string name, string edition, Version version, Platform platform, Architecture architecture, PackageOptions options = null) : base(name, edition, version, platform, architecture, options)
		{
			this.Scriptor = new Scriptor.Systemd(this);
			this.InstallPath = Utility.Unix.GetInstallPath(this.PackageIdentity);
		}

		internal override string FileName => this.GetFileName(EXTENSION);
		internal override string EntryPrefix => null;

		public override void Pack(string output, bool overwrite) => this.Tar(output, overwrite);
	}
}
