using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace PharmaPOS.WPF.Services;

/// <summary>
/// AES-GCM protected store identity. Tampering or editing fields without the app key fails decryption.
/// Machine binding is enforced by comparing the decrypted machineId to this PC.
/// </summary>
internal static class ProtectedStoreIdentityFile
{
    private const string Magic = "PPID1";
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true
    };

    // App-side secret (not user-editable). Raises the bar vs plaintext JSON.
    // Combined with machineId inside the ciphertext so a copied file won't unlock another PC.
    private static readonly byte[] KeyMaterial = SHA256.HashData(Encoding.UTF8.GetBytes(
        "PharmaPOS.StoreIdentity.v1|CloudPharma|7f3c9a2e-b8d1-4e55-9c01-a6d4f0e28b17|DoNotDistribute"));

    public static void Write(string path, StoreIdentitySettings settings)
    {
        var plain = JsonSerializer.SerializeToUtf8Bytes(settings, JsonOptions);
        var nonce = RandomNumberGenerator.GetBytes(12);
        var cipher = new byte[plain.Length];
        var tag = new byte[16];
        using var aes = new AesGcm(KeyMaterial, 16);
        aes.Encrypt(nonce, plain, cipher, tag);

        using var fs = File.Create(path);
        var magic = Encoding.ASCII.GetBytes(Magic);
        fs.Write(magic);
        fs.Write(nonce);
        fs.Write(tag);
        fs.Write(cipher);
    }

    public static bool TryRead(string path, out StoreIdentitySettings? settings)
    {
        settings = null;
        if (!File.Exists(path))
            return false;

        try
        {
            var data = File.ReadAllBytes(path);
            var magicBytes = Encoding.ASCII.GetBytes(Magic);
            if (data.Length < magicBytes.Length + 12 + 16 + 1)
                return false;
            if (!data.AsSpan(0, magicBytes.Length).SequenceEqual(magicBytes))
                return false;

            var nonce = data.AsSpan(magicBytes.Length, 12);
            var tag = data.AsSpan(magicBytes.Length + 12, 16);
            var cipher = data.AsSpan(magicBytes.Length + 12 + 16);
            var plain = new byte[cipher.Length];
            using var aes = new AesGcm(KeyMaterial, 16);
            aes.Decrypt(nonce, cipher, tag, plain);

            settings = JsonSerializer.Deserialize<StoreIdentitySettings>(plain, JsonOptions);
            return settings is not null
                   && (!string.IsNullOrWhiteSpace(settings.StoreId)
                       || !string.IsNullOrWhiteSpace(settings.StoreCode));
        }
        catch (CryptographicException)
        {
            return false;
        }
        catch
        {
            return false;
        }
    }
}
