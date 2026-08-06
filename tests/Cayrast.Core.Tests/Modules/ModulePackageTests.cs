using System.IO.Compression;
using System.Text;
using Cayrast.Abstractions.Modules;
using Cayrast.Core.Modules;

namespace Cayrast.Core.Tests.Modules;

/// <summary>
/// Tests for <see cref="ModulePackage"/>.
/// </summary>
/// <remarks>
/// A <c>.cayrast</c> file is a ZIP archive the user downloaded from anywhere, processed
/// before they have agreed to anything. These are security tests: the rejection cases
/// below are attacks the format permits and the extractor has to refuse.
/// </remarks>
public sealed class ModulePackageTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "cayrast-pkg", Guid.NewGuid().ToString("N"));

    public ModulePackageTests() => Directory.CreateDirectory(_root);

    private const string ValidManifest = """
        {
          "name": "Example Module",
          "id": "example.module",
          "version": "1.0.0",
          "author": "Someone",
          "description": "An example",
          "permissions": ["network"],
          "entry": "main.dll"
        }
        """;

    /// <summary>Builds a package from a set of entry name / content pairs.</summary>
    private string CreatePackage(string name, params (string EntryName, string Content)[] entries)
    {
        var path = Path.Combine(_root, name);

        using var stream = File.Create(path);
        using var archive = new ZipArchive(stream, ZipArchiveMode.Create);

        foreach (var (entryName, content) in entries)
        {
            var entry = archive.CreateEntry(entryName);
            using var writer = new StreamWriter(entry.Open(), Encoding.UTF8);
            writer.Write(content);
        }

        return path;
    }

    // ---------------------------------------------------------------- Manifest

    [Fact]
    public void ReadManifest_AcceptsAWellFormedPackage()
    {
        var package = CreatePackage("valid.cayrast", ("manifest.json", ValidManifest));

        var (manifest, id, permissions) = ModulePackage.ReadManifest(package);

        Assert.Equal("Example Module", manifest.Name);
        Assert.Equal("example.module", id.Value);
        Assert.Equal(ModulePermission.Network, permissions);
    }

    [Fact]
    public void ReadManifest_RejectsAFileThatIsNotAnArchive()
    {
        var path = Path.Combine(_root, "bogus.cayrast");
        File.WriteAllText(path, "this is not a zip archive");

        var exception = Assert.Throws<PackageException>(() => ModulePackage.ReadManifest(path));
        Assert.Equal(PackageRejectionReason.NotAPackage, exception.Reason);
    }

    [Fact]
    public void ReadManifest_RejectsAPackageWithNoManifest()
    {
        var package = CreatePackage("empty.cayrast", ("readme.txt", "nothing here"));

        var exception = Assert.Throws<PackageException>(() => ModulePackage.ReadManifest(package));
        Assert.Equal(PackageRejectionReason.MissingManifest, exception.Reason);
    }

    [Theory]
    // A malformed id must be caught here, before it is used to build a directory path.
    [InlineData("""{ "name": "X", "id": "../../evil", "version": "1.0.0", "author": "A" }""")]
    [InlineData("""{ "name": "X", "id": "noNamespace", "version": "1.0.0", "author": "A" }""")]
    [InlineData("""{ "name": "", "id": "a.b", "version": "1.0.0", "author": "A" }""")]
    [InlineData("""{ "name": "X", "id": "a.b", "version": "not-a-version", "author": "A" }""")]
    [InlineData("not json at all")]
    public void ReadManifest_RejectsMalformedManifests(string manifestJson)
    {
        var package = CreatePackage($"bad-{Guid.NewGuid():N}.cayrast", ("manifest.json", manifestJson));

        var exception = Assert.Throws<PackageException>(() => ModulePackage.ReadManifest(package));
        Assert.Contains(exception.Reason, new[] { PackageRejectionReason.InvalidManifest, PackageRejectionReason.UnsafePath });
    }

    [Theory]
    // A path in the entry field is another way out of the module's own directory.
    [InlineData("""{ "name": "X", "id": "a.b", "version": "1.0.0", "author": "A", "entry": "../../evil.dll" }""")]
    [InlineData("""{ "name": "X", "id": "a.b", "version": "1.0.0", "author": "A", "entry": "sub\\dir\\main.dll" }""")]
    [InlineData("""{ "name": "X", "id": "a.b", "version": "1.0.0", "author": "A", "entry": "C:\\Windows\\evil.dll" }""")]
    public void ReadManifest_RejectsPathsInTheEntryField(string manifestJson)
    {
        var package = CreatePackage($"entry-{Guid.NewGuid():N}.cayrast", ("manifest.json", manifestJson));

        var exception = Assert.Throws<PackageException>(() => ModulePackage.ReadManifest(package));
        Assert.Equal(PackageRejectionReason.UnsafePath, exception.Reason);
    }

    [Fact]
    public void ParsePermissions_RejectsAnUnknownPermission()
    {
        // Rejected rather than ignored: silently dropping it would let a permission
        // introduced by a newer version slip past a consent prompt that did not know
        // to display it.
        var exception = Assert.Throws<PackageException>(() => ModulePackage.ParsePermissions(["network", "mindReading"]));

        Assert.Equal(PackageRejectionReason.UnknownPermission, exception.Reason);
        Assert.Contains("mindReading", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ParsePermissions_CombinesDeclaredCapabilities()
    {
        var permissions = ModulePackage.ParsePermissions(["filesystem", "network", "clipboard"]);

        Assert.Equal(
            ModulePermission.FileSystem | ModulePermission.Network | ModulePermission.Clipboard,
            permissions);
    }

    [Fact]
    public void ParsePermissions_IsCaseInsensitive()
    {
        // Manifests are hand-written; casing should not be a trap.
        Assert.Equal(ModulePermission.Network, ModulePackage.ParsePermissions(["NETWORK"]));
        Assert.Equal(ModulePermission.Network, ModulePackage.ParsePermissions(["Network"]));
    }

    // ---------------------------------------------------------------- Extraction

    [Fact]
    public void Extract_WritesPackageContents()
    {
        var package = CreatePackage("good.cayrast",
            ("manifest.json", ValidManifest),
            ("backend/main.dll", "fake assembly"),
            ("frontend/index.html", "<html></html>"));

        var destination = Path.Combine(_root, "extracted");
        ModulePackage.Extract(package, destination);

        Assert.True(File.Exists(Path.Combine(destination, "manifest.json")));
        Assert.True(File.Exists(Path.Combine(destination, "backend", "main.dll")));
        Assert.True(File.Exists(Path.Combine(destination, "frontend", "index.html")));
    }

    [Theory]
    [InlineData("../escaped.txt")]
    [InlineData("../../escaped.txt")]
    [InlineData("..\\..\\escaped.txt")]
    [InlineData("subdir/../../escaped.txt")]
    public void Extract_RefusesEntriesThatEscapeTheDestination(string maliciousEntryName)
    {
        // Zip slip. The archive format permits these names; extraction code has to
        // refuse them, or a package can drop a DLL anywhere the user can write.
        var package = CreatePackage($"slip-{Guid.NewGuid():N}.cayrast",
            ("manifest.json", ValidManifest),
            (maliciousEntryName, "malicious payload"));

        var destination = Path.Combine(_root, $"slip-{Guid.NewGuid():N}");

        var exception = Assert.Throws<PackageException>(() => ModulePackage.Extract(package, destination));
        Assert.Equal(PackageRejectionReason.UnsafePath, exception.Reason);

        // The escaped file must not exist anywhere outside the destination.
        Assert.False(File.Exists(Path.Combine(_root, "escaped.txt")));
    }

    [Fact]
    public void Extract_RefusesTooManyEntries()
    {
        var path = Path.Combine(_root, "many.cayrast");

        using (var stream = File.Create(path))
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Create))
        {
            archive.CreateEntry("manifest.json");

            // Just over the limit. Each entry is empty, so this stays fast while still
            // exercising the guard.
            for (var i = 0; i < 10_001; i++)
            {
                archive.CreateEntry($"file{i}.txt");
            }
        }

        var exception = Assert.Throws<PackageException>(() => ModulePackage.Extract(path, Path.Combine(_root, "many")));
        Assert.Equal(PackageRejectionReason.TooLarge, exception.Reason);
    }

    [Fact]
    public void Extract_RefusesAZipBomb()
    {
        // Highly compressible content: a small archive that expands enormously. The
        // declared size in the header cannot be trusted, so the extractor counts bytes
        // as they are written.
        var path = Path.Combine(_root, "bomb.cayrast");

        using (var stream = File.Create(path))
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Create))
        {
            archive.CreateEntry("manifest.json");

            var entry = archive.CreateEntry("bomb.bin", CompressionLevel.SmallestSize);
            using var output = entry.Open();

            // 300 MB of zeroes compresses to a few hundred kilobytes.
            var chunk = new byte[1024 * 1024];
            for (var i = 0; i < 300; i++)
            {
                output.Write(chunk);
            }
        }

        var exception = Assert.Throws<PackageException>(() => ModulePackage.Extract(path, Path.Combine(_root, "bomb")));
        Assert.Equal(PackageRejectionReason.TooLarge, exception.Reason);
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_root))
            {
                Directory.Delete(_root, recursive: true);
            }
        }
        catch (IOException)
        {
            // A leftover temp directory is not worth failing a test run over.
        }
    }
}
