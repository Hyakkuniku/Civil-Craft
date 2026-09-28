using System;
using System.Security.Cryptography;
using System.Text;

/// <summary>
/// Authenticated encryption for player-controlled persistent files. The prefix
/// makes encrypted data unambiguous while still allowing a one-time migration
/// from saves created before encryption was introduced.
/// </summary>
public static class SaveEncryption
{
    private const string Prefix = "CCSAVE1:";

    // This protects against casual save editing. Like every client-side key it
    // can ultimately be recovered from a determined reverse-engineering attack.
    private const string EncodedApplicationSecret =
        "0fotiBH0THWhZxsitOonr+2umjr3PlOJX1fD8n2fwdQtdTTnzqj4hLtfvLVJ/OJy/xCpNYw2hhNECZyeXTz6KA==";

    private const int IvLength = 16;
    private const int AuthenticationTagLength = 32;

    public static bool IsEncrypted(string serialized)
    {
        return !string.IsNullOrEmpty(serialized) &&
               serialized.StartsWith(Prefix, StringComparison.Ordinal);
    }

    public static string Encrypt(string plaintext, string purpose)
    {
        if (plaintext == null) throw new ArgumentNullException(nameof(plaintext));
        ValidatePurpose(purpose);

        byte[] plaintextBytes = Encoding.UTF8.GetBytes(plaintext);
        byte[] iv = new byte[IvLength];
        using (RandomNumberGenerator random = RandomNumberGenerator.Create())
            random.GetBytes(iv);

        byte[] ciphertext;
        using (Aes aes = Aes.Create())
        {
            aes.KeySize = 256;
            aes.BlockSize = 128;
            aes.Mode = CipherMode.CBC;
            aes.Padding = PaddingMode.PKCS7;
            aes.Key = DeriveKey(purpose, "encryption");
            aes.IV = iv;
            using (ICryptoTransform encryptor = aes.CreateEncryptor())
                ciphertext = encryptor.TransformFinalBlock(plaintextBytes, 0, plaintextBytes.Length);
        }

        byte[] authenticatedBytes = Combine(iv, ciphertext);
        byte[] tag;
        using (HMACSHA256 hmac = new HMACSHA256(DeriveKey(purpose, "authentication")))
            tag = hmac.ComputeHash(authenticatedBytes);

        return Prefix + Convert.ToBase64String(Combine(authenticatedBytes, tag));
    }

    /// <summary>
    /// Decrypts current data or returns a legacy plaintext JSON document for
    /// migration. Non-JSON, non-encrypted input is rejected rather than being
    /// interpreted as a new empty PlayerData object by JsonUtility.
    /// </summary>
    public static bool TryDecryptOrReadLegacy(string serialized, string purpose,
        out string plaintext, out bool wasLegacyPlaintext, out string error)
    {
        plaintext = null;
        wasLegacyPlaintext = false;
        error = string.Empty;

        if (string.IsNullOrWhiteSpace(serialized))
        {
            error = "The save file is empty.";
            return false;
        }

        if (!IsEncrypted(serialized))
        {
            if (!serialized.TrimStart().StartsWith("{", StringComparison.Ordinal))
            {
                error = "The save file has an unknown format.";
                return false;
            }

            plaintext = serialized;
            wasLegacyPlaintext = true;
            return true;
        }

        ValidatePurpose(purpose);
        try
        {
            byte[] payload = Convert.FromBase64String(serialized.Substring(Prefix.Length));
            if (payload.Length <= IvLength + AuthenticationTagLength)
                throw new CryptographicException("The encrypted payload is incomplete.");

            int ciphertextLength = payload.Length - IvLength - AuthenticationTagLength;
            byte[] authenticatedBytes = new byte[IvLength + ciphertextLength];
            byte[] suppliedTag = new byte[AuthenticationTagLength];
            Buffer.BlockCopy(payload, 0, authenticatedBytes, 0, authenticatedBytes.Length);
            Buffer.BlockCopy(payload, authenticatedBytes.Length, suppliedTag, 0, suppliedTag.Length);

            byte[] expectedTag;
            using (HMACSHA256 hmac = new HMACSHA256(DeriveKey(purpose, "authentication")))
                expectedTag = hmac.ComputeHash(authenticatedBytes);
            if (!ConstantTimeEquals(suppliedTag, expectedTag))
                throw new CryptographicException("The save authentication check failed.");

            byte[] iv = new byte[IvLength];
            byte[] ciphertext = new byte[ciphertextLength];
            Buffer.BlockCopy(authenticatedBytes, 0, iv, 0, iv.Length);
            Buffer.BlockCopy(authenticatedBytes, iv.Length, ciphertext, 0, ciphertext.Length);

            byte[] plaintextBytes;
            using (Aes aes = Aes.Create())
            {
                aes.KeySize = 256;
                aes.BlockSize = 128;
                aes.Mode = CipherMode.CBC;
                aes.Padding = PaddingMode.PKCS7;
                aes.Key = DeriveKey(purpose, "encryption");
                aes.IV = iv;
                using (ICryptoTransform decryptor = aes.CreateDecryptor())
                    plaintextBytes = decryptor.TransformFinalBlock(ciphertext, 0, ciphertext.Length);
            }

            plaintext = Encoding.UTF8.GetString(plaintextBytes);
            return true;
        }
        catch (Exception exception) when (exception is FormatException ||
                                          exception is CryptographicException ||
                                          exception is ArgumentException)
        {
            error = exception.Message;
            return false;
        }
    }

    private static byte[] DeriveKey(string purpose, string use)
    {
        byte[] secret = Convert.FromBase64String(EncodedApplicationSecret);
        byte[] input = Encoding.UTF8.GetBytes("CivilCraft|save-v1|" + purpose + "|" + use);
        using (HMACSHA256 hmac = new HMACSHA256(secret))
            return hmac.ComputeHash(input);
    }

    private static void ValidatePurpose(string purpose)
    {
        if (string.IsNullOrWhiteSpace(purpose))
            throw new ArgumentException("An encryption purpose is required.", nameof(purpose));
    }

    private static byte[] Combine(byte[] left, byte[] right)
    {
        byte[] combined = new byte[left.Length + right.Length];
        Buffer.BlockCopy(left, 0, combined, 0, left.Length);
        Buffer.BlockCopy(right, 0, combined, left.Length, right.Length);
        return combined;
    }

    private static bool ConstantTimeEquals(byte[] left, byte[] right)
    {
        if (left == null || right == null || left.Length != right.Length) return false;
        int difference = 0;
        for (int i = 0; i < left.Length; i++) difference |= left[i] ^ right[i];
        return difference == 0;
    }
}
