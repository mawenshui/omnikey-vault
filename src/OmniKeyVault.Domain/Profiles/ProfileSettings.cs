namespace OmniKeyVault.Domain;

/// <summary>
/// Per-profile settings per OKV_FORMAT.md §3.3.
/// dev/test profiles default to ParticipateInSync=false to enforce
/// production data isolation (PRD §5.5.1).
/// </summary>
public sealed record ProfileSettings
{
    public bool ParticipateInSync { get; init; }
    public bool AutoLockOnSwitch { get; init; }
    public int IdleLockMinutes { get; init; } = 15;

    public static ProfileSettings DefaultProd() => new()
    {
        ParticipateInSync = true,
        AutoLockOnSwitch = false,
        IdleLockMinutes = 15
    };

    public static ProfileSettings DefaultDev() => new()
    {
        ParticipateInSync = false,
        AutoLockOnSwitch = true,
        IdleLockMinutes = 5
    };
}
