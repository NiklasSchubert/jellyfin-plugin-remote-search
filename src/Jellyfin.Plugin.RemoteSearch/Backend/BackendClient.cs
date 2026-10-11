using System.Globalization;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Jellyfin.Plugin.RemoteSearch.Configuration;

namespace Jellyfin.Plugin.RemoteSearch.Backend;

/// <summary>HTTP over the backend's unix socket, see docs/BACKEND-API.md.</summary>
public sealed class BackendClient : IBackendClient, IDisposable
{
    private const string TrackKind = "track";

    // longer than the backend's own 3 min limit, which includes the wait behind other downloads
    private static readonly TimeSpan DownloadTimeout = TimeSpan.FromMinutes(4);

    // searches outlive the caller's SearchTimeout to fill the cache, but a hung backend must not pin them forever
    private static readonly TimeSpan SearchTimeout = TimeSpan.FromMinutes(1);
    private static readonly Uri BaseAddress = new("http://backend/");

    private readonly PluginSettings _settings;
    private readonly HttpClient _http;

    public BackendClient(PluginSettings settings)
    {
        _settings = settings;
        var handler = new SocketsHttpHandler { ConnectCallback = ConnectToSocketAsync };
        _http = new HttpClient(handler) { BaseAddress = BaseAddress, Timeout = Timeout.InfiniteTimeSpan };
    }

    public Task<IReadOnlyList<RemoteTrack>> SearchTracksAsync(string term, int limit, CancellationToken cancellationToken)
    {
        var requestUri = string.Create(CultureInfo.InvariantCulture, $"search?type={TrackKind}&limit={limit}&q={Uri.EscapeDataString(term)}");
        return WithTimeoutAsync(SearchTimeout, cancellationToken, token => SearchCoreAsync(requestUri, token));
    }

    public Task DownloadAsync(string trackUri, string destinationPath, CancellationToken cancellationToken) =>
        WithTimeoutAsync(DownloadTimeout, cancellationToken, token => DownloadCoreAsync(trackUri, destinationPath, token));

    public void Dispose() => _http.Dispose();

    internal static RemoteTrack ToTrack(SearchItem item) => new(
        item.Uri,
        item.Name,
        item.Artists ?? [],
        item.AlbumName,
        item.DurationMs,
        item.CoverUrl);

    internal static IReadOnlyList<RemoteTrack> ParseSearchResponse(string body)
    {
        List<SearchItem>? items;
        try
        {
            items = JsonSerializer.Deserialize<List<SearchItem>>(body);
        }
        catch (JsonException ex)
        {
            throw new BackendException("backend sent an invalid search response: " + ex.Message, ex);
        }

        return (items ?? [])
            .Where(item => item.Kind == TrackKind && !string.IsNullOrEmpty(item.Uri))
            .Select(ToTrack)
            .ToList();
    }

    private static async Task<T> WithTimeoutAsync<T>(TimeSpan limit, CancellationToken cancellationToken, Func<CancellationToken, Task<T>> operation)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(limit);
        try
        {
            return await operation(timeout.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException ex) when (!cancellationToken.IsCancellationRequested)
        {
            throw new BackendException($"backend did not finish within {limit}", ex);
        }
    }

    private static Task WithTimeoutAsync(TimeSpan limit, CancellationToken cancellationToken, Func<CancellationToken, Task> operation) =>
        WithTimeoutAsync(limit, cancellationToken, async token =>
        {
            await operation(token).ConfigureAwait(false);
            return true;
        });

    private async Task<IReadOnlyList<RemoteTrack>> SearchCoreAsync(string requestUri, CancellationToken cancellationToken)
    {
        using var response = await SendAsync(() => _http.GetAsync(requestUri, cancellationToken)).ConfigureAwait(false);
        var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        return ParseSearchResponse(body);
    }

    private async Task DownloadCoreAsync(string trackUri, string destinationPath, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "download")
        {
            // JsonContent streams chunked; a sized body suits any HTTP server on the socket
            Content = new StringContent(JsonSerializer.Serialize(new DownloadRequest(trackUri)), Encoding.UTF8, "application/json"),
        };
        using var response = await SendAsync(
            () => _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken)).ConfigureAwait(false);

        var destination = File.Create(destinationPath);
        await using (destination.ConfigureAwait(false))
        {
            await response.Content.CopyToAsync(destination, cancellationToken).ConfigureAwait(false);
        }
    }

    private static async Task<HttpResponseMessage> SendAsync(Func<Task<HttpResponseMessage>> send)
    {
        HttpResponseMessage response;
        try
        {
            response = await send().ConfigureAwait(false);
        }
        catch (HttpRequestException ex)
        {
            throw new BackendException("backend is not reachable: " + ex.Message, ex);
        }

        if (response.IsSuccessStatusCode)
        {
            return response;
        }

        using (response)
        {
            var error = await ReadErrorAsync(response).ConfigureAwait(false);
            throw new BackendException($"backend answered {(int)response.StatusCode}: {error}");
        }
    }

    private static async Task<string> ReadErrorAsync(HttpResponseMessage response)
    {
        var body = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
        try
        {
            return JsonSerializer.Deserialize<ErrorBody>(body)?.Error ?? body;
        }
        catch (JsonException)
        {
            return body;
        }
    }

    private async ValueTask<Stream> ConnectToSocketAsync(SocketsHttpConnectionContext context, CancellationToken cancellationToken)
    {
        var socket = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
        try
        {
            await socket.ConnectAsync(new UnixDomainSocketEndPoint(_settings.BackendSocketPath), cancellationToken).ConfigureAwait(false);
            return new NetworkStream(socket, ownsSocket: true);
        }
        catch
        {
            socket.Dispose();
            throw;
        }
    }

    internal sealed record SearchItem(
        [property: JsonPropertyName("kind")] string Kind,
        [property: JsonPropertyName("uri")] string Uri,
        [property: JsonPropertyName("name")] string Name,
        [property: JsonPropertyName("artists")] string[]? Artists,
        [property: JsonPropertyName("duration_ms")] long DurationMs,
        [property: JsonPropertyName("album_name")] string? AlbumName,
        [property: JsonPropertyName("cover_url")] string? CoverUrl);

    private sealed record DownloadRequest([property: JsonPropertyName("uri")] string Uri);

    private sealed record ErrorBody([property: JsonPropertyName("error")] string? Error);
}
