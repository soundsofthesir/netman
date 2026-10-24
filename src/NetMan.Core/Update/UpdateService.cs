using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;

namespace NetMan.Core;

public sealed record UpdateSource(string Owner, string Repo);

/// <summary>A downloaded, checksum-verified release waiting to be installed.</summary>
public sealed record PendingUpdate(Version Version, string FilePath, string Sha256);

/// <summary>Checks GitHub for a newer release and downloads it into the updates folder.</summary>
public sealed class UpdateService(HttpClient http, UpdateSource source, string directory)
{
    private const int BufferSize = 81920;

    /// <summary>The verified newer release, or null when there is none. Throws <see cref="UpdateException"/> on network or checksum errors.</summary>
    public async Task<PendingUpdate?> CheckAsync(Version current, CancellationToken cancellationToken)
    {
        current = AppVersion.Normalize(current);
        var latest = new Uri($"https://api.github.com/repos/{source.Owner}/{source.Repo}/releases/latest");

        using var response = await SendAsync(latest, current, cancellationToken).ConfigureAwait(false);
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }

        var release = ReleaseParser.Parse(await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false));
        if (release is null || release.Version <= current)
        {
            return null;
        }

        var existing = UpdateInstaller.FindPending(directory, current);
        if (existing is not null && existing.Version == release.Version)
        {
            return existing;
        }

        return await DownloadAsync(release, current, cancellationToken).ConfigureAwait(false);
    }

    private async Task<PendingUpdate> DownloadAsync(ReleaseInfo release, Version current, CancellationToken ct)
    {
        string expected;
        using (var shaResponse = await SendAsync(release.ShaUrl, current, ct).ConfigureAwait(false))
        {
            expected = ReleaseParser.ParseSha256(await shaResponse.Content.ReadAsStringAsync(ct).ConfigureAwait(false));
        }

        Directory.CreateDirectory(directory);
        var name = string.Create(CultureInfo.InvariantCulture, $"NetMan-{release.Version}.exe");
        var final = Path.Combine(directory, name);
        var part = final + ".part";

        string actual;
        try
        {
            using var exeResponse = await SendAsync(release.ExeUrl, current, ct).ConfigureAwait(false);
            await using var input = await exeResponse.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
            await using var output = new FileStream(part, FileMode.Create, FileAccess.Write, FileShare.None, BufferSize, FileOptions.Asynchronous);
            using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            var buffer = new byte[BufferSize];
            int read;
            while ((read = await input.ReadAsync(buffer, ct).ConfigureAwait(false)) > 0)
            {
                hash.AppendData(buffer, 0, read);
                await output.WriteAsync(buffer.AsMemory(0, read), ct).ConfigureAwait(false);
            }

            actual = Convert.ToHexStringLower(hash.GetHashAndReset());
        }
        catch
        {
            File.Delete(part);
            throw;
        }

        if (actual != expected)
        {
            File.Delete(part);
            throw new UpdateException("Downloaded update does not match its checksum.");
        }

        File.Move(part, final, overwrite: true);
        var pending = new PendingUpdate(release.Version, final, actual);
        UpdateInstaller.WriteMarker(directory, pending);
        UpdateInstaller.DeleteOtherDownloads(directory, final);
        return pending;
    }

    private async Task<HttpResponseMessage> SendAsync(Uri url, Version current, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.UserAgent.Add(new ProductInfoHeaderValue("NetMan", current.ToString()));
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));

        HttpResponseMessage response;
        try
        {
            response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
        }
        catch (HttpRequestException ex)
        {
            throw new UpdateException($"Cannot reach {url.Host}.", ex);
        }
        catch (TaskCanceledException ex) when (!ct.IsCancellationRequested)
        {
            throw new UpdateException($"{url.Host} did not respond.", ex);
        }

        if (!response.IsSuccessStatusCode && response.StatusCode != HttpStatusCode.NotFound)
        {
            var status = (int)response.StatusCode;
            response.Dispose();
            throw new UpdateException($"Update server returned {status.ToString(CultureInfo.InvariantCulture)}.");
        }

        return response;
    }
}
