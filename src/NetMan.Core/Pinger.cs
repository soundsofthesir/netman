using System.Net;
using System.Net.NetworkInformation;

namespace NetMan.Core;

public sealed record PingResult(int Sent, int Received, long? AverageMs)
{
    public bool Reachable => Received > 0;
}

public interface IPinger
{
    Task<PingResult> PingAsync(string target, int count, TimeSpan timeout, CancellationToken cancellationToken);
}

public sealed class Pinger : IPinger
{
    private static readonly byte[] Payload = new byte[32];

    public async Task<PingResult> PingAsync(string target, int count, TimeSpan timeout, CancellationToken cancellationToken)
    {
        if (!Ipv4.TryParse(target, out var value))
        {
            throw new ArgumentException("Not a valid IPv4 address.", nameof(target));
        }

        if (count < 1)
        {
            throw new ArgumentException("Count must be at least 1.", nameof(count));
        }

        var address = IPAddress.Parse(Ipv4.Format(value));
        using var ping = new Ping();
        var received = 0;
        long total = 0;

        for (var i = 0; i < count; i++)
        {
            try
            {
                var reply = await ping.SendPingAsync(address, timeout, Payload, null, cancellationToken).ConfigureAwait(false);
                if (reply.Status == IPStatus.Success)
                {
                    received++;
                    total += reply.RoundtripTime;
                }
            }
            catch (PingException)
            {
                // No route or similar. Counts as no reply.
            }
        }

        return new PingResult(count, received, received == 0 ? null : total / received);
    }
}
