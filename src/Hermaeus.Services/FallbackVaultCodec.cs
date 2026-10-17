using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;

namespace Hermaeus.Services;

internal enum FallbackVaultFormat { LegacyPlaintext, LegacyV2, V3, UnsupportedVersion }

// Format machinery only. Production activation is owned by SecretStore, not this codec.
internal static class FallbackVaultCodec
{
    private const int HeaderBytes = 44;
    private const string Prefix = "v3:";
    private const string Unavailable = "Fallback vault data is unavailable.";
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);
    private static readonly byte[] KdfDomain = Encoding.ASCII.GetBytes("Hermaeus.SecretStore.v3\0");

    internal static FallbackVaultFormat Classify(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        if (value.StartsWith("v3:", StringComparison.OrdinalIgnoreCase)) return FallbackVaultFormat.V3;
        if (value.StartsWith("v2:", StringComparison.OrdinalIgnoreCase)) return FallbackVaultFormat.LegacyV2;
        if (value.Length > 2 && value[0] is 'v' or 'V')
        {
            var index = 1;
            while (index < value.Length && value[index] is >= '0' and <= '9') index++;
            if (index > 1 && index < value.Length && value[index] == ':')
                return FallbackVaultFormat.UnsupportedVersion;
        }
        return FallbackVaultFormat.LegacyPlaintext;
    }

    internal static string NormalizeName(string? name) => string.IsNullOrWhiteSpace(name) ? "default" : name.Trim();

    internal static void ValidateNormalizedName(string name)
    {
        if (name is null || !string.Equals(name, NormalizeName(name), StringComparison.Ordinal))
            throw new ArgumentException("A normalised fallback vault name is required.");
        try { _ = StrictUtf8.GetByteCount(name); }
        catch (EncoderFallbackException) { throw new ArgumentException("A valid Unicode fallback vault name is required."); }
    }

    internal static byte[] BuildValueAdditionalData(string name)
    {
        ValidateNormalizedName(name);
        var encoded = StrictUtf8.GetBytes(name);
        var domain = "Hermaeus.SecretStore\0v3\0value\0"u8;
        var result = new byte[checked(domain.Length + sizeof(uint) + encoded.Length)];
        domain.CopyTo(result);
        BinaryPrimitives.WriteUInt32LittleEndian(result.AsSpan(domain.Length), checked((uint)encoded.Length));
        encoded.CopyTo(result.AsSpan(domain.Length + sizeof(uint)));
        return result;
    }

    internal static string EncryptValue(string name, string secret, ReadOnlySpan<byte> key)
    {
        byte[] plaintext;
        try { plaintext = StrictUtf8.GetBytes(secret); }
        catch (EncoderFallbackException) { throw new ArgumentException("A valid Unicode fallback secret is required."); }
        try { return Encrypt(plaintext, key, BuildValueAdditionalData(name)); }
        finally { CryptographicOperations.ZeroMemory(plaintext); }
    }

    internal static string DecryptValue(string name, string envelope, ReadOnlySpan<byte> key)
    {
        var plaintext = Decrypt(envelope, key, BuildValueAdditionalData(name));
        try
        {
            try { return StrictUtf8.GetString(plaintext); }
            catch (DecoderFallbackException) { throw new CryptographicException(Unavailable); }
        }
        finally { CryptographicOperations.ZeroMemory(plaintext); }
    }

    internal static string EncryptSnapshot(ReadOnlySpan<byte> json, ReadOnlySpan<byte> key, ReadOnlySpan<byte> originalVaultId) =>
        Encrypt(json, key, BuildRecoveryAdditionalData("Hermaeus.SecretStore\0v3\0snapshot\0"u8, originalVaultId));

    internal static byte[] DecryptSnapshot(string envelope, ReadOnlySpan<byte> key, ReadOnlySpan<byte> originalVaultId) =>
        Decrypt(envelope, key, BuildRecoveryAdditionalData("Hermaeus.SecretStore\0v3\0snapshot\0"u8, originalVaultId));

    internal static string EncryptLocator(ReadOnlySpan<byte> snapshotDigest, ReadOnlySpan<byte> key, ReadOnlySpan<byte> originalVaultId)
    {
        if (snapshotDigest.Length != 32) throw new ArgumentException("A 32-byte snapshot digest is required.");
        return Encrypt(snapshotDigest, key, BuildRecoveryAdditionalData("Hermaeus.SecretStore\0v3\0locator\0"u8, originalVaultId));
    }

    internal static byte[] DecryptLocator(string envelope, ReadOnlySpan<byte> key, ReadOnlySpan<byte> originalVaultId)
    {
        // Fixed size is checked before decoding or allocating an attacker-controlled envelope.
        if (envelope.Length != 107) throw new CryptographicException(Unavailable);
        var plaintext = Decrypt(envelope, key, BuildRecoveryAdditionalData("Hermaeus.SecretStore\0v3\0locator\0"u8, originalVaultId));
        if (plaintext.Length == 32) return plaintext;
        CryptographicOperations.ZeroMemory(plaintext);
        throw new CryptographicException(Unavailable);
    }

    private static byte[] BuildRecoveryAdditionalData(ReadOnlySpan<byte> domain, ReadOnlySpan<byte> originalVaultId)
    {
        if (originalVaultId.Length != 32) throw new ArgumentException("A 32-byte original vault identity is required.");
        var result = new byte[domain.Length + 32];
        domain.CopyTo(result);
        originalVaultId.CopyTo(result.AsSpan(domain.Length));
        return result;
    }

    private static string Encrypt(ReadOnlySpan<byte> plaintext, ReadOnlySpan<byte> key, ReadOnlySpan<byte> aad)
    {
        ValidateKey(key);
        var envelope = new byte[checked(HeaderBytes + plaintext.Length)];
        RandomNumberGenerator.Fill(envelope.AsSpan(0, 28));
        var derived = Derive(key, envelope.AsSpan(0, 16));
        try
        {
            using var aes = new AesGcm(derived, 16);
            aes.Encrypt(envelope.AsSpan(16, 12), plaintext, envelope.AsSpan(HeaderBytes), envelope.AsSpan(28, 16), aad);
            return Prefix + Convert.ToBase64String(envelope);
        }
        catch (CryptographicException) { throw new CryptographicException(Unavailable); }
        catch (PlatformNotSupportedException) { throw new CryptographicException(Unavailable); }
        finally { CryptographicOperations.ZeroMemory(derived); }
    }

    private static byte[] Decrypt(string encoded, ReadOnlySpan<byte> key, ReadOnlySpan<byte> aad)
    {
        ValidateKey(key);
        if (Classify(encoded) != FallbackVaultFormat.V3) throw new CryptographicException(Unavailable);
        byte[] envelope;
        try { envelope = Convert.FromBase64String(encoded[Prefix.Length..]); }
        catch (FormatException) { throw new CryptographicException(Unavailable); }
        if (envelope.Length < HeaderBytes
            || !string.Equals(Convert.ToBase64String(envelope), encoded[Prefix.Length..], StringComparison.Ordinal))
            throw new CryptographicException(Unavailable);
        var plaintext = new byte[envelope.Length - HeaderBytes];
        var derived = Derive(key, envelope.AsSpan(0, 16));
        try
        {
            using var aes = new AesGcm(derived, 16);
            aes.Decrypt(envelope.AsSpan(16, 12), envelope.AsSpan(HeaderBytes), envelope.AsSpan(28, 16), plaintext, aad);
            return plaintext;
        }
        catch (CryptographicException)
        {
            CryptographicOperations.ZeroMemory(plaintext);
            throw new CryptographicException(Unavailable);
        }
        catch (PlatformNotSupportedException)
        {
            CryptographicOperations.ZeroMemory(plaintext);
            throw new CryptographicException(Unavailable);
        }
        finally { CryptographicOperations.ZeroMemory(derived); }
    }

    private static byte[] Derive(ReadOnlySpan<byte> key, ReadOnlySpan<byte> salt)
    {
        var domainSalt = new byte[KdfDomain.Length + 16];
        KdfDomain.CopyTo(domainSalt, 0);
        salt.CopyTo(domainSalt.AsSpan(KdfDomain.Length));
        return Rfc2898DeriveBytes.Pbkdf2(key, domainSalt, 100_000, HashAlgorithmName.SHA256, 32);
    }

    private static void ValidateKey(ReadOnlySpan<byte> key)
    {
        if (key.Length != 32) throw new ArgumentException("A 32-byte protected vault key is required.");
    }
}
