namespace NetMan.Core;

/// <summary>Network adapter Characteristics flags from the adapter's device class registry key.</summary>
public static class AdapterFilter
{
    public const int Virtual = 0x1;
    public const int Physical = 0x4;
    public const int Hidden = 0x8;

    /// <summary>Physical adapters, USB ones included, that Windows does not hide.</summary>
    public static bool IsPhysical(int characteristics) =>
        (characteristics & Physical) != 0 && (characteristics & Hidden) == 0;
}
