using System.Diagnostics;
using OmniKeyVault.Domain;

namespace OmniKeyVault.Application;

/// <summary>
/// v2.0: SSH Agent integration service for Windows.
/// Loads SSH private keys into the Windows OpenSSH ssh-agent service.
/// Requires the ssh-agent service to be running and the ssh-add utility to be available.
/// </summary>
[OmniKeyVaultService(NoLockRequired = true)]
public sealed class SshAgentService
{
    /// <summary>Checks if the OpenSSH ssh-agent service is running on Windows.</summary>
    public bool IsAgentRunning()
    {
        if (!OperatingSystem.IsWindows()) return false;
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "sc.exe",
                Arguments = "query sshagent",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                CreateNoWindow = true,
            };
            var proc = Process.Start(psi);
            if (proc == null) return false;
            proc.WaitForExit(3000);
            var output = proc.StandardOutput.ReadToEnd();
            return output.Contains("RUNNING", StringComparison.OrdinalIgnoreCase);
        }
        catch { return false; }
    }

    /// <summary>Adds a private key to the ssh-agent.</summary>
    public (bool Success, string Message) AddKey(string privateKeyPem, string? passphrase = null)
    {
        if (!OperatingSystem.IsWindows())
            return (false, "SSH Agent integration is only available on Windows");

        if (!IsAgentRunning())
            return (false, "ssh-agent 服务未运行。请以管理员身份运行: Start-Service ssh-agent");

        // Write the key to a temp file, add it, then delete
        var tempFile = Path.Combine(Path.GetTempPath(), $"okv_ssh_key_{Guid.NewGuid():N}");
        try
        {
            File.WriteAllText(tempFile, privateKeyPem);

            var psi = new ProcessStartInfo
            {
                FileName = "ssh-add.exe",
                Arguments = $"\"{tempFile}\"",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
            };

            // Set environment for passphrase if provided (ssh-add reads from stdin)
            var proc = Process.Start(psi);
            if (proc == null) return (false, "无法启动 ssh-add");

            if (!string.IsNullOrEmpty(passphrase))
            {
                proc.StandardInput.Write(passphrase + "\n");
                proc.StandardInput.Close();
            }

            proc.WaitForExit(10000);
            var stderr = proc.StandardError.ReadToEnd();
            var stdout = proc.StandardOutput.ReadToEnd();

            if (proc.ExitCode == 0)
                return (true, "密钥已成功加载到 ssh-agent");
            return (false, $"ssh-add 失败 (exit {proc.ExitCode}): {stderr.Trim()}");
        }
        catch (Exception ex)
        {
            return (false, $"加载密钥失败: {ex.Message}");
        }
        finally
        {
            try { if (File.Exists(tempFile)) File.Delete(tempFile); } catch { }
        }
    }

    /// <summary>Lists keys currently loaded in the ssh-agent.</summary>
    public (bool Success, string Output) ListKeys()
    {
        if (!OperatingSystem.IsWindows())
            return (false, "SSH Agent integration is only available on Windows");

        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "ssh-add.exe",
                Arguments = "-l",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
            };
            var proc = Process.Start(psi);
            if (proc == null) return (false, "无法启动 ssh-add");
            proc.WaitForExit(5000);
            var output = proc.StandardOutput.ReadToEnd();
            if (proc.ExitCode == 0)
                return (true, output.Trim());
            var err = proc.StandardError.ReadToEnd().Trim();
            return (false, string.IsNullOrEmpty(err) ? "没有加载的密钥" : err);
        }
        catch (Exception ex)
        {
            return (false, $"执行失败: {ex.Message}");
        }
    }

    /// <summary>Removes all keys from the ssh-agent.</summary>
    public (bool Success, string Message) RemoveAllKeys()
    {
        if (!OperatingSystem.IsWindows())
            return (false, "SSH Agent integration is only available on Windows");

        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "ssh-add.exe",
                Arguments = "-D",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
            };
            var proc = Process.Start(psi);
            if (proc == null) return (false, "无法启动 ssh-add");
            proc.WaitForExit(5000);
            if (proc.ExitCode == 0)
                return (true, "已从 ssh-agent 移除所有密钥");
            return (false, "移除失败");
        }
        catch (Exception ex)
        {
            return (false, $"执行失败: {ex.Message}");
        }
    }
}
