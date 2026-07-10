using System.Runtime.Versioning;

namespace OmniKeyVault.Application;

/// <summary>
/// System-level events (session lock/unlock, system suspend/resume) that drive
/// the auto-lock policy per MANUAL §12.5 / ARCHITECTURE.md §2.2.5 / SECURITY.md §7.
/// Implemented via <c>Microsoft.Win32.SystemEvents</c> on Windows; the v1.x macOS
/// and Linux builds will subscribe to NSDistributedNotificationCenter / DBus via
/// the same interface (out of scope for v1).
/// </summary>
public interface ISystemEventProvider : IDisposable
{
    /// <summary>Fired when the workstation is locked (Win+L, screensaver, RDP
    /// session disconnect). Per MANUAL §12.5 this should immediately lock the
    /// vault.</summary>
    event EventHandler<SystemEventAtArgs>? SessionLocked;

    /// <summary>Fired when the workstation is unlocked.</summary>
    event EventHandler<SystemEventAtArgs>? SessionUnlocked;

    /// <summary>Fired when the system is about to suspend (sleep / hibernate).
    /// Per MANUAL §12.5 this should immediately lock the vault.</summary>
    event EventHandler<SystemEventAtArgs>? SystemSuspending;

    /// <summary>Fired when the system resumes from suspend.</summary>
    event EventHandler<SystemEventAtArgs>? SystemResumed;

    void Start();
    void Stop();
}

public sealed record SystemEventAtArgs(DateTimeOffset At);

/// <summary>No-op provider for non-Windows platforms or test environments where
/// SystemEvents is unavailable. Events never fire; <see cref="Start"/> is a no-op.</summary>
public sealed class NoOpSystemEventProvider : ISystemEventProvider
{
    public event EventHandler<SystemEventAtArgs>? SessionLocked { add { } remove { } }
    public event EventHandler<SystemEventAtArgs>? SessionUnlocked { add { } remove { } }
    public event EventHandler<SystemEventAtArgs>? SystemSuspending { add { } remove { } }
    public event EventHandler<SystemEventAtArgs>? SystemResumed { add { } remove { } }
    public void Start() { }
    public void Stop() { }
    public void Dispose() { }
}

