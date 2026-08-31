using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace Noty.Core;

/// AES-GCM over note bodies. Titles, colours and dates stay in plaintext so lists
/// render without unsealing every row.
///
/// The 256-bit key sits in a file beside the database, itself wrapped with DPAPI
/// for the current user — the Windows counterpart of the Keychain, and stronger
/// than the 0600 file the macOS build settles for.
public static class Crypto
{
    private const int NonceSize = 12;   // AesGcm.NonceByteSizes.MaxSize
    private const int TagSize = 16;     // AesGcm.TagByteSizes.MaxSize

    private static readonly byte[] KeyBytes = LoadOrCreateKey();

    private static byte[] LoadOrCreateKey()
    {
        try
        {
            if (File.Exists(Paths.Key))
            {
                var stored = File.ReadAllBytes(Paths.Key);
                var plain = ProtectedData.Unprotect(stored, null, DataProtectionScope.CurrentUser);
                if (plain.Length == 32) return plain;
            }
        }
        catch (Exception e)
        {
            Log.Line($"key unwrap failed — {e.Message}");
        }

        var key = RandomNumberGenerator.GetBytes(32);
        try
        {
            File.WriteAllBytes(Paths.Key,
                ProtectedData.Protect(key, null, DataProtectionScope.CurrentUser));
        }
        catch (Exception e)
        {
            Log.Line($"key write failed — {e.Message}");
        }
        return key;
    }

    /// nonce ‖ ciphertext ‖ tag, so one blob round-trips through SQLite.
    public static byte[] Seal(string text)
    {
        if (string.IsNullOrEmpty(text)) text = "";
        var plain = Encoding.UTF8.GetBytes(text);
        var nonce = RandomNumberGenerator.GetBytes(NonceSize);
        var cipher = new byte[plain.Length];
        var tag = new byte[TagSize];

        using var gcm = new AesGcm(KeyBytes, TagSize);
        gcm.Encrypt(nonce, plain, cipher, tag);

        var combined = new byte[NonceSize + cipher.Length + TagSize];
        Buffer.BlockCopy(nonce, 0, combined, 0, NonceSize);
        Buffer.BlockCopy(cipher, 0, combined, NonceSize, cipher.Length);
        Buffer.BlockCopy(tag, 0, combined, NonceSize + cipher.Length, TagSize);
        return combined;
    }

    public static string Open(byte[]? data)
    {
        if (data is null || data.Length < NonceSize + TagSize) return "";
        try
        {
            var nonce = new byte[NonceSize];
            var cipher = new byte[data.Length - NonceSize - TagSize];
            var tag = new byte[TagSize];
            Buffer.BlockCopy(data, 0, nonce, 0, NonceSize);
            Buffer.BlockCopy(data, NonceSize, cipher, 0, cipher.Length);
            Buffer.BlockCopy(data, NonceSize + cipher.Length, tag, 0, TagSize);

            var plain = new byte[cipher.Length];
            using var gcm = new AesGcm(KeyBytes, TagSize);
            gcm.Decrypt(nonce, cipher, tag, plain);
            return Encoding.UTF8.GetString(plain);
        }
        catch (Exception e)
        {
            Log.Line($"unseal failed — {e.Message}");
            return "";
        }
    }
}
