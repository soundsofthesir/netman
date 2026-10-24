using System.Globalization;
using System.Net;
using System.Net.Http.Headers;

namespace NetMan.Core;

public sealed record UpdateSource(string Owner, string Repo);

/// <summary>A downloaded release with its signature, waiting to be installed.</summary>
public sealed record PendingUpdate(Version Version, string FilePath, string Signature);

/// <summary>Checks GitHub for a newer release, downloads it and keeps it only if its signature is valid.</summary>
public sealed class UpdateService(HttpClient http, UpdateSource source, string directory, string publicKey)
{
    /// <summary>Far above the real exe size; stops a hostile server from filling memory.</summary>
    private const long MaxExeBytes = 300L * 1024 * 1024;

    /// <summary>The verified newer release, or null when there is none. Throws <see cref="UpdateException"/> on network or signature errors.</summary>
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

        var existing = UpdateInstaller.FindPending(directory, current, publicKey);
        if (existing is not null && existing.Version == release.Version)
        {
            return existing;
        }

        return await DownloadAsync(release, current, cancellationToken).ConfigureAwait(false);
    }

    private async Task<PendingUpdate> DownloadAsync(ReleaseInfo release, Version current, CancellationToken ct)
    {
        string signature;
        using (var sigResponse = await SendAsync(release.SigUrl, current, ct).ConfigureAwait(false))
        {
            signature = (await sigResponse.Content.ReadAsStringAsync(ct).ConfigureAwait(false)).Trim();
        }

        byte[] exe;
        using (var exeResponse = await SendAsync(release.ExeUrl, current, ct).ConfigureAwait(false))
        {
            if (exeResponse.Content.Headers.ContentLength > MaxExeBytes)
            {
                throw new UpdateException("Update download is too large.");
            }

            exe = await exeResponse.Content.ReadAsByteArrayAsync(ct).ConfigureAwait(false);
        }

        if (exe.LongLength > MaxExeBytes || !UpdateSignature.Verify(exe, release.Version, signature, publicKey))
        {
            throw new UpdateException("Update signature is not valid.");
        }

        Directory.CreateDirectory(directory);
        var final = Path.Combine(directory, string.Create(CultureInfo.InvariantCulture, $"NetMan-{release.Version}.exe"));
        var part = final + ".part";
        await File.WriteAllBytesAsync(part, exe, ct).ConfigureAwait(false);
        File.Move(part, final, overwrite: true);

        var pending = new PendingUpdate(release.Version, final, signature);
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
