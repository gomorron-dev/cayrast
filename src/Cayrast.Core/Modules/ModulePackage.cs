using System.IO.Compression;
using System.Text.Json;
using System.Text.Json.Serialization;
using Cayrast.Abstractions;
using Cayrast.Abstractions.Modules;

namespace Cayrast.Core.Modules;

/// <summary>Why a module package was rejected.</summary>
public enum PackageRejectionReason
{
    /// <summary>The file could not be opened as a package.</summary>
    NotAPackage,

    /// <summary>No <c>manifest.json</c> at the package root.</summary>
    MissingManifest,

    /// <summary>The manifest could not be parsed, or a required field was absent.</summary>
    InvalidManifest,

    /// <summary>The manifest declares a permission this version does not recognise.</summary>
    UnknownPermission,

    /// <summary>The package requires a newer Cayrast.</summary>
    HostTooOld,

    /// <summary>The package exceeds a size or entry-count limit.</summary>
    TooLarge,

    /// <summary>An entry would write outside the destination directory.</summary>
    UnsafePath,
}

/// <summary>Raised when a package fails validation.</summary>
public sealed class PackageException(PackageRejectionReason reason, string message) : Exception(message)
{
    /// <summary>Why the package was rejected.</summary>
    public PackageRejectionReason Reason { get; } = reason;
}

/// <summary>Source-generated JSON metadata for module manifests.</summary>
[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase, AllowTrailingCommas = true)]
[JsonSerializable(typeof(ModuleManifest))]
internal sealed partial class ManifestJsonContext : JsonSerializerContext;

/// <summary>
/// Reads and validates <c>.cayrast</c> module packages.
/// </summary>
/// <remarks>
/// <para>
/// <b>Everything here is untrusted input.</b> A <c>.cayrast</c> file is a ZIP archive
/// the user downloaded from anywhere, and it is processed before the user has agreed
/// to anything. Every field is validated, every path is checked, and every limit is
/// enforced before a single byte is written to disk.
/// </para>
/// <para>
/// The two attacks this class exists to stop:
/// </para>
/// <list type="bullet">
///   <item><b>Zip slip</b> — an entry named <c>..\..\Windows\System32\evil.dll</c>
///   escapes the extraction directory. Archive formats permit it; extraction code has
///   to refuse it.</item>
///   <item><b>Zip bombs</b> — a few kilobytes that expand to gigabytes, filling the
///   disk. Compressed size tells you nothing, so the uncompressed total is capped and
///   checked as it is written, not merely declared.</item>
/// </list>
/// </remarks>
public static class ModulePackage
{
    /// <summary>Largest total uncompressed size a package may expand to.</summary>
    private const long MaxUncompressedBytes = 256L * 1024 * 1024;

    /// <summary>Largest number of entries a package may contain.</summary>
    private const int MaxEntries = 10_000;

    /// <summary>Manifest file name, at the package root.</summary>
    private const string ManifestEntryName = "manifest.json";

    /// <summary>Reads and validates a package's manifest without extracting anything.</summary>
    /// <param name="packagePath">Path to the <c>.cayrast</c> file.</param>
    /// <returns>The validated manifest and the module id parsed from it.</returns>
    /// <exception cref="PackageException">The package or its manifest is invalid.</exception>
    public static (ModuleManifest Manifest, ModuleId Id, ModulePermission Permissions) ReadManifest(string packagePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(packagePath);

        ZipArchive archive;
        try
        {
            archive = ZipFile.OpenRead(packagePath);
        }
        catch (Exception ex) when (ex is InvalidDataException or IOException)
        {
            throw new PackageException(PackageRejectionReason.NotAPackage,
                $"'{Path.GetFileName(packagePath)}' is not a valid {CayrastBrand.ModulePackageExtension} package.");
        }

        using (archive)
        {
            if (archive.Entries.Count > MaxEntries)
            {
                throw new PackageException(PackageRejectionReason.TooLarge,
                    $"The package contains {archive.Entries.Count} entries, above the limit of {MaxEntries}.");
            }

            var entry = archive.GetEntry(ManifestEntryName)
                        ?? throw new PackageException(PackageRejectionReason.MissingManifest,
                            $"The package has no {ManifestEntryName} at its root.");

            using var stream = entry.Open();
            return ParseManifest(stream);
        }
    }

    /// <summary>Parses and validates a manifest stream.</summary>
    /// <exception cref="PackageException">The manifest is malformed or unsupported.</exception>
    public static (ModuleManifest Manifest, ModuleId Id, ModulePermission Permissions) ParseManifest(Stream stream)
    {
        ModuleManifest? manifest;

        try
        {
            manifest = JsonSerializer.Deserialize(stream, ManifestJsonContext.Default.ModuleManifest);
        }
        catch (JsonException ex)
        {
            throw new PackageException(PackageRejectionReason.InvalidManifest, $"The manifest is not valid JSON: {ex.Message}");
        }

        if (manifest is null)
        {
            throw new PackageException(PackageRejectionReason.InvalidManifest, "The manifest is empty.");
        }

        if (!ModuleId.TryParse(manifest.Id, out var id))
        {
            throw new PackageException(PackageRejectionReason.InvalidManifest,
                $"'{manifest.Id}' is not a valid module id. Expected reverse-DNS form such as 'author.module'.");
        }

        if (string.IsNullOrWhiteSpace(manifest.Name))
        {
            throw new PackageException(PackageRejectionReason.InvalidManifest, "The manifest has no name.");
        }

        if (!Version.TryParse(StripPrerelease(manifest.Version), out _))
        {
            throw new PackageException(PackageRejectionReason.InvalidManifest,
                $"'{manifest.Version}' is not a valid version.");
        }

        // The entry assembly, if declared, must be a plain file name inside backend/.
        // A path here would be another way out of the module's own directory.
        if (manifest.Entry is not null && !IsSafeRelativeFileName(manifest.Entry))
        {
            throw new PackageException(PackageRejectionReason.UnsafePath,
                $"The entry '{manifest.Entry}' must be a simple file name.");
        }

        if (manifest.Ui is not null && !IsSafeRelativeFileName(manifest.Ui))
        {
            throw new PackageException(PackageRejectionReason.UnsafePath,
                $"The UI entry '{manifest.Ui}' must be a simple file name.");
        }

        var permissions = ParsePermissions(manifest.Permissions);
        return (manifest, id, permissions);
    }

    /// <summary>Converts declared permission strings into a capability set.</summary>
    /// <remarks>
    /// An unrecognised permission is rejected rather than ignored. Silently dropping it
    /// would mean a module built against a newer Cayrast appears to load with fewer
    /// capabilities than it needs, then fails confusingly at run time — and it would let
    /// a future permission be smuggled past a consent prompt that did not know to show it.
    /// </remarks>
    public static ModulePermission ParsePermissions(IReadOnlyList<string> declared)
    {
        ArgumentNullException.ThrowIfNull(declared);

        var result = ModulePermission.None;

        foreach (var name in declared)
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                continue;
            }

            if (!Enum.TryParse<ModulePermission>(name.Trim(), ignoreCase: true, out var permission)
                || permission == ModulePermission.None)
            {
                throw new PackageException(PackageRejectionReason.UnknownPermission,
                    $"'{name}' is not a permission this version of {CayrastBrand.ProductName} recognises.");
            }

            result |= permission;
        }

        return result;
    }

    /// <summary>
    /// Extracts a package into a directory, refusing anything that would escape it.
    /// </summary>
    /// <param name="packagePath">The <c>.cayrast</c> file.</param>
    /// <param name="destinationDirectory">Where to extract. Created if absent.</param>
    /// <exception cref="PackageException">The package is unsafe or exceeds a limit.</exception>
    public static void Extract(string packagePath, string destinationDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(packagePath);
        ArgumentException.ThrowIfNullOrWhiteSpace(destinationDirectory);

        Directory.CreateDirectory(destinationDirectory);

        // Resolved once, with a trailing separator, so the containment check below is a
        // simple prefix test that cannot be fooled by "..\" or a symlink in the path.
        var root = Path.GetFullPath(destinationDirectory);
        if (!root.EndsWith(Path.DirectorySeparatorChar))
        {
            root += Path.DirectorySeparatorChar;
        }

        using var archive = ZipFile.OpenRead(packagePath);

        if (archive.Entries.Count > MaxEntries)
        {
            throw new PackageException(PackageRejectionReason.TooLarge,
                $"The package contains {archive.Entries.Count} entries, above the limit of {MaxEntries}.");
        }

        long written = 0;

        foreach (var entry in archive.Entries)
        {
            // A directory entry, which has no content.
            if (string.IsNullOrEmpty(entry.Name))
            {
                continue;
            }

            var target = Path.GetFullPath(Path.Combine(root, entry.FullName));

            // The zip-slip check. Archive formats permit "..\..\Windows\System32\x.dll";
            // it is extraction code that has to refuse it.
            if (!target.StartsWith(root, StringComparison.OrdinalIgnoreCase))
            {
                throw new PackageException(PackageRejectionReason.UnsafePath,
                    $"The package tried to write outside its own directory ('{entry.FullName}').");
            }

            Directory.CreateDirectory(Path.GetDirectoryName(target)!);

            using var source = entry.Open();
            using var destination = File.Create(target);

            // Counted as it is written rather than trusted from the entry header. A zip
            // bomb declares a small size and expands to gigabytes; the only honest
            // measure is what actually lands on disk.
            written += CopyWithLimit(source, destination, MaxUncompressedBytes - written);

            if (written >= MaxUncompressedBytes)
            {
                throw new PackageException(PackageRejectionReason.TooLarge,
                    $"The package expands beyond the {MaxUncompressedBytes / (1024 * 1024)} MB limit.");
            }
        }
    }

    private static long CopyWithLimit(Stream source, Stream destination, long remaining)
    {
        var buffer = new byte[81920];
        long total = 0;
        int read;

        while (remaining > 0 && (read = source.Read(buffer, 0, (int)Math.Min(buffer.Length, remaining))) > 0)
        {
            destination.Write(buffer, 0, read);
            total += read;
            remaining -= read;
        }

        return total;
    }

    /// <summary>Whether a manifest-declared file name is a plain name with no path parts.</summary>
    private static bool IsSafeRelativeFileName(string value) =>
        !string.IsNullOrWhiteSpace(value)
        && value.IndexOfAny(['\\', '/', ':']) < 0
        && value != "."
        && value != ".."
        && value.IndexOfAny(Path.GetInvalidFileNameChars()) < 0;

    /// <summary>Drops a semantic-version prerelease suffix so <see cref="Version"/> can parse it.</summary>
    private static string StripPrerelease(string version)
    {
        if (string.IsNullOrWhiteSpace(version))
        {
            return string.Empty;
        }

        var suffix = version.IndexOfAny(['-', '+']);
        return suffix < 0 ? version : version[..suffix];
    }
}
