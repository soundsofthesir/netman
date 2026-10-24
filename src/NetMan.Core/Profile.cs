namespace NetMan.Core;

public enum AddressMode
{
    Dhcp,
    Static,
}

/// <summary>A saved adapter configuration. Address fields hold the text the user entered.</summary>
public sealed record Profile
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public string Name { get; init; } = "";
    public string Notes { get; init; } = "";
    public Guid AdapterId { get; init; }
    public string AdapterName { get; init; } = "";
    public AddressMode Mode { get; init; } = AddressMode.Dhcp;
    public string Ip { get; init; } = "";
    public string Mask { get; init; } = "";
    public string Gateway { get; init; } = "";
    public string Dns1 { get; init; } = "";
    public string Dns2 { get; init; } = "";
    public string PingTarget { get; init; } = "";
}
