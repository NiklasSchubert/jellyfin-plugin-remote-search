using Jellyfin.Plugin.RemoteSearch.Configuration;
using Jellyfin.Plugin.RemoteSearch.Downloads;
using Jellyfin.Plugin.RemoteSearch.Library;
using Jellyfin.Plugin.RemoteSearch.Backend;
using Jellyfin.Plugin.RemoteSearch.Storage;
using Microsoft.Extensions.Logging.Abstractions;

namespace Jellyfin.Plugin.RemoteSearch.Tests;

public sealed class DownloadServiceTests : IDisposable
{
    private static readonly byte[] OggBytes = "OggS fake audio"u8.ToArray();

    private readonly TemporaryDirectory _directory = new();
    private readonly FakeBackend _backend = new();
    private readonly RecordingLibraryRefresher _libraryRefresher = new();
    private readonly PluginSettings _settings;
    private readonly Ledger _ledger;
    private readonly PlaceholderStore _placeholders;
    private readonly DownloadService _downloads;

    public DownloadServiceTests()
    {
        _settings = new PluginSettings(ApplicationPathsStub.Create(_directory.Path));
        _ledger = new Ledger(_settings);
        _placeholders = new PlaceholderStore(_settings, TimeProvider.System);
        _downloads = new DownloadService(_backend, _ledger, _placeholders, _libraryRefresher, _settings, NullLogger<DownloadService>.Instance);
    }

    public void Dispose() => _directory.Dispose();

    [Fact]
    public async Task DownloadLandsInTheLibraryFolderAndLedger()
    {
        var track = Tracks.Create(title: "Song", artist: "Artist", album: "Album");
        _placeholders.Touch(track);

        var path = await _downloads.GetFileAsync(track.Id, CancellationToken.None);

        Assert.Equal(Path.Combine(_settings.DownloadPath, "Artist", "Album", "Song.ogg"), path);
        Assert.Equal(OggBytes, await File.ReadAllBytesAsync(path));
        Assert.Equal(path, _ledger.Get(track.Id)?.Path);
        Assert.Equal([path], _libraryRefresher.AddedFiles);
    }

    [Fact]
    public async Task ConcurrentPlaysDownloadOnce()
    {
        var track = Tracks.Create();
        _placeholders.Touch(track);
        _backend.Gate = new TaskCompletionSource();

        var first = _downloads.GetFileAsync(track.Id, CancellationToken.None);
        var second = _downloads.GetFileAsync(track.Id, CancellationToken.None);
        _backend.Gate.SetResult();

        Assert.Equal(await first, await second);
        Assert.Equal(1, _backend.Downloads);
    }

    [Fact]
    public async Task DownloadedTrackIsNotFetchedAgain()
    {
        var track = Tracks.Create();
        _placeholders.Touch(track);

        await _downloads.GetFileAsync(track.Id, CancellationToken.None);
        await _downloads.GetFileAsync(track.Id, CancellationToken.None);

        Assert.Equal(1, _backend.Downloads);
    }

    [Fact]
    public async Task CallerGivingUpDoesNotCancelTheDownload()
    {
        var track = Tracks.Create();
        _placeholders.Touch(track);
        _backend.Gate = new TaskCompletionSource();
        using var impatient = new CancellationTokenSource();

        var abandoned = _downloads.GetFileAsync(track.Id, impatient.Token);
        await impatient.CancelAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => abandoned);
        var patient = _downloads.GetFileAsync(track.Id, CancellationToken.None);
        _backend.Gate.SetResult();

        Assert.True(File.Exists(await patient));
        Assert.Equal(1, _backend.Downloads);
    }

    [Fact]
    public async Task FailedDownloadLeavesNothingBehindAndCanBeRetried()
    {
        var track = Tracks.Create();
        _placeholders.Touch(track);
        _backend.FailNext = true;

        await Assert.ThrowsAsync<BackendException>(() => _downloads.GetFileAsync(track.Id, CancellationToken.None));
        Assert.Empty(Directory.EnumerateFiles(_settings.DownloadPath, "*", SearchOption.AllDirectories));
        Assert.Null(_ledger.Get(track.Id));

        var path = await _downloads.GetFileAsync(track.Id, CancellationToken.None);
        Assert.True(File.Exists(path));
    }

    [Fact]
    public async Task LedgerKnowsTheFileBeforeItAppearsInTheLibraryFolder()
    {
        var track = Tracks.Create();
        _placeholders.Touch(track);
        _backend.OnDownload = () => Assert.NotNull(_ledger.Get(track.Id));

        await _downloads.GetFileAsync(track.Id, CancellationToken.None);
    }

    [Fact]
    public async Task FailedRedownloadKeepsThePreviousLedgerEntry()
    {
        var track = Tracks.Create();
        _placeholders.Touch(track);
        var path = await _downloads.GetFileAsync(track.Id, CancellationToken.None);
        var promoted = _ledger.Get(track.Id)! with { LocalItemId = Guid.NewGuid() };
        _ledger.Save(promoted);
        File.Delete(path);
        _backend.FailNext = true;

        await Assert.ThrowsAsync<BackendException>(() => _downloads.GetFileAsync(track.Id, CancellationToken.None));

        Assert.Equal(promoted, _ledger.Get(track.Id));
    }

    [Fact]
    public async Task NameTakenByAnotherTrackGetsTheIdAppended()
    {
        var original = Tracks.Create(id: "original");
        var namesake = Tracks.Create(id: "namesake");
        _placeholders.Touch(original);
        _placeholders.Touch(namesake);

        var originalPath = await _downloads.GetFileAsync(original.Id, CancellationToken.None);
        var namesakePath = await _downloads.GetFileAsync(namesake.Id, CancellationToken.None);

        Assert.NotEqual(originalPath, namesakePath);
        Assert.EndsWith("[namesake].ogg", namesakePath, StringComparison.Ordinal);
    }

    [Fact]
    public async Task UnknownIdIsRejected()
    {
        await Assert.ThrowsAsync<KeyNotFoundException>(() => _downloads.GetFileAsync("never-searched", CancellationToken.None));
    }

    private sealed class FakeBackend : IBackendClient
    {
        public TaskCompletionSource? Gate { get; set; }

        public bool FailNext { get; set; }

        public Action? OnDownload { get; set; }

        public int Downloads { get; private set; }

        public Task<IReadOnlyList<RemoteTrack>> SearchTracksAsync(string term, int limit, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public async Task DownloadAsync(string trackUri, string destinationPath, CancellationToken cancellationToken)
        {
            if (Gate is not null)
            {
                await Gate.Task;
            }

            OnDownload?.Invoke();
            await File.WriteAllBytesAsync(destinationPath, OggBytes, cancellationToken);
            if (FailNext)
            {
                FailNext = false;
                throw new BackendException("backend answered 504: timed out");
            }

            Downloads++;
        }
    }

    private sealed class RecordingLibraryRefresher : ILibraryRefresher
    {
        public List<string> AddedFiles { get; } = [];

        public Task FileAddedAsync(string path)
        {
            AddedFiles.Add(path);
            return Task.CompletedTask;
        }
    }
}
