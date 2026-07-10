namespace OmniKeyVault.Tests;

/// <summary> xUnit fixture: provides a unique temp directory per test class. All files
/// created under the directory are auto-deleted at the end of the class. </summary>
public sealed class TempVaultDir : IDisposable
{
    public string Root { get; }

    public TempVaultDir()
    {
        Root = Path.Combine(
            Path.GetTempPath(),
            "okv-tests-" + Guid.NewGuid().ToString("N").Substring(0, 8));
        Directory.CreateDirectory(Root);
    }

    public string VaultPath(string relative, bool createIfMissing = false)
    {
        var p = System.IO.Path.Combine(Root, relative);
        if (createIfMissing)
        {
            // If the relative path ends with a separator or has no extension,
            // treat it as a directory and create it directly. Otherwise, create
            // the parent directory of the file.
            if (string.IsNullOrEmpty(System.IO.Path.GetExtension(p)) || relative.EndsWith(System.IO.Path.DirectorySeparatorChar) || relative.EndsWith(System.IO.Path.AltDirectorySeparatorChar))
                Directory.CreateDirectory(p);
            else
                Directory.CreateDirectory(System.IO.Path.GetDirectoryName(p)!);
        }
        return p;
    }

    /// <summary>Returns a unique random path under Root. Useful when tests need isolated files.</summary>
    public string RandomPath(string extension = "okv")
    {
        return System.IO.Path.Combine(Root, $"{Guid.NewGuid():N}.{extension}");
    }

    public void Dispose()
    {
        try { Directory.Delete(Root, recursive: true); } catch { }
    }
}
