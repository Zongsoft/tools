using System;
using System.IO;
using System.Linq;
using System.Text;

using Xunit;

using Zongsoft.Services;

namespace Zongsoft.Tools.Packager.Tests;

public sealed class VersionFileTest
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

		Assert.False(File.Exists(Path.Combine(directory.Path, ".edition")));
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
		Assert.False(File.Exists(Path.Combine(directory.Path, ".edition")));
		file.Save(Path.Combine(directory.Path, "host.tar.gz"));
		Assert.Equal(file.Identifier, ApplicationIdentifier.Load(Path.Combine(directory.Path, ".version")));
		var saved = ApplicationManifest.Load(Path.Combine(directory.Path, ".edition"));
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
			Assert.Equal(selected, saved.Editions.Current);
			Assert.Equal(new Version(1, 2, 0), selected.Version);
		}
	}

	[Fact]
	public void Load_SourceFile_DoesNotSearchParentOrChildren()
	{
		using var directory = new MigrationTestDirectory();
		directory.Write(".edition", "Zongsoft.Daemon@1.0.0");
		directory.Write("source/child/.version", "Zongsoft.Hosting.Web@2.0.0");
		var source = Path.Combine(directory.Path, "source");

		Assert.Throws<InvalidOperationException>(() => PackCommand<Package.Tar>.VersionFile.Load(source, null, null, null));

		Assert.False(File.Exists(Path.Combine(source, ".edition")));
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
		var path = directory.Write(".edition", "Zongsoft.Daemon@1.2.0\n");
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
		directory.Write(".edition", "Zongsoft.Hosting.Web\n\n[Community]\n1.2.3\n");

		var file = PackCommand<Package.Tar>.VersionFile.Load(directory.Path, null, edition, null);

		AssertIdentity(file.Identifier, "Zongsoft.Hosting.Web", "Community", "1.2.3");
	}

	[Theory]
	[InlineData(null, false)]
	[InlineData("", true)]
	public void Load_MultipleEditions_RequiresExplicitSelection(string edition, bool hasOverrides)
	{
		using var directory = new MigrationTestDirectory();
		var path = directory.Write(".edition", Editions());
		var original = File.ReadAllBytes(path);

		var error = Assert.Throws<InvalidOperationException>(() => PackCommand<Package.Tar>.VersionFile.Load(directory.Path, hasOverrides ? "zongsoft.hosting.web" : null, edition, hasOverrides ? new Version(4, 0, 0) : null));

		Assert.Contains(".edition", error.Message);
		Assert.Equal(original, File.ReadAllBytes(path));
	}

	[Theory]
	[InlineData("community", null, "Community", "1.0.0")]
	[InlineData("PROFESSIONAL", null, "Professional", "2.1.0")]
	[InlineData("professional", "2.5.0", "Professional", "2.5.0")]
	public void Load_SelectedEdition_PreservesNameAndUsesSelectedVersion(string edition, string version, string expectedEdition, string expectedVersion)
	{
		using var directory = new MigrationTestDirectory();
		directory.Write(".edition", Editions());

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
		var path = directory.Write(".edition", named ? Editions() : "Zongsoft.Hosting.Web@1.0.0\n");
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
		if(existing)
			directory.Write(".edition", "Zongsoft.Daemon@1.0.0\n");

		Assert.Throws<InvalidOperationException>(() => PackCommand<Package.Tar>.VersionFile.Load(directory.Path, name, null, version == null ? null : Version.Parse(version)));
	}

	[Theory]
	[InlineData("Zongsoft.Daemon@0.0.0\n")]
	[InlineData("Zongsoft.Daemon\n[Community]\n0.0.0\n")]
	public void Load_ZeroVersionFromFile_IsRejected(string content)
	{
		using var directory = new MigrationTestDirectory();
		directory.Write(".edition", content);

		Assert.Throws<InvalidOperationException>(() => PackCommand<Package.Tar>.VersionFile.Load(directory.Path, null, null, null));
	}

	[Theory]
	[InlineData("")]
	[InlineData("Zongsoft.Daemon@invalid\n")]
	[InlineData("Zongsoft.Daemon\n[Community]\n1.0.0\n[community]\n2.0.0\n")]
	public void Load_InvalidFile_FailsWithoutOverwriting(string content)
	{
		using var directory = new MigrationTestDirectory();
		var path = directory.Write(".edition", content);
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
		var path = Path.Combine(directory.Path, ".edition");
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
		Assert.False(File.Exists(Path.Combine(directory.Path, ".edition")));
	}
	#endregion

	#region 保存测试
	[Fact]
	public void Save_SelectedEdition_PreservesOtherVersionsAndOrder()
	{
		using var directory = new MigrationTestDirectory();
		var path = directory.Write(".edition", Editions());
		var file = PackCommand<Package.Tar>.VersionFile.Load(directory.Path, null, "professional", new Version(2, 5, 0));

		file.Save(Path.Combine(directory.Path, "host.rpm"));

		var saved = ApplicationManifest.Load(path);
		Assert.Null(saved.Version);
		Assert.Equal(new[] { "Community", "Professional", "Enterprise" }, saved.Editions.Select(edition => edition.Name));
		Assert.Equal(new[] { new Version(1, 0, 0), new Version(2, 5, 0), new Version(3, 0, 1) }, saved.Editions.Select(edition => edition.Version));
		Assert.Equal(file.Identifier, ApplicationIdentifier.Load(Path.Combine(directory.Path, ".version")));
		Assert.Equal(Encoding.UTF8.GetBytes("Zongsoft.Hosting.Web=Professional\r\n\r\n[Community]\r\n1.0.0\r\n\r\n[Professional]\r\n2.5.0\r\n\r\n[Enterprise]\r\n3.0.1\r\n"), File.ReadAllBytes(path));
	}

	[Fact]
	public void Save_SingleVersion_UsesCoreFormat()
	{
		using var directory = new MigrationTestDirectory();
		var path = directory.Write(".edition", "; hosting\nZongsoft.Daemon@1.0.0\n");
		var file = PackCommand<Package.Tar>.VersionFile.Load(directory.Path, null, "", new Version(2, 0, 1));

		file.Save(Path.Combine(directory.Path, "host.deb"));

		Assert.Equal(new Version(2, 0, 1), ApplicationManifest.Load(path).Version);
		Assert.Contains("hosting", File.ReadAllText(path));
		Assert.Equal(file.Identifier, ApplicationIdentifier.Load(Path.Combine(directory.Path, ".version")));
	}

	[Fact]
	public void Save_ManifestIdentifierConflict_PreservesManifestAndPackage()
	{
		using var directory = new MigrationTestDirectory();
		var path = directory.Write(".edition", Editions());
		var original = File.ReadAllBytes(path);
		var file = PackCommand<Package.Tar>.VersionFile.Load(directory.Path, null, "Community", new Version(2, 0, 0));
		var conflict = Path.Combine(directory.Path, ".version");
		Directory.CreateDirectory(conflict);
		var package = directory.Write("host.tar.gz", "retained package marker");

		var error = Assert.Throws<IOException>(() => file.Save(package));

		Assert.Contains(path, error.Message);
		Assert.Contains(conflict, error.Message);
		Assert.Contains(package, error.Message);
		Assert.Equal(original, File.ReadAllBytes(path));
		Assert.True(Directory.Exists(conflict));
		Assert.Equal("retained package marker", File.ReadAllText(package));
		Assert.Empty(Directory.GetDirectories(directory.Path, ".zongsoft-*"));
	}

	[Fact]
	public void Save_LockedIdentifier_RollsBackManifestAndPreservesPackage()
	{
		Assert.SkipWhen(!OperatingSystem.IsWindows(), "File sharing prevents replacement on Windows.");

		using var directory = new MigrationTestDirectory();
		var path = directory.Write(".edition", Editions());
		var identifier = directory.Write(".version", "different-Legacy@8.0.0\n");
		var originalManifest = File.ReadAllBytes(path);
		var originalIdentifier = File.ReadAllBytes(identifier);
		var file = PackCommand<Package.Tar>.VersionFile.Load(directory.Path, null, "Community", new Version(2, 0, 0));
		var package = directory.Write("host.tar.gz", "retained package marker");
		using var locked = File.Open(identifier, FileMode.Open, FileAccess.Read, FileShare.Read);

		Assert.Throws<IOException>(() => file.Save(package));
		Assert.Equal(originalManifest, File.ReadAllBytes(path));
		Assert.Equal(originalIdentifier, File.ReadAllBytes(identifier));
		Assert.Equal("retained package marker", File.ReadAllText(package));
		Assert.Empty(Directory.GetDirectories(directory.Path, ".zongsoft-*"));
	}

	[Fact]
	public void Save_Failure_ReportsPackageAndPreservesArtifact()
	{
		using var directory = new MigrationTestDirectory();
		var file = PackCommand<Package.Tar>.VersionFile.Load(directory.Path, "Zongsoft.Daemon", null, new Version(1, 2, 0));
		var path = Path.Combine(directory.Path, ".edition");
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

	#region 清单与标识
	[Theory]
	[InlineData(null, "Enterprise", "3.0.1")]
	[InlineData("", "Enterprise", "3.0.1")]
	[InlineData(" \t ", "Enterprise", "3.0.1")]
	[InlineData("community", "Community", "1.0.0")]
	public void Load_CurrentOrExplicitEdition_TakesPrecedence(string option, string edition, string version)
	{
		using var directory = new MigrationTestDirectory();
		var path = directory.Write(".edition", Editions().Replace("Zongsoft.Hosting.Web\n", "Zongsoft.Hosting.Web=Enterprise\n"));
		var legacy = directory.Write(".version", "Different.Application-Legacy@8.0.0");
		var original = File.ReadAllBytes(legacy);
		var file = PackCommand<Package.Tar>.VersionFile.Load(directory.Path, null, option, null);

		AssertIdentity(file.Identifier, "Zongsoft.Hosting.Web", edition, version);
		Assert.Equal(original, File.ReadAllBytes(legacy));
		file.Save(Path.Combine(directory.Path, "host.tar.gz"));
		Assert.Equal(edition, ApplicationManifest.Load(path).Editions.Current.Name);
		Assert.Equal(file.Identifier, ApplicationIdentifier.Load(legacy));
	}

	[Theory]
	[InlineData(null, "Community", "")]
	[InlineData(null, "Community", "\n")]
	[InlineData(null, "Community", "\r\n")]
	[InlineData("", "Community", "")]
	[InlineData("", "Community", "\n")]
	[InlineData("", "Community", "\r\n")]
	[InlineData("Enterprise", "Enterprise", "")]
	[InlineData("Enterprise", "Enterprise", "\n")]
	[InlineData("Enterprise", "Enterprise", "\r\n")]
	public void Load_Identifier_AllowsEditionOverrideAndSavesOnlyIdentifier(string option, string edition, string newline)
	{
		using var directory = new MigrationTestDirectory();
		var path = Path.Combine(directory.Path, ".version");
		File.WriteAllText(path, "Zongsoft.Hosting.Web-Community@1.0.0" + newline, new UTF8Encoding(false));
		var file = PackCommand<Package.Tar>.VersionFile.Load(directory.Path, null, option, new Version(2, 0, 0));

		AssertIdentity(file.Identifier, "Zongsoft.Hosting.Web", edition, "2.0.0");
		file.Save(Path.Combine(directory.Path, "host.tar.gz"));
		Assert.Equal(file.Identifier, ApplicationIdentifier.Load(path));
		Assert.False(File.Exists(Path.Combine(directory.Path, ".edition")));
	}

	[Fact]
	public void Load_IdentifierWithoutEdition_AllowsExplicitEdition()
	{
		using var directory = new MigrationTestDirectory();
		directory.Write(".version", "Zongsoft.Hosting.Web@1.0.0");
		var file = PackCommand<Package.Tar>.VersionFile.Load(directory.Path, null, "Enterprise", null);
		AssertIdentity(file.Identifier, "Zongsoft.Hosting.Web", "Enterprise", "1.0.0");
	}

	[Theory]
	[InlineData("")]
	[InlineData("Zongsoft.Hosting.Web=Missing\n[Community]\n1.0.0\n")]
	public void Load_InvalidManifest_DoesNotFallBack(string content)
	{
		using var directory = new MigrationTestDirectory();
		var path = directory.Write(".edition", content);
		var legacy = directory.Write(".version", "Zongsoft.Hosting.Web@1.0.0");
		var error = Assert.Throws<InvalidDataException>(() => PackCommand<Package.Tar>.VersionFile.Load(directory.Path, null, null, null));
		Assert.Contains(path, error.Message);
		Assert.Equal("Zongsoft.Hosting.Web@1.0.0", File.ReadAllText(legacy));
	}

	[Fact]
	public void Load_ManifestDirectory_DoesNotFallBack()
	{
		using var directory = new MigrationTestDirectory();
		Directory.CreateDirectory(Path.Combine(directory.Path, ".edition"));
		directory.Write(".version", "Zongsoft.Hosting.Web@1.0.0");
		Assert.Throws<InvalidDataException>(() => PackCommand<Package.Tar>.VersionFile.Load(directory.Path, null, null, null));
	}

	[Fact]
	public void Load_OldEditionFile_IsNotParsedAsManifest()
	{
		using var directory = new MigrationTestDirectory();
		directory.Write(".version", Editions());
		Assert.Throws<InvalidOperationException>(() => PackCommand<Package.Tar>.VersionFile.Load(directory.Path, null, null, null));
	}

	[Theory]
	[InlineData(false)]
	[InlineData(true)]
	public void Save_NewPairConflict_DoesNotLeaveHalfPair(bool directoryConflict)
	{
		using var directory = new MigrationTestDirectory();
		var file = PackCommand<Package.Tar>.VersionFile.Load(directory.Path, "Zongsoft.Hosting.Web", "Community", new Version(1, 0, 0));
		var conflict = Path.Combine(directory.Path, ".version");

		if(directoryConflict)
			Directory.CreateDirectory(conflict);
		else
			directory.Write(".version", "concurrent version");

		Assert.Throws<IOException>(() => file.Save(Path.Combine(directory.Path, "host.tar.gz")));
		Assert.False(File.Exists(Path.Combine(directory.Path, ".edition")));
		Assert.Empty(Directory.GetDirectories(directory.Path, ".zongsoft-*"));

		if(!directoryConflict)
			Assert.Equal("concurrent version", File.ReadAllText(conflict));
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
