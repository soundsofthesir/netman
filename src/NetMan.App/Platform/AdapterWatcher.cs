using System.Net.NetworkInformation;

namespace NetMan.App.Platform;

/// <summary>
/// Raises <see cref="Changed"/> when Windows reports an address or availability change, and on a fixed interval
/// so changes Windows does not announce still show. Raised on a thread-pool thread.
/// </summary>
internal sealed class AdapterWatcher : IDisposable
{
    public static readonly TimeSpan DefaultInterval = TimeSpan.FromSeconds(2);

    private readonly System.Threading.Timer _timer;

    public AdapterWatcher(TimeSpan interval)
    {
        NetworkChange.NetworkAddressChanged += OnAddressChanged;
        NetworkChange.NetworkAvailabilityChanged += OnAvailabilityChanged;
        _timer = new System.Threading.Timer(_ => Raise(), null, interval, interval);
    }

    public event EventHandler? Changed;

    public void Dispose()
    {
        NetworkChange.NetworkAddressChanged -= OnAddressChanged;
        NetworkChange.NetworkAvailabilityChanged -= OnAvailabilityChanged;
        _timer.Dispose();
    }

    private void OnAddressChanged(object? sender, EventArgs e) => Raise();

    private void OnAvailabilityChanged(object? sender, NetworkAvailabilityEventArgs e) => Raise();

    private void Raise() => Changed?.Invoke(this, EventArgs.Empty);
}
