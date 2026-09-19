/*
 * Authors:
 *   钟峰(Popeye Zhong) <zongsoft@gmail.com>
 *
 * The MIT License (MIT)
 * 
 * Copyright (C) 2015 Zongsoft Corporation <http://www.zongsoft.com>
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
using System.Windows.Forms;
using System.Text.RegularExpressions;

namespace Zongsoft.Regular;

public partial class MainForm : Form
{
	#region 私有变量
	private RegexOptions _regexOptions;
	private int _position;
	private string _fileName;
	#endregion

	#region 构造函数
	public MainForm()
	{
		this.InitializeComponent();

		_regexOptions = RegexOptions.IgnoreCase | RegexOptions.IgnorePatternWhitespace | RegexOptions.ExplicitCapture;
	}
	#endregion

	#region 事件处理
	private void FMain_Load(object sender, EventArgs e)
	{
		chkOptionsIgnoreCase.DataBindings.Add(this.GetRegexOptionsBinding());
		chkOptionsIgnorePatternWhitespace.DataBindings.Add(this.GetRegexOptionsBinding());
		chkOptionsMultiline.DataBindings.Add(this.GetRegexOptionsBinding());
		chkOptionsSingleline.DataBindings.Add(this.GetRegexOptionsBinding());
		chkOptionsExplicitCapture.DataBindings.Add(this.GetRegexOptionsBinding());
	}

	private void FMain_KeyUp(object sender, KeyEventArgs e)
	{
		if(e.KeyCode == Keys.F5)
			btnMatch.PerformClick();
	}

	private void MatchButtonClick(object sender, EventArgs e)
	{
		var pattern = txtPattern.SelectionLength > 0 ? txtPattern.SelectedText : txtPattern.Text;
		var input = txtInput.SelectionLength > 0 ? txtInput.SelectedText : txtInput.Text;

		if(string.IsNullOrEmpty(pattern))
		{
			MessageBox.Show(Properties.Resources.PatternRequired_Message, Properties.Resources.Information_Title, MessageBoxButtons.OK, MessageBoxIcon.Information);
			txtPattern.Focus();
			return;
		}

		if(string.IsNullOrEmpty(input))
		{
			MessageBox.Show(Properties.Resources.InputRequired_Message, Properties.Resources.Information_Title, MessageBoxButtons.OK, MessageBoxIcon.Information);
			txtInput.Focus();
			return;
		}

		_position = txtInput.SelectionLength > 0 ? txtInput.SelectionStart : 0;

		try
		{
			this.Cursor = Cursors.WaitCursor;

			tvwResult.Nodes.Clear();
			tvwResult.BeginUpdate();

			var regex = this.GenerateRegex(pattern);
			var match = regex.Match(input);

			while(match.Success)
			{
				if(match.Value.Length > 0)
				{
					var matchNode = tvwResult.Nodes.Add(match.Value);
					matchNode.Tag = match;

					for(int i = 1; i < match.Groups.Count; i++)
					{
						var groupNode = matchNode.Nodes.Add(string.Format("{0}:{1}", regex.GroupNameFromNumber(i), match.Groups[i].Value));
						groupNode.Tag = match.Groups[i];

						if(match.Groups[i].Captures.Count > 1)
						{
							for(int j = 0; j < match.Groups[i].Captures.Count; j++)
							{
								var node = groupNode.Nodes.Add(match.Groups[i].Captures[j].Value);
								node.Tag = match.Groups[i].Captures[j];
							}
						}
					}
				}

				match = match.NextMatch();
			}

			if(tvwResult.Nodes.Count > 0)
			{
				if(tvwResult.Nodes.Count == 1)
					tvwResult.Nodes[0].Expand();

				tvwResult.Focus();
			}
		}
		catch(Exception ex)
		{
			MessageBox.Show(string.Format(Properties.Resources.Error_Message, Environment.NewLine, ex.Message),
							ex.GetType().FullName,
							MessageBoxButtons.OK, MessageBoxIcon.Error);

			txtPattern.Focus();
		}
		finally
		{
			this.Cursor = Cursors.Default;
			tvwResult.EndUpdate();
		}
	}

	private void ResultAfterSelect(object sender, TreeViewEventArgs e)
	{
		lblCaptureIndex.Text = string.Empty;
		lblCaptureLength.Text = string.Empty;
		lblCaptureValue.Text = string.Empty;

		if(e.Node != null)
		{
			if(e.Node.Tag is Capture capture)
			{
				lblCaptureIndex.Text = (_position + capture.Index).ToString();
				lblCaptureLength.Text = capture.Length.ToString();
				lblCaptureValue.Text = capture.Value;

				txtInput.Select(_position + capture.Index, capture.Length);
			}
		}
	}

	private void NewFileClick(object sender, EventArgs e)
	{
		if(txtPattern.TextLength > 0)
		{
			if(MessageBox.Show(string.Format(Properties.Resources.NewFile_Confirmation, Environment.NewLine), Properties.Resources.NewFile_Title, MessageBoxButtons.YesNo, MessageBoxIcon.Question, MessageBoxDefaultButton.Button2) != System.Windows.Forms.DialogResult.Yes)
				return;
		}

		_fileName = null;
		txtPattern.Clear();
	}

	private void OpenFileClick(object sender, EventArgs e)
	{
		using(var dialog = new OpenFileDialog())
		{
			dialog.CheckFileExists = true;
			dialog.CheckPathExists = true;
			dialog.DefaultExt = ".txt";
			dialog.Filter = Properties.Resources.TextFile_Filter;

			if(dialog.ShowDialog(this) == System.Windows.Forms.DialogResult.OK)
			{
				txtPattern.Text = File.ReadAllText(dialog.FileName);
				_fileName = dialog.FileName;
			}
		}
	}

	private void SaveFileClick(object sender, EventArgs e)
	{
		if(string.IsNullOrEmpty(_fileName))
			mnuFileSaveAs.PerformClick();
		else
			File.WriteAllText(_fileName, txtPattern.Text);
	}

	private void SaveFileAsClick(object sender, EventArgs e)
	{
		using(var dialog = new SaveFileDialog())
		{
			dialog.AddExtension = true;
			dialog.CreatePrompt = true;
			dialog.CheckPathExists = true;
			dialog.DefaultExt = ".txt";
			dialog.Filter = Properties.Resources.TextFile_Filter;
			dialog.FileName = Path.GetFileName(_fileName);

			if(dialog.ShowDialog(this) == DialogResult.OK)
			{
				File.WriteAllText(dialog.FileName, txtPattern.Text);
				_fileName = dialog.FileName;
			}
		}
	}

	private void ExitClick(object sender, EventArgs e)
	{
		this.Close();
	}

	private void UndoClick(object sender, EventArgs e)
	{
		if(this.GetFocusedControl() is TextBoxBase editor && editor.CanUndo)
			editor.Undo();
	}

	private void CutClick(object sender, EventArgs e)
	{
		if(this.GetFocusedControl() is TextBoxBase editor && editor.SelectionLength > 0)
			editor.Cut();
	}

	private void CopyClick(object sender, EventArgs e)
	{
		if(this.GetFocusedControl() is TextBoxBase editor && editor.SelectionLength > 0)
			editor.Copy();
	}

	private void PasteClick(object sender, EventArgs e)
	{
		if(this.GetFocusedControl() is TextBoxBase editor)
			editor.Paste();
	}

	private void SelectAllClick(object sender, EventArgs e)
	{
		if(this.GetFocusedControl() is TextBoxBase editor)
			editor.SelectAll();
	}

	private void AboutClick(object sender, EventArgs e)
	{
		using(var dialog = new AboutDialog())
		{
			dialog.ShowDialog(this);
		}
	}

	private void OptionsClick(object sender, EventArgs e)
	{
		MessageBox.Show(string.Format(Properties.Resources.Options_Message, Environment.NewLine, _regexOptions),
			Properties.Resources.Options_Title,
			MessageBoxButtons.OK, MessageBoxIcon.Warning);
	}
	#endregion

	#region 选项绑定
	private Binding GetRegexOptionsBinding()
	{
		var binding = new Binding("Checked", _regexOptions, "", true, DataSourceUpdateMode.OnPropertyChanged);
		binding.Format += new ConvertEventHandler(this.RegexOptionsBinding_Format);
		binding.Parse += new ConvertEventHandler(this.RegexOptionsBinding_Parse);
		return binding;
	}

	private void RegexOptionsBinding_Format(object sender, ConvertEventArgs e)
	{
		if(e.DesiredType == typeof(bool))
		{
			var binding = (Binding)sender;

			if(binding.Control == chkOptionsIgnoreCase)
				e.Value = (((RegexOptions)e.Value) & RegexOptions.IgnoreCase) == RegexOptions.IgnoreCase;
			else if(binding.Control == chkOptionsIgnorePatternWhitespace)
				e.Value = (((RegexOptions)e.Value) & RegexOptions.IgnorePatternWhitespace) == RegexOptions.IgnorePatternWhitespace;
			else if(binding.Control == chkOptionsMultiline)
				e.Value = (((RegexOptions)e.Value) & RegexOptions.Multiline) == RegexOptions.Multiline;
			else if(binding.Control == chkOptionsSingleline)
				e.Value = (((RegexOptions)e.Value) & RegexOptions.Singleline) == RegexOptions.Singleline;
			else if(binding.Control == chkOptionsExplicitCapture)
				e.Value = (((RegexOptions)e.Value) & RegexOptions.ExplicitCapture) == RegexOptions.ExplicitCapture;
		}
	}

	private void RegexOptionsBinding_Parse(object sender, ConvertEventArgs e)
	{
		if(e.DesiredType == typeof(RegexOptions))
		{
			var binding = (Binding)sender;

			if(binding.Control == chkOptionsIgnoreCase)
				e.Value = (bool)e.Value ? (_regexOptions | RegexOptions.IgnoreCase) : (_regexOptions & (~RegexOptions.IgnoreCase));
			else if(binding.Control == chkOptionsIgnorePatternWhitespace)
				e.Value = (bool)e.Value ? (_regexOptions | RegexOptions.IgnorePatternWhitespace) : (_regexOptions & (~RegexOptions.IgnorePatternWhitespace));
			else if(binding.Control == chkOptionsMultiline)
				e.Value = (bool)e.Value ? (_regexOptions | RegexOptions.Multiline) : (_regexOptions & (~RegexOptions.Multiline));
			else if(binding.Control == chkOptionsSingleline)
				e.Value = (bool)e.Value ? (_regexOptions | RegexOptions.Singleline) : (_regexOptions & (~RegexOptions.Singleline));
			else if(binding.Control == chkOptionsExplicitCapture)
				e.Value = (bool)e.Value ? (_regexOptions | RegexOptions.ExplicitCapture) : (_regexOptions & (~RegexOptions.ExplicitCapture));

			_regexOptions = (RegexOptions)e.Value;
		}
	}
	#endregion

	#region 私有方法
	private Regex GenerateRegex(string pattern)
	{
		return new Regex(pattern, _regexOptions);
	}

	private Control GetFocusedControl()
	{
		var control = this.ActiveControl;

		while(control is ContainerControl container)
		{
			control = container.ActiveControl;
		}

		return control;
	}
	#endregion
}
