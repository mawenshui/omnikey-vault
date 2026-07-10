using System.Runtime.Versioning;
using OmniKeyVault.Application;

namespace OmniKeyVault.Cli.Gui;

/// <summary>Windows implementation of <see cref="ISystemEventProvider"/> backed
/// by <c>Microsoft.Win32.SystemEvents</c> (SessionSwitch + PowerModeChanged).
/// Lives in the Cli project because the <c>Microsoft.Win32</c> types are
/// only available with the <c>Microsoft.Win32.SystemEvents</c> package +
/// the <c>net8.0-windows</c> TFM, which would force every consumer of the
/// Application project to also be Windows-only. By keeping this class in the
/// GUI host project we keep the Application layer cross-platform
/// (tests use <see cref="NoOpSystemEventProvider"/>).</summary>
[SupportedOSPlatform("windows")]
public sealed class WindowsSystemEventProvider : ISystemEventProvider
{
    private Microsoft.Win32.SessionSwitchEventHandler? _switchHandler;
    private Microsoft.Win32.PowerModeChangedEventHandler? _powerHandler;
    private bool _started;

    public event EventHandler<SystemEventAtArgs>? SessionLocked;
    public event EventHandler<SystemEventAtArgs>? SessionUnlocked;
    public event EventHandler<SystemEventAtArgs>? SystemSuspending;
    public event EventHandler<SystemEventAtArgs>? SystemResumed;

    public void Start()
    {
        if (_started) return;
        _switchHandler = OnSessionSwitch;
        _powerHandler = OnPowerModeChanged;
        Microsoft.Win32.SystemEvents.SessionSwitch += _switchHandler;
        Microsoft.Win32.SystemEvents.PowerModeChanged += _powerHandler;
        _started = true;
    }

    public void Stop()
    {
        if (!_started) return;
        if (_switchHandler != null) Microsoft.Win32.SystemEvents.SessionSwitch -= _switchHandler;
        if (_powerHandler != null) Microsoft.Win32.SystemEvents.PowerModeChanged -= _powerHandler;
        _switchHandler = null;
        _powerHandler = null;
        _started = false;
    }

    private void OnSessionSwitch(object sender, Microsoft.Win32.SessionSwitchEventArgs e)
    {
        var at = DateTimeOffset.Now;
        switch (e.Reason)
        {
            case Microsoft.Win32.SessionSwitchReason.SessionLock:
                SessionLocked?.Invoke(this, new SystemEventAtArgs(at));
                break;
            case Microsoft.Win32.SessionSwitchReason.SessionUnlock:
                SessionUnlocked?.Invoke(this, new SystemEventAtArgs(at));
                break;
        }
    }

    private void OnPowerModeChanged(object sender, Microsoft.Win32.PowerModeChangedEventArgs e)
    {
        var at = DateTimeOffset.Now;
        switch (e.Mode)
        {
            case Microsoft.Win32.PowerModes.Suspend:
                SystemSuspending?.Invoke(this, new SystemEventAtArgs(at));
                break;
            case Microsoft.Win32.PowerModes.Resume:
                SystemResumed?.Invoke(this, new SystemEventAtArgs(at));
                break;
        }
    }

    public void Dispose() => Stop();
}
