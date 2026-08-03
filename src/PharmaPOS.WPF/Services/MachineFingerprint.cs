using System.Security.Cryptography;
using System.Text;
using Microsoft.Win32;

namespace PharmaPOS.WPF.Services;

/// <summary>Builds a stable, non-secret machine fingerprint for license binding.</summary>
public static class MachineFingerprint
{
    public static string GetMachineId()
    {
        var raw = string.Join("|",
            ReadMachineGuid(),
            Environment.MachineName.ToUpperInvariant());

        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(raw));
        return Convert.ToHexString(hash)[..32];
    }

    private static string ReadMachineGuid()
    {
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Cryptography");
            var value = key?.GetValue("MachineGuid") as string;
            if (!string.IsNullOrWhiteSpace(value))
                return value.Trim().ToUpperInvariant();
        }
        catch
        {
            // ignored
        }

        return "NO-GUID";
    }
}
