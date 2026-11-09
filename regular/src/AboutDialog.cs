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
using System.Reflection;
using System.Windows.Forms;

namespace Zongsoft.Regular;

partial class AboutDialog : Form
{
	#region 构造函数
	public AboutDialog()
	{
		this.InitializeComponent();

		lblProductName.Text = this.AssemblyProduct;
		lblVersion.Text = string.Format("{0}", this.AssemblyVersion);
		lblCompanyName.Text = this.AssemblyCompany;
		txtCopyright.Text = this.AssemblyCopyright;
		txtDescription.Text = this.AssemblyDescription;
	}
	#endregion

	#region 程序集特性访问器
	public string AssemblyTitle
	{
		get
		{
			var attributes = Assembly.GetExecutingAssembly().GetCustomAttributes(typeof(AssemblyTitleAttribute), false);
			if(attributes.Length > 0)
			{
				AssemblyTitleAttribute titleAttribute = (AssemblyTitleAttribute)attributes[0];
				if(!string.IsNullOrEmpty(titleAttribute.Title))
					return titleAttribute.Title;
			}

			return System.IO.Path.GetFileNameWithoutExtension(Assembly.GetExecutingAssembly().Location);
		}
	}

	public string AssemblyVersion => Assembly.GetExecutingAssembly().GetName().Version.ToString();

	public string AssemblyDescription
	{
		get
		{
			var attributes = Assembly.GetExecutingAssembly().GetCustomAttributes(typeof(AssemblyDescriptionAttribute), false);
			if(attributes.Length == 0)
				return string.Empty;

			return ((AssemblyDescriptionAttribute)attributes[0]).Description;
		}
	}

	public string AssemblyProduct
	{
		get
		{
			var attributes = Assembly.GetExecutingAssembly().GetCustomAttributes(typeof(AssemblyProductAttribute), false);
			if(attributes.Length == 0)
				return string.Empty;

			return ((AssemblyProductAttribute)attributes[0]).Product;
		}
	}

	public string AssemblyCopyright
	{
		get
		{
			var attributes = Assembly.GetExecutingAssembly().GetCustomAttributes(typeof(AssemblyCopyrightAttribute), false);
			if(attributes.Length == 0)
				return string.Empty;

			return ((AssemblyCopyrightAttribute)attributes[0]).Copyright;
		}
	}

	public string AssemblyCompany
	{
		get
		{
			var attributes = Assembly.GetExecutingAssembly().GetCustomAttributes(typeof(AssemblyCompanyAttribute), false);
			if(attributes.Length == 0)
				return string.Empty;

			return ((AssemblyCompanyAttribute)attributes[0]).Company;
		}
	}
	#endregion
}
