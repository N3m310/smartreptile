namespace SmartReptile.Application.Abstractions;

/// <summary>
/// Storage for the binary things the database should not carry: a species profile's picture now, camera snapshots
/// and exports when FR-15/FR-17 are built. The store mints every key, so a caller never supplies a name and a
/// stored file can never be addressed by anything a request said.
/// </summary>
/// <remarks>
/// The port is deliberately four methods wide, and two consequences are the caller's to handle rather than the
/// store's, because only the caller knows which unit of work a file belongs to. A file written for a change that is
/// then refused is an orphan — harmless, since nothing refers to it. A file whose reference has been committed
/// over is deleted afterwards; a failure there leaves an unreferenced file rather than a row pointing at a missing
/// one, which is why <see cref="DeleteAsync"/> reports nothing.
/// </remarks>
public interface IFileStore
{
    /// <summary>Writes a file and answers the key that addresses it. The key is not derived from anything the caller supplied.</summary>
    /// <param name="content">Bytes to store, read to the end.</param>
    /// <param name="extension">
    /// Extension to store the file under, including the leading dot. It comes from the type the bytes were sniffed
    /// as, never from a name the caller sent, so the extension cannot disagree with the content.
    /// </param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The key to record and to pass back to <see cref="OpenAsync"/> or <see cref="DeleteAsync"/>.</returns>
    Task<string> SaveAsync(Stream content, string extension, CancellationToken cancellationToken);

    /// <summary>Opens a stored file, or answers null when the key holds nothing.</summary>
    /// <param name="key">Key returned by <see cref="SaveAsync"/>.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A readable stream the caller disposes, or null.</returns>
    Task<Stream?> OpenAsync(string key, CancellationToken cancellationToken);

    /// <summary>
    /// Deletes a stored file. A key that holds nothing is not an error — the caller is dropping a reference, not
    /// asking whether one existed — and neither is a deletion the filesystem refuses: the reference is already gone
    /// by the time this is called, so the worst case is a file nobody points at.
    /// </summary>
    /// <param name="key">Key returned by <see cref="SaveAsync"/>.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task DeleteAsync(string key, CancellationToken cancellationToken);
}
