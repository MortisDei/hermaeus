using System.Security.Cryptography;
using System.Text;
using Hermaeus.Services;
using Xunit;

namespace Hermaeus.Tests;

public sealed class FallbackVaultCodecTests
{
    private static byte[] Key => Enumerable.Range(0, 32).Select(value => (byte)value).ToArray();
    private static byte[] VaultId => SHA256.HashData("synthetic-original-vault"u8);

    [Fact]
    public void Value_AAD_uses_exact_normalised_name_and_UTF8_byte_length()
    {
        var expected = Encoding.ASCII.GetBytes("Hermaeus.SecretStore\0v3\0value\0")
            .Concat(new byte[] { 6, 0, 0, 0, 0xc3, 0xa9, 0xf0, 0x9f, 0x94, 0x91 }).ToArray();
        Assert.Equal(expected, FallbackVaultCodec.BuildValueAdditionalData("é🔑"));
        Assert.Equal("é🔑", FallbackVaultCodec.NormalizeName("  é🔑  "));
        Assert.Equal("default", FallbackVaultCodec.NormalizeName(null));
        Assert.Equal("default", FallbackVaultCodec.NormalizeName(" \t"));
        Assert.NotEqual(FallbackVaultCodec.BuildValueAdditionalData("é"), FallbackVaultCodec.BuildValueAdditionalData("e\u0301"));
    }

    [Theory]
    [InlineData(" ")]
    [InlineData("")]
    [InlineData(" name")]
    [InlineData("name ")]
    public void Unnormalised_dictionary_keys_are_rejected(string name) =>
        Assert.Throws<ArgumentException>(() => FallbackVaultCodec.EncryptValue(name, "synthetic", Key));

    [Fact]
    public void Invalid_UTF16_names_and_secrets_are_rejected_without_replacement()
    {
        Assert.Throws<ArgumentException>(() => FallbackVaultCodec.BuildValueAdditionalData("name\ud800"));
        Assert.Throws<ArgumentException>(() => FallbackVaultCodec.EncryptValue("name", "synthetic\udc00", Key));
    }

    [Fact]
    public void Fresh_salt_and_nonce_roundtrip_Unicode_without_modifying_caller_key()
    {
        var key = Key;
        var original = key.ToArray();
        var first = FallbackVaultCodec.EncryptValue("é🔑", "synthetic-秘密-🔒", key);
        var second = FallbackVaultCodec.EncryptValue("é🔑", "synthetic-秘密-🔒", key);
        var firstBytes = Convert.FromBase64String(first[3..]);
        var secondBytes = Convert.FromBase64String(second[3..]);
        Assert.StartsWith("v3:", first);
        Assert.NotEqual(firstBytes[..16], secondBytes[..16]);
        Assert.NotEqual(firstBytes[16..28], secondBytes[16..28]);
        Assert.Equal("synthetic-秘密-🔒", FallbackVaultCodec.DecryptValue("é🔑", first, key));
        Assert.Equal("synthetic-秘密-🔒", FallbackVaultCodec.DecryptValue("é🔑", "V3:" + first[3..], key));
        Assert.Equal(original, key);
    }

    [Fact]
    public void Independent_fixed_envelope_uses_approved_layout_KDF_and_AAD()
    {
        var expected = CreateFixture("synthetic-fixture"u8.ToArray());
        Assert.Equal("synthetic-fixture", FallbackVaultCodec.DecryptValue("é🔑", expected, Key));
        Assert.Throws<CryptographicException>(() => FallbackVaultCodec.DecryptValue("É🔑", expected, Key));
        var wrongKey = Key;
        wrongKey[0] ^= 1;
        Assert.Throws<CryptographicException>(() => FallbackVaultCodec.DecryptValue("é🔑", expected, wrongKey));
    }

    [Fact]
    public void Authenticated_non_UTF8_value_is_unavailable()
    {
        var envelope = CreateFixture([0xc0, 0xaf]);
        Assert.Throws<CryptographicException>(() => FallbackVaultCodec.DecryptValue("é🔑", envelope, Key));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(15)]
    [InlineData(16)]
    [InlineData(27)]
    [InlineData(28)]
    [InlineData(43)]
    [InlineData(44)]
    public void Every_binary_field_rejects_tampering_without_plaintext_diagnostics(int offset)
    {
        var value = FallbackVaultCodec.EncryptValue("sensitive-name", "synthetic-secret", Key);
        var bytes = Convert.FromBase64String(value[3..]);
        bytes[offset] ^= 1;
        var exception = Assert.Throws<CryptographicException>(() =>
            FallbackVaultCodec.DecryptValue("sensitive-name", "v3:" + Convert.ToBase64String(bytes), Key));
        Assert.Equal("Fallback vault data is unavailable.", exception.Message);
        Assert.Null(exception.InnerException);
    }

    [Fact]
    public void Truncation_and_noncanonical_Base64_never_fall_through()
    {
        var value = FallbackVaultCodec.EncryptValue("name", "synthetic", Key);
        var bytes = Convert.FromBase64String(value[3..]);
        for (var length = 0; length < bytes.Length; length++)
            Assert.Throws<CryptographicException>(() => FallbackVaultCodec.DecryptValue("name",
                "v3:" + Convert.ToBase64String(bytes.AsSpan(0, length)), Key));
        foreach (var malformed in new[] { "v3:not base64!", value + "\n", "v3: " + value[3..] })
            Assert.Throws<CryptographicException>(() => FallbackVaultCodec.DecryptValue("name", malformed, Key));
    }

    [Theory]
    [InlineData("cGxhaW50ZXh0", "LegacyPlaintext")]
    [InlineData("v2:payload", "LegacyV2")]
    [InlineData("V2:payload", "LegacyV2")]
    [InlineData("v3:broken", "V3")]
    [InlineData("V3:broken", "V3")]
    [InlineData("v4:payload", "UnsupportedVersion")]
    [InlineData("V12345678901234567890:payload", "UnsupportedVersion")]
    [InlineData("v03:payload", "UnsupportedVersion")]
    [InlineData("v0:payload", "UnsupportedVersion")]
    public void Explicit_format_classification_does_not_offer_a_legacy_fallback(string value, string expected)
    {
        Assert.Equal(expected, FallbackVaultCodec.Classify(value).ToString());
        Assert.Throws<CryptographicException>(() => FallbackVaultCodec.DecryptValue("name", value, Key));
    }

    [Fact]
    public void Snapshot_and_locator_bind_original_identity_and_distinct_purposes()
    {
        var id = VaultId;
        var snapshot = FallbackVaultCodec.EncryptSnapshot("{\"name\":\"synthetic\"}"u8, Key, id);
        var digest = SHA256.HashData(Encoding.ASCII.GetBytes(snapshot));
        var locator = FallbackVaultCodec.EncryptLocator(digest, Key, id);
        Assert.Equal("{\"name\":\"synthetic\"}", Encoding.UTF8.GetString(FallbackVaultCodec.DecryptSnapshot(snapshot, Key, id)));
        Assert.Equal(107, locator.Length);
        Assert.Equal(digest, FallbackVaultCodec.DecryptLocator(locator, Key, id));
        var differentId = id.ToArray();
        differentId[0] ^= 1;
        Assert.Throws<CryptographicException>(() => FallbackVaultCodec.DecryptSnapshot(snapshot, Key, differentId));
        Assert.Throws<CryptographicException>(() => FallbackVaultCodec.DecryptLocator(locator, Key, differentId));
        Assert.Throws<CryptographicException>(() => FallbackVaultCodec.DecryptSnapshot(locator, Key, id));
        Assert.Throws<CryptographicException>(() => FallbackVaultCodec.DecryptValue("name", locator, Key));
        Assert.Throws<CryptographicException>(() => FallbackVaultCodec.DecryptLocator(snapshot, Key, id));
        Assert.Throws<CryptographicException>(() => FallbackVaultCodec.DecryptLocator(locator + "\n", Key, id));
    }

    [Fact]
    public void Fixed_size_key_identity_and_locator_digest_are_required()
    {
        Assert.Throws<ArgumentException>(() => FallbackVaultCodec.EncryptValue("name", "synthetic", new byte[31]));
        Assert.Throws<ArgumentException>(() => FallbackVaultCodec.EncryptSnapshot([], Key, new byte[31]));
        Assert.Throws<ArgumentException>(() => FallbackVaultCodec.EncryptLocator(new byte[31], Key, VaultId));
    }

    private static string CreateFixture(byte[] plaintext)
    {
        // Independent construction fixes field offsets, PBKDF2 domain bytes and Unicode AAD.
        var salt = Enumerable.Range(0x20, 16).Select(value => (byte)value).ToArray();
        var nonce = Enumerable.Range(0x40, 12).Select(value => (byte)value).ToArray();
        var saltWithDomain = Encoding.ASCII.GetBytes("Hermaeus.SecretStore.v3\0").Concat(salt).ToArray();
        var derived = Rfc2898DeriveBytes.Pbkdf2(Key, saltWithDomain, 100_000, HashAlgorithmName.SHA256, 32);
        var aad = Encoding.ASCII.GetBytes("Hermaeus.SecretStore\0v3\0value\0")
            .Concat(new byte[] { 6, 0, 0, 0, 0xc3, 0xa9, 0xf0, 0x9f, 0x94, 0x91 }).ToArray();
        var ciphertext = new byte[plaintext.Length];
        var tag = new byte[16];
        try
        {
            using var aes = new AesGcm(derived, 16);
            aes.Encrypt(nonce, plaintext, ciphertext, tag, aad);
            return "v3:" + Convert.ToBase64String(salt.Concat(nonce).Concat(tag).Concat(ciphertext).ToArray());
        }
        finally { CryptographicOperations.ZeroMemory(derived); }
    }
}
