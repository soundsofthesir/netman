namespace NetMan.Core;

public enum AdapterKind
{
    Ethernet,
    WiFi,
}

public sealed record AdapterInfo(Guid Id, string Name, string Description, AdapterKind Kind, bool IsConnected);

public interface IAdapterReader
{
    /// <summary>Physical, USB and Wi-Fi adapters Windows currently lists. Disabled adapters are not included.</summary>
    IReadOnlyList<AdapterInfo> List();

    /// <summary>The adapter's live settings, or its stored settings when disconnected. Null when the adapter is not present.</summary>
    LiveConfig? Read(Guid adapterId);
}
