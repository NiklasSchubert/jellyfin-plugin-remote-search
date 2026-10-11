namespace Jellyfin.Plugin.RemoteSearch.Backend;

public interface IBackendClient
{
    /// <exception cref="BackendException">the backend is unreachable or answers with an error.</exception>
    Task<IReadOnlyList<RemoteTrack>> SearchTracksAsync(string term, int limit, CancellationToken cancellationToken);

    /// <summary>Writes the track's tagged Ogg file to <paramref name="destinationPath"/>.</summary>
    /// <exception cref="BackendException">the backend is unreachable or answers with an error.</exception>
    Task DownloadAsync(string trackUri, string destinationPath, CancellationToken cancellationToken);
}

public sealed class BackendException : Exception
{
    public BackendException(string message, Exception? innerException = null)
        : base(message, innerException)
    {
    }
}
