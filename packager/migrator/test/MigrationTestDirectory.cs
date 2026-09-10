using System;
using System.IO;
using System.Text;
using System.Security.Cryptography;

namespace Zongsoft.Tools.Packager.Migration.Tests;

internal sealed class MigrationTestDirectory : IDisposable
{
	#region 构造函数
	public MigrationTestDirectory()
	{
		this.Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "ZongsoftMigrationTests", Guid.NewGuid().ToString("N"));
		Directory.CreateDirectory(this.Path);
	}
	#endregion

	#region 属性定义
	public string Path { get; }
	#endregion

	#region 辅助方法
	public string Write(string relative, string content)
	{
		var path = System.IO.Path.GetFullPath(System.IO.Path.Combine(this.Path, relative));
		if(!path.StartsWith(this.Path + System.IO.Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("Fixture path escapes its owned directory.");
		Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path));
		File.WriteAllText(path, content.Replace("\r\n", "\n").Replace("\n", "\r\n"), new UTF8Encoding(false));
		return path;
	}

	public MigrationPlan.Script Script(string relative, string sql)
	{
		var source = this.Write(relative, sql);
		return new() { Path = relative, Checksum = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(source))) };
	}

	public void Dispose()
	{
		if(Directory.Exists(this.Path)) Directory.Delete(this.Path, true);
	}
	#endregion
}
