using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using OmniKeyVault.Application;

namespace OmniKeyVault.Cli.Gui.Views;

/// <summary>
/// v0.2 S4-T8: device trust prompt. Shown when the SyncService encounters
/// a signature from an unknown device (i.e. a public key not previously
/// recorded as trusted). The user can:
///   1. Trust (record the device's public key in the trusted list)
///   2. Reject (treat the signature as invalid; sync fails)
///   3. Trust once (apply this change but don't persist)
///
/// Emits <see cref="Trusted"/> with the chosen action so the host can
/// continue the sync flow.
/// </summary>
public partial class DeviceTrustDialog : Window
{
    public enum TrustAction { Trust, TrustOnce, Reject }

    public event EventHandler<TrustAction>? Trusted;

    private readonly string _deviceId;
    private readonly string _publicKeyHex;

    public DeviceTrustDialog(string deviceId, byte[] publicKey, string reason)
    {
        InitializeComponent();
        _deviceId = deviceId;
        _publicKeyHex = publicKey.Length > 0
            ? Convert.ToHexString(publicKey)[..Math.Min(16, publicKey.Length * 2)]
            : "(空)";
        DeviceIdText.Text = deviceId;
        PublicKeyText.Text = $"{_publicKeyHex}…(共 {publicKey.Length} 字节)";
        ReasonText.Text = reason;
    }

    private void OnTrust(object? sender, RoutedEventArgs e)
    {
        Trusted?.Invoke(this, TrustAction.Trust);
        Close();
    }

    private void OnTrustOnce(object? sender, RoutedEventArgs e)
    {
        Trusted?.Invoke(this, TrustAction.TrustOnce);
        Close();
    }

    private void OnReject(object? sender, RoutedEventArgs e)
    {
        Trusted?.Invoke(this, TrustAction.Reject);
        Close();
    }
}