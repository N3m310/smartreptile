using System.Text.RegularExpressions;
using Microsoft.Extensions.Options;
using SmartReptile.Application.Abstractions;
using SmartReptile.Infrastructure.Options;

namespace SmartReptile.Infrastructure.Storage;

/// <summary>
/// The filesystem implementation of <see cref="IFileStore"/>: one directory per stored file, under the configured
/// root (§03-implementation/03 §2).
/// </summary>
/// <remarks>
/// Three things make a filesystem safe to hand a request anywhere near.
/// <list type="number">
/// <item>
/// <b>The key is minted here and is opaque.</b> A caller supplies bytes and an extension it sniffed; the filename
/// is a fresh GUID, so nothing a request carried reaches the path.
/// </item>
/// <item>
/// <b>Keys are validated before they are joined to the root</b>, because the key is the one value that later comes
/// back out of the database: <c>ImageStorageKey</c> is read on every image request. A row edited by hand — or by a
/// future migration bug — cannot turn into <c>../../appsettings.json</c>.
/// </item>
/// <item>
/// <b>Each file gets its own two-character subdirectory.</b> A single flat directory is fine for a demo and a
/// problem at ten thousand files, and the shard costs nothing to add now — it is not something a later cleanup can
/// introduce without moving every existing file.
/// </item>
/// </list>
/// The extension stays only an extension: the media type the profile records is the sniffed one, and that is what
/// decides how the file is served. This class never interprets content.
/// </remarks>
public sealed class FileSystemFileStore : IFileStore
{
    /// <summary>A key this store could have minted: 32 hex characters of GUID and a short extension.</summary>
    private static readonly Regex KeyShape = new(@"^[0-9a-f]{32}\.[a-z0-9]{1,5}$", RegexOptions.Compiled);

    private readonly string root;
    private readonly ILogger<FileSystemFileStore> logger;

    /// <summary>Creates the store and resolves the root against the content root when it is not already absolute.</summary>
    /// <param name="options">Storage configuration.</param>
    /// <param name="environment">Host environment, which supplies the content root for a relative path.</param>
    /// <param name="logger">Logger for a refused key or a deletion the filesystem would not perform.</param>
    public FileSystemFileStore(
        IOptions<StorageOptions> options,
        IHostEnvironment environment,
        ILogger<FileSystemFileStore> logger)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(environment);

        this.logger = logger;

        var configured = string.IsNullOrWhiteSpace(options.Value.RootPath) ? "uploads" : options.Value.RootPath;

        root = Path.IsPathRooted(configured) ? configured : Path.Combine(environment.ContentRootPath, configured);
    }

    /// <inheritdoc />
    public async Task<string> SaveAsync(Stream content, string extension, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(content);

        var key = $"{Guid.NewGuid():N}{Normalise(extension)}";
        var path = PathFor(key);

        Directory.CreateDirectory(Path.GetDirectoryName(path)!);

        // CreateNew: the key is fresh, so a file already at this name would mean something is deeply wrong, and
        // overwriting it would be worse than failing.
        await using var file = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None);
        await content.CopyToAsync(file, cancellationToken).ConfigureAwait(false);

        return key;
    }

    /// <inheritdoc />
    public Task<Stream?> OpenAsync(string key, CancellationToken cancellationToken)
    {
        if (!TryPathFor(key, out var path) || !File.Exists(path))
        {
            return Task.FromResult<Stream?>(null);
        }

        return Task.FromResult<Stream?>(new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read));
    }

    /// <inheritdoc />
    public Task DeleteAsync(string key, CancellationToken cancellationToken)
    {
        if (!TryPathFor(key, out var path) || !File.Exists(path))
        {
            return Task.CompletedTask;
        }

        try
        {
            File.Delete(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // The caller has already committed the change that dropped the reference, so failing here would turn a
            // successful edit into an error. The file stays and nothing points at it.
            logger.LogWarning(ex, "Stored file {Key} could not be deleted; it is now unreferenced", key);
        }

        return Task.CompletedTask;
    }

    private string PathFor(string key) => Path.Combine(root, key[..2], key);

    /// <summary>Resolves a stored key, or refuses it. See the class remarks for why the shape is checked.</summary>
    private bool TryPathFor(string key, out string path)
    {
        path = string.Empty;

        if (string.IsNullOrWhiteSpace(key) || !KeyShape.IsMatch(key))
        {
            logger.LogWarning("Refused a storage key that is not one of this store's: {Key}", key);

            return false;
        }

        path = PathFor(key);

        return true;
    }

    /// <summary>Keeps a supplied extension to a safe lower-case suffix; anything else becomes <c>.bin</c>.</summary>
    private static string Normalise(string? extension)
    {
        if (string.IsNullOrWhiteSpace(extension))
        {
            return ".bin";
        }

        var trimmed = extension.Trim().ToLowerInvariant();

        return Regex.IsMatch(trimmed, @"^\.[a-z0-9]{1,4}$") ? trimmed : ".bin";
    }
}
