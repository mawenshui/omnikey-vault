using System.Runtime.InteropServices;
using System.Text;

namespace OmniKeyVault.Infrastructure;

/// <summary>
/// v2.3.5: Direct Win32 clipboard helper that bypasses Avalonia's OLE-based
/// clipboard implementation. The OLE clipboard API requires the calling thread
/// to have called OleInitialize (COM initialization), which is not always
/// guaranteed — especially when clipboard operations are triggered from
/// background threads (e.g. BrowserExtensionApiService's HttpListener thread).
///
/// This helper uses the basic Win32 clipboard API (OpenClipboard /
/// SetClipboardData / CloseClipboard) which does NOT require COM initialization.
/// </summary>
public static class Win32Clipboard
{
    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool OpenClipboard(IntPtr hWndNewOwner);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CloseClipboard();

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool EmptyClipboard();

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr SetClipboardData(uint uFormat, IntPtr hMem);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr GlobalAlloc(uint uFlags, nuint dwBytes);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr GlobalLock(IntPtr hMem);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GlobalUnlock(IntPtr hMem);

    [DllImport("ole32.dll")]
    private static extern int OleInitialize(IntPtr pvReserved);

    private const uint CF_UNICODETEXT = 13;
    private const uint GMEM_MOVEABLE = 0x0002;

    private static int _oleInitialized;

    /// <summary>
    /// Calls OleInitialize on the current thread. Safe to call multiple times.
    /// Called at app startup to ensure COM is initialized on the UI thread.
    /// </summary>
    public static void EnsureOleInitialized()
    {
        if (Interlocked.CompareExchange(ref _oleInitialized, 1, 0) == 0)
        {
            try { OleInitialize(IntPtr.Zero); }
            catch { /* best-effort — Win32 clipboard API works without OLE */ }
        }
    }

    /// <summary>
    /// Copies text to the system clipboard using direct Win32 API.
    /// Does NOT require COM/OleInitialize — uses OpenClipboard/SetClipboardData.
    /// Thread-safe: retries up to 10 times if another app holds the clipboard.
    /// </summary>
    public static void SetText(string text)
    {
        if (string.IsNullOrEmpty(text)) return;

        // Retry loop: OpenClipboard fails if another window has it open.
        for (int attempt = 0; attempt < 10; attempt++)
        {
            if (OpenClipboard(IntPtr.Zero))
            {
                try
                {
                    EmptyClipboard();

                    // Allocate global memory for the Unicode text (including null terminator)
                    var byteCount = (nuint)((text.Length + 1) * 2); // +1 for null terminator, *2 for UTF-16
                    var hGlobal = GlobalAlloc(GMEM_MOVEABLE, byteCount);
                    if (hGlobal == IntPtr.Zero) return;

                    var pLock = GlobalLock(hGlobal);
                    if (pLock == IntPtr.Zero) return;

                    try
                    {
                        // Copy text bytes + null terminator using Marshal
                        var bytes = Encoding.Unicode.GetBytes(text);
                        Marshal.Copy(bytes, 0, pLock, bytes.Length);
                        // Write null terminator (2 zero bytes)
                        Marshal.WriteInt16(pLock, bytes.Length, 0);
                    }
                    finally
                    {
                        GlobalUnlock(hGlobal);
                    }

                    // SetClipboardData takes ownership of the memory handle
                    SetClipboardData(CF_UNICODETEXT, hGlobal);
                }
                finally
                {
                    CloseClipboard();
                }
                return; // Success
            }

            // Clipboard is locked by another process — wait and retry
            System.Threading.Thread.Sleep(20);
        }
    }

    /// <summary>
    /// Clears the system clipboard using direct Win32 API.
    /// </summary>
    public static void Clear()
    {
        for (int attempt = 0; attempt < 5; attempt++)
        {
            if (OpenClipboard(IntPtr.Zero))
            {
                try
                {
                    EmptyClipboard();
                }
                finally
                {
                    CloseClipboard();
                }
                return;
            }
            System.Threading.Thread.Sleep(20);
        }
    }
}
