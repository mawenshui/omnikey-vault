using System.Runtime.InteropServices;
using System.Text;

namespace OmniKeyVault.Application;

/// <summary>
/// v2.6.0: Windows Credential Manager integration for secure credential storage.
/// Allows storing vault metadata and encrypted credentials in the system credential store.
/// 
/// Key features:
/// - Vault credentials can be stored in Windows Credential Manager
/// - Managed by Windows OS with proper access control
/// - Different from DPAPI: stored in system credential vault, not user-scoped encrypted files
/// - Can be accessed by other authorized applications (e.g., automation scripts)
/// - Each vault has its own credential entry with a unique target name
/// 
/// Security model:
/// - Credentials are stored in Windows Credential Manager
/// - Access is controlled by Windows security (ACLs)
/// - Requires user consent to write credentials
/// - Credential data is encrypted by Windows OS
/// </summary>
public static class CredentialManagerService
{
    #region P/Invoke declarations for advapi32.dll

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct CREDENTIAL
    {
        public int Flags;
        public int Type;
        public string TargetName;
        public string Comment;
        public System.Runtime.InteropServices.ComTypes.FILETIME LastWritten;
        public int CredentialBlobSize;
        public IntPtr CredentialBlob;
        public int Persist;
        public int AttributeCount;
        public IntPtr Attributes;
        public string TargetAlias;
        public string UserName;
    }

    // Credential types
    private const int CRED_TYPE_GENERIC = 1;
    private const int CRED_TYPE_DOMAIN_PASSWORD = 2;

    // Persistence modes
    private const int CRED_PERSIST_SESSION = 1;      // Valid only for logon session
    private const int CRED_PERSIST_LOCAL_MACHINE = 2; // Persists across reboots
    private const int CRED_PERSIST_ENTERPRISE = 3;    // Persists across domain logons

    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool CredWrite([In] ref CREDENTIAL Credential, [In] uint Flags);

    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool CredRead([In] string TargetName, [In] int Type, [In] uint Flags, out IntPtr CredentialPtr);

    [DllImport("advapi32.dll", SetLastError = true)]
    private static extern bool CredDelete([In] string TargetName, [In] int Type, [In] uint Flags);

    [DllImport("advapi32.dll", SetLastError = true)]
    private static extern bool CredFree([In] IntPtr Credential);

    #endregion

    #region Constants

    private const string CREDENTIAL_TARGET_PREFIX = "OmniKeyVault_v2_";

    #endregion

    #region Public API

    /// <summary>
    /// Gets the target name for storing a vault's credentials in Windows Credential Manager.
    /// Format: OmniKeyVault_v2_{vault_uuid}
    /// </summary>
    private static string GetTargetName(Guid vaultUuid) => 
        $"{CREDENTIAL_TARGET_PREFIX}{vaultUuid:D}";

    /// <summary>
    /// Stores vault metadata in Windows Credential Manager.
    /// The data is stored as a JSON string in the credential blob.
    /// </summary>
    /// <param name="vaultUuid">The vault UUID</param>
    /// <param name="vaultPath">The vault file path (for identification)</param>
    /// <param name="metadata">Additional metadata to store</param>
    /// <returns>True if the credential was successfully stored</returns>
    public static bool StoreVaultCredential(Guid vaultUuid, string vaultPath, Dictionary<string, string>? metadata = null)
    {
        try
        {
            var targetName = GetTargetName(vaultUuid);
            
            // Build the credential data
            var credentialData = new CredentialData
            {
                VaultUuid = vaultUuid,
                VaultPath = vaultPath,
                StoredAt = DateTimeOffset.UtcNow,
                Metadata = metadata ?? new Dictionary<string, string>()
            };
            
            var jsonData = System.Text.Json.JsonSerializer.Serialize(credentialData);
            var credentialBytes = Encoding.UTF8.GetBytes(jsonData);
            
            var cred = new CREDENTIAL
            {
                Flags = 0,
                Type = CRED_TYPE_GENERIC,
                TargetName = targetName,
                Comment = "OmniKey Vault - Encrypted vault metadata",
                CredentialBlobSize = credentialBytes.Length,
                CredentialBlob = Marshal.AllocHGlobal(credentialBytes.Length),
                Persist = CRED_PERSIST_LOCAL_MACHINE,
                UserName = Environment.UserName
            };
            
            // Copy data to unmanaged memory
            Marshal.Copy(credentialBytes, 0, cred.CredentialBlob, credentialBytes.Length);
            
            try
            {
                return CredWrite(ref cred, 0);
            }
            finally
            {
                // Free unmanaged memory
                if (cred.CredentialBlob != IntPtr.Zero)
                    Marshal.FreeHGlobal(cred.CredentialBlob);
            }
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Retrieves vault metadata from Windows Credential Manager.
    /// </summary>
    /// <param name="vaultUuid">The vault UUID</param>
    /// <returns>The credential data if found, null otherwise</returns>
    public static CredentialData? RetrieveVaultCredential(Guid vaultUuid)
    {
        try
        {
            var targetName = GetTargetName(vaultUuid);
            
            if (!CredRead(targetName, CRED_TYPE_GENERIC, 0, out IntPtr credPtr))
                return null;
            
            try
            {
                var cred = Marshal.PtrToStructure<CREDENTIAL>(credPtr);
                
                // Read the credential blob
                var credentialBytes = new byte[cred.CredentialBlobSize];
                Marshal.Copy(cred.CredentialBlob, credentialBytes, 0, cred.CredentialBlobSize);
                
                // Deserialize JSON
                var jsonData = Encoding.UTF8.GetString(credentialBytes);
                var credentialData = System.Text.Json.JsonSerializer.Deserialize<CredentialData>(jsonData);
                return credentialData;
            }
            finally
            {
                CredFree(credPtr);
            }
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Deletes a vault credential from Windows Credential Manager.
    /// </summary>
    /// <param name="vaultUuid">The vault UUID</param>
    /// <returns>True if the credential was deleted, false if it didn't exist</returns>
    public static bool DeleteVaultCredential(Guid vaultUuid)
    {
        try
        {
            var targetName = GetTargetName(vaultUuid);
            return CredDelete(targetName, CRED_TYPE_GENERIC, 0);
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Checks if a vault credential exists in Windows Credential Manager.
    /// </summary>
    /// <param name="vaultUuid">The vault UUID</param>
    /// <returns>True if the credential exists</returns>
    public static bool VaultCredentialExists(Guid vaultUuid)
    {
        return RetrieveVaultCredential(vaultUuid) != null;
    }

    /// <summary>
    /// Lists all vault UUIDs that have credentials stored in Windows Credential Manager.
    /// </summary>
    /// <returns>A list of vault UUIDs</returns>
    public static List<Guid> ListAllVaultCredentials()
    {
        // Note: This would require CredEnumerate, which is more complex.
        // For now, return empty list - users need to know the vault UUID to retrieve.
        return new List<Guid>();
    }

    #endregion

    #region Internal types

    /// <summary>
    /// Data structure for vault credentials stored in Windows Credential Manager.
    /// </summary>
    public class CredentialData
    {
        public Guid VaultUuid { get; set; }
        public string VaultPath { get; set; } = string.Empty;
        public DateTimeOffset StoredAt { get; set; }
        public Dictionary<string, string> Metadata { get; set; } = new();
    }

    #endregion
}