using System.Security.Cryptography;
using NeatShield.Core.Logging;

namespace NeatShield.Core.Filters;

public enum UpdateStatus { UpToDate, Updated, Failed }

/// <param name="Dir">The release folder now in use (Updated / UpToDate).</param>
public sealed record UpdateResult(UpdateStatus Status, string? Version, string? Dir, string? Error);

/// <summary>
/// Follows the neat-filters client protocol: read manifest.json, download only the files whose
/// sha256 changed (copy the rest from the release in use), verify size and sha256, then make the new
/// release current by renaming its folder into place.
/// </summary>
/// <remarks>
/// sha256 values come from the same release as the files, so this protects against damaged or cut-off
/// downloads, not against a malicious release: whoever can publish a release of the filters repository
/// controls what the app blocks.
/// </remarks>
public sealed class FilterUpdater
{
    private const long MaxTotalBytes = 300L * 1024 * 1024;
    private const long MaxManifestBytes = 4L * 1024 * 1024;

    private readonly HttpClient _http;
    private readonly FilterStore _store;

    public FilterUpdater(HttpClient http, FilterStore store)
    {
        _http = http;
        _store = store;
    }

    public async Task<UpdateResult> UpdateAsync(string baseUrl, IProgress<string>? progress, CancellationToken ct)
    {
        string? tmpDir = null;
        try
        {
            if (!Uri.TryCreate(baseUrl.EndsWith('/') ? baseUrl : baseUrl + "/", UriKind.Absolute, out var baseUri) ||
                baseUri.Scheme != Uri.UriSchemeHttps)
            {
                return new UpdateResult(UpdateStatus.Failed, null, null, "Địa chỉ bộ lọc phải bắt đầu bằng https://");
            }

            progress?.Report("Đang tải manifest.json...");
            var manifestBytes = await DownloadSmallAsync(new Uri(baseUri, "manifest.json"), ct).ConfigureAwait(false);
            var manifest = FilterManifest.Parse(manifestBytes);
            manifest.Validate();

            var currentDir = _store.CurrentDir;
            var currentManifest = currentDir is null ? null : _store.ReadManifest(currentDir);

            // Same manifest bytes as the release in use: nothing to do.
            if (currentDir is not null && currentManifest is not null)
            {
                var currentBytes = await File.ReadAllBytesAsync(Path.Combine(currentDir, "manifest.json"), ct).ConfigureAwait(false);
                if (currentBytes.AsSpan().SequenceEqual(manifestBytes))
                {
                    return new UpdateResult(UpdateStatus.UpToDate, currentManifest.Version, currentDir, null);
                }
            }

            var finalDir = Path.Combine(_store.ReleasesDir, manifest.Version);
            tmpDir = Path.Combine(_store.ReleasesDir, ".tmp-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tmpDir);

            if (manifest.Files.Sum(f => f.Size) > MaxTotalBytes)
            {
                throw new InvalidDataException("release is larger than the allowed size");
            }

            var done = 0;
            foreach (var file in manifest.Files)
            {
                ct.ThrowIfCancellationRequested();
                done++;
                var dest = Path.Combine(tmpDir, file.Name);
                var reused = false;

                var old = currentManifest?.Files.FirstOrDefault(f => f.Name == file.Name);
                if (old is not null && currentDir is not null &&
                    string.Equals(old.Sha256, file.Sha256, StringComparison.OrdinalIgnoreCase))
                {
                    var src = Path.Combine(currentDir, file.Name);
                    if (File.Exists(src) && string.Equals(await HashFileAsync(src, ct).ConfigureAwait(false), file.Sha256, StringComparison.OrdinalIgnoreCase))
                    {
                        File.Copy(src, dest);
                        reused = true;
                    }
                }

                if (!reused)
                {
                    progress?.Report($"Đang tải {file.Name} ({done}/{manifest.Files.Count})...");
                    await DownloadVerifiedAsync(new Uri(baseUri, Uri.EscapeDataString(file.Name)), dest, file, ct).ConfigureAwait(false);
                }
            }

            // manifest.json goes in last, exactly as downloaded: its hash is the engine cache key.
            await File.WriteAllBytesAsync(Path.Combine(tmpDir, "manifest.json"), manifestBytes, ct).ConfigureAwait(false);

            if (Directory.Exists(finalDir))
            {
                Directory.Delete(finalDir, recursive: true);
            }

            Directory.Move(tmpDir, finalDir);
            tmpDir = null;
            _store.SetCurrent(manifest.Version);
            _store.Prune();

            Log.Info($"filters updated to {manifest.Version}");
            return new UpdateResult(UpdateStatus.Updated, manifest.Version, finalDir, null);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            Log.Warn($"filter update failed: {ex.Message}");
            return new UpdateResult(UpdateStatus.Failed, null, null, ex.Message);
        }
        finally
        {
            if (tmpDir is not null)
            {
                try
                {
                    Directory.Delete(tmpDir, recursive: true);
                }
                catch
                {
                    // Prune() removes leftovers next time.
                }
            }
        }
    }

    private async Task<byte[]> DownloadSmallAsync(Uri uri, CancellationToken ct)
    {
        using var response = await _http.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        if (response.Content.Headers.ContentLength > MaxManifestBytes)
        {
            throw new InvalidDataException("manifest.json is too large");
        }

        await using var stream = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
        using var ms = new MemoryStream();
        var buffer = new byte[81920];
        int n;
        while ((n = await stream.ReadAsync(buffer, ct).ConfigureAwait(false)) > 0)
        {
            ms.Write(buffer, 0, n);
            if (ms.Length > MaxManifestBytes) throw new InvalidDataException("manifest.json is too large");
        }

        return ms.ToArray();
    }

    private async Task DownloadVerifiedAsync(Uri uri, string dest, FilterFile expected, CancellationToken ct)
    {
        using var response = await _http.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();

        await using var src = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
        await using var dst = File.Create(dest);
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var buffer = new byte[81920];
        long total = 0;
        int n;
        while ((n = await src.ReadAsync(buffer, ct).ConfigureAwait(false)) > 0)
        {
            total += n;
            if (total > expected.Size)
            {
                throw new InvalidDataException($"{expected.Name} is larger than the manifest says");
            }

            hash.AppendData(buffer, 0, n);
            await dst.WriteAsync(buffer.AsMemory(0, n), ct).ConfigureAwait(false);
        }

        if (total != expected.Size)
        {
            throw new InvalidDataException($"{expected.Name}: size {total} differs from the manifest ({expected.Size})");
        }

        var actual = Convert.ToHexString(hash.GetHashAndReset());
        if (!string.Equals(actual, expected.Sha256, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException($"{expected.Name}: sha256 does not match the manifest");
        }
    }

    internal static async Task<string> HashFileAsync(string path, CancellationToken ct)
    {
        await using var stream = File.OpenRead(path);
        return Convert.ToHexString(await SHA256.HashDataAsync(stream, ct).ConfigureAwait(false));
    }
}
