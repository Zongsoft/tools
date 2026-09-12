using System;
using System.IO;
using System.Linq;
using System.Text;

using Zongsoft.Services;
using Xunit;

namespace Zongsoft.Tools.Packager.Tests;

public sealed class VersionFileTests
{
	#region 加载测试
	[Theory]
	[InlineData(null, null)]
	[InlineData(null, "1.2.0")]
	[InlineData("Zongsoft.Daemon", null)]
	public void Load_MissingFile_RequiresNameAndVersion(string name, string version)
	{
		using var directory = new MigrationTestDirectory();

		Assert.Throws<InvalidOperationException>(() => PackCommand<Package.Tar>.VersionFile.Load(directory.Path, name, null, version == null ? null : Version.Parse(version)));

		Assert.False(File.Exists(Path.Combine(directory.Path, ".version")));
	}

	[Theory]
	[InlineData(null)]
	[InlineData("")]
	[InlineData("Community")]
	public void Load_MissingFile_PreparesSingleOrNamedVersionWithoutWriting(string edition)
	{
		using var directory = new MigrationTestDirectory();
		var file = PackCommand<Package.Tar>.VersionFile.Load(directory.Path, "Zongsoft.Daemon", edition, new Version(1, 2, 0));

		AssertIdentity(file.Identifier, "Zongsoft.Daemon", string.IsNullOrEmpty(edition) ? null : edition, "1.2.0");
		Assert.False(File.Exists(Path.Combine(directory.Path, ".version")));
		file.Save(Path.Combine(directory.Path, "host.tar.gz"));
		var saved = ApplicationVersion.Load(Path.Combine(directory.Path, ".version"));
		Assert.Equal("Zongsoft.Daemon", saved.Name);
		if(string.IsNullOrEmpty(edition))
		{
			Assert.Equal(new Version(1, 2, 0), saved.Version);
			Assert.Empty(saved.Editions);
		}
		else
		{
			Assert.Null(saved.Version);
			var selected = Assert.Single(saved.Editions);
			Assert.Equal("Community", selected.Name);
			Assert.Equal(new Version(1, 2, 0), selected.Version);
		}
	}

	[Fact]
	public void Load_SourceFile_DoesNotSearchParentOrChildren()
	{
		using var directory = new MigrationTestDirectory();
		directory.Write(".version", "Zongsoft.Daemon@1.0.0");
		directory.Write("source/child/.version", "Zongsoft.Hosting.Web@2.0.0");
		var source = Path.Combine(directory.Path, "source");

		Assert.Throws<InvalidOperationException>(() => PackCommand<Package.Tar>.VersionFile.Load(source, null, null, null));

		Assert.False(File.Exists(Path.Combine(source, ".version")));
	}

	[Theory]
	[InlineData(null, null)]
	[InlineData("zongsoft.daemon", null)]
	[InlineData("", null)]
	[InlineData("   ", null)]
	[InlineData("", "2.3.4")]
	[InlineData("   ", "2.3.4")]
	[InlineData(null, "2.3.4")]
	[InlineData("ZONGSOFT.DAEMON", "2.3.4")]
	public void Load_SingleVersion_UsesOmittedValuesAndCanonicalName(string name, string version)
	{
		using var directory = new MigrationTestDirectory();
		var path = directory.Write(".version", "Zongsoft.Daemon@1.2.0\n");
		var original = File.ReadAllBytes(path);

		var file = PackCommand<Package.Tar>.VersionFile.Load(directory.Path, name, null, version == null ? null : Version.Parse(version));

		AssertIdentity(file.Identifier, "Zongsoft.Daemon", null, version ?? "1.2.0");
		Assert.Equal(original, File.ReadAllBytes(path));
	}

	[Theory]
	[InlineData(null)]
	[InlineData("")]
	[InlineData("community")]
	public void Load_OneEdition_AutomaticallySelectsCanonicalEdition(string edition)
	{
		using var directory = new MigrationTestDirectory();
		directory.Write(".version", "Zongsoft.Hosting.Web\n\n[Community]\n1.2.3\n");

		var file = PackCommand<Package.Tar>.VersionFile.Load(directory.Path, null, edition, null);

		AssertIdentity(file.Identifier, "Zongsoft.Hosting.Web", "Community", "1.2.3");
	}

	[Theory]
	[InlineData(null, false)]
	[InlineData("", true)]
	public void Load_MultipleEditions_RequiresExplicitSelection(string edition, bool hasOverrides)
	{
		using var directory = new MigrationTestDirectory();
		var path = directory.Write(".version", Editions());
		var original = File.ReadAllBytes(path);

		var error = Assert.Throws<InvalidOperationException>(() => PackCommand<Package.Tar>.VersionFile.Load(directory.Path, hasOverrides ? "zongsoft.hosting.web" : null, edition, hasOverrides ? new Version(4, 0, 0) : null));

		Assert.Contains(".version", error.Message);
		Assert.Equal(original, File.ReadAllBytes(path));
	}

	[Theory]
	[InlineData("community", null, "Community", "1.0.0")]
	[InlineData("PROFESSIONAL", null, "Professional", "2.1.0")]
	[InlineData("professional", "2.5.0", "Professional", "2.5.0")]
	public void Load_SelectedEdition_PreservesNameAndUsesSelectedVersion(string edition, string version, string expectedEdition, string expectedVersion)
	{
		using var directory = new MigrationTestDirectory();
		directory.Write(".version", Editions());

		var file = PackCommand<Package.Tar>.VersionFile.Load(directory.Path, "ZONGSOFT.HOSTING.WEB", edition, version == null ? null : Version.Parse(version));

		AssertIdentity(file.Identifier, "Zongsoft.Hosting.Web", expectedEdition, expectedVersion);
	}

	[Theory]
	[InlineData("Zongsoft.Other", "Community", true)]
	[InlineData(null, "Missing", true)]
	[InlineData(null, "Community", false)]
	public void Load_NameMismatchOrUnknownEdition_FailsWithoutChangingFile(string name, string edition, bool named)
	{
		using var directory = new MigrationTestDirectory();
		var path = directory.Write(".version", named ? Editions() : "Zongsoft.Hosting.Web@1.0.0\n");
		var original = File.ReadAllBytes(path);

		var error = Assert.Throws<InvalidOperationException>(() => PackCommand<Package.Tar>.VersionFile.Load(directory.Path, name, edition, null));

		Assert.Contains(name ?? edition, error.Message);
		Assert.Equal(original, File.ReadAllBytes(path));
	}

	[Theory]
	[InlineData(false, "", "1.0.0")]
	[InlineData(false, "   ", "1.0.0")]
	[InlineData(false, "Zongsoft.Daemon", "0.0")]
	[InlineData(true, "Zongsoft.Daemon", "0.0.0")]
	[InlineData(false, "Zongsoft.Daemon", null)]
	public void Load_MissingIdentityOrZeroVersion_Fails(bool existing, string name, string version)
	{
		using var directory = new MigrationTestDirectory();
		if(existing) directory.Write(".version", "Zongsoft.Daemon@1.0.0\n");

		Assert.Throws<InvalidOperationException>(() => PackCommand<Package.Tar>.VersionFile.Load(directory.Path, name, null, version == null ? null : Version.Parse(version)));
	}

	[Theory]
	[InlineData("Zongsoft.Daemon@0.0.0\n")]
	[InlineData("Zongsoft.Daemon\n[Community]\n0.0.0\n")]
	public void Load_ZeroVersionFromFile_IsRejected(string content)
	{
		using var directory = new MigrationTestDirectory();
		directory.Write(".version", content);

		Assert.Throws<InvalidOperationException>(() => PackCommand<Package.Tar>.VersionFile.Load(directory.Path, null, null, null));
	}

	[Theory]
	[InlineData("")]
	[InlineData("Zongsoft.Daemon@invalid\n")]
	[InlineData("Zongsoft.Daemon\n[Community]\n1.0.0\n[community]\n2.0.0\n")]
	public void Load_InvalidFile_FailsWithoutOverwriting(string content)
	{
		using var directory = new MigrationTestDirectory();
		var path = directory.Write(".version", content);
		var original = File.ReadAllBytes(path);

		var error = Assert.Throws<InvalidDataException>(() => PackCommand<Package.Tar>.VersionFile.Load(directory.Path, "Zongsoft.Daemon", null, new Version(3, 0, 0)));

		Assert.Contains(path, error.Message);
		Assert.IsType<FormatException>(error.InnerException);
		Assert.Equal(original, File.ReadAllBytes(path));
	}

	[Fact]
	public void Load_VersionPathIsDirectory_IsNotTreatedAsMissing()
	{
		using var directory = new MigrationTestDirectory();
		var path = Path.Combine(directory.Path, ".version");
		Directory.CreateDirectory(path);

		var error = Assert.Throws<InvalidDataException>(() => PackCommand<Package.Tar>.VersionFile.Load(directory.Path, "Zongsoft.Daemon", null, new Version(1, 0, 0)));

		Assert.Contains(path, error.Message);
		Assert.NotNull(error.InnerException);
		Assert.True(Directory.Exists(path));
	}

	[Theory]
	[InlineData("bad@name", null)]
	[InlineData("Zongsoft.Daemon", "bad[edition]")]
	public void Load_NewIdentityWithReservedCharacters_IsRejected(string name, string edition)
	{
		using var directory = new MigrationTestDirectory();

		var error = Assert.Throws<InvalidOperationException>(() => PackCommand<Package.Tar>.VersionFile.Load(directory.Path, name, edition, new Version(1, 0, 0)));

		Assert.IsAssignableFrom<ArgumentException>(error.InnerException);
		Assert.False(File.Exists(Path.Combine(directory.Path, ".version")));
	}
	#endregion

	#region 保存测试
	[Fact]
	public void Save_SelectedEdition_PreservesOtherVersionsAndOrder()
	{
		using var directory = new MigrationTestDirectory();
		var path = directory.Write(".version", Editions());
		var file = PackCommand<Package.Tar>.VersionFile.Load(directory.Path, null, "professional", new Version(2, 5, 0));

		file.Save(Path.Combine(directory.Path, "host.rpm"));

		var saved = ApplicationVersion.Load(path);
		Assert.Null(saved.Version);
		Assert.Equal(new[] { "Community", "Professional", "Enterprise" }, saved.Editions.Select(edition => edition.Name));
		Assert.Equal(new[] { new Version(1, 0, 0), new Version(2, 5, 0), new Version(3, 0, 1) }, saved.Editions.Select(edition => edition.Version));
		Assert.Equal(Encoding.UTF8.GetBytes("Zongsoft.Hosting.Web\r\n\r\n[Community]\r\n1.0.0\r\n\r\n[Professional]\r\n2.5.0\r\n\r\n[Enterprise]\r\n3.0.1\r\n"), File.ReadAllBytes(path));
	}

	[Fact]
	public void Save_SingleVersion_UsesCoreFormat()
	{
		using var directory = new MigrationTestDirectory();
		var path = directory.Write(".version", "; hosting\nZongsoft.Daemon@1.0.0\n");
		var file = PackCommand<Package.Tar>.VersionFile.Load(directory.Path, null, "", new Version(2, 0, 1));

		file.Save(Path.Combine(directory.Path, "host.deb"));

		Assert.Equal(Encoding.UTF8.GetBytes("Zongsoft.Daemon@2.0.1\r\n"), File.ReadAllBytes(path));
	}

	[Fact]
	public void Save_Failure_ReportsPackageAndPreservesArtifact()
	{
		using var directory = new MigrationTestDirectory();
		var file = PackCommand<Package.Tar>.VersionFile.Load(directory.Path, "Zongsoft.Daemon", null, new Version(1, 2, 0));
		var path = Path.Combine(directory.Path, ".version");
		Directory.CreateDirectory(path);
		var package = directory.Write("host.tar.gz", "retained package marker");
		var bytes = File.ReadAllBytes(package);

		var error = Assert.Throws<IOException>(() => file.Save(package));

		Assert.Contains(package, error.Message);
		Assert.Contains(path, error.Message);
		Assert.NotNull(error.InnerException);
		Assert.Equal(bytes, File.ReadAllBytes(package));
		Assert.True(Directory.Exists(path));
	}
	#endregion

	#region 辅助方法
	private static string Editions() => "Zongsoft.Hosting.Web\n\n[Community]\n1.0.0\n\n[Professional]\n2.1.0\n\n[Enterprise]\n3.0.1\n";

	private static void AssertIdentity(ApplicationIdentifier identifier, string name, string edition, string version)
	{
		Assert.Equal(name, identifier.Name);
		Assert.Equal(edition, identifier.Edition);
		Assert.Equal(Version.Parse(version), identifier.Version);
	}
	#endregion
}
