using System.IO;
using System.Net.Http;
using System.Security.Cryptography;

namespace WinLaunch.Enhanced.Services;

// Downloads a release's setup file and keeps it only when its size and SHA-256
// match what GitHub published. Anything else is deleted before it can run.
public static class UpdateDownloader
{
    public static async Task<string> FetchAsync(HttpClient http, ReleaseAsset asset, string folder,
        Action<double> progress, CancellationToken cancel)
    {
        if (asset.Sha256 == null)
            throw new InvalidDataException("The release has no checksum to check the download against.");
        Directory.CreateDirectory(folder);
        var target = Path.Combine(folder, asset.Name);
        var partial = target + ".part";
        try
        {
            string actual;
            long written = 0;
            using (var response = await http.GetAsync(asset.Url, HttpCompletionOption.ResponseHeadersRead, cancel))
            {
                response.EnsureSuccessStatusCode();
                long total = response.Content.Headers.ContentLength ?? asset.Size;
                using var digest = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
                await using (var source = await response.Content.ReadAsStreamAsync(cancel))
                await using (var sink = new FileStream(partial, FileMode.Create, FileAccess.Write, FileShare.None))
                {
                    var buffer = new byte[64 * 1024];
                    int read;
                    while ((read = await source.ReadAsync(buffer.AsMemory(), cancel)) > 0)
                    {
                        await sink.WriteAsync(buffer.AsMemory(0, read), cancel);
                        digest.AppendData(buffer, 0, read);
                        written += read;
                        if (total > 0)
                            progress(Math.Min(1, (double)written / total));
                    }
                }
                actual = Convert.ToHexString(digest.GetHashAndReset()).ToLowerInvariant();
            }
            if (asset.Size > 0 && written != asset.Size)
                throw new InvalidDataException("The download stopped early. Try again.");
            if (actual != asset.Sha256)
                throw new InvalidDataException("The download does not match the checksum GitHub published, so it was discarded.");
            File.Move(partial, target, overwrite: true);
            return target;
        }
        catch
        {
            TryDelete(partial);
            throw;
        }
    }

    private static void TryDelete(string path)
    {
        try { File.Delete(path); }
        catch { }
    }
}
