using System.Security.Cryptography;
using AutoPartsErp.SharedKernel.Security;

namespace AutoPartsErp.SharedKernel.Tests;

/// <summary>
/// Encryption at rest for the few columns worth encrypting.
/// <para>
/// The tests that matter here are not "it comes back out again" — that one is table stakes. They
/// are the three that decide whether this is worth having at all: that a value somebody edited in
/// the database refuses to decrypt rather than decrypting to something else, that a key can be
/// changed without making yesterday's rows unreadable, and that a column encrypted from today
/// forward does not lose everything written before.
/// </para>
/// </summary>
public sealed class AesGcmSecretProtectorTests
{
    private const string Iban = "PT50 0002 0123 1234 5678 9015 4";

    private static readonly string KeyOne =
        Convert.ToBase64String(Enumerable.Range(0, 32).Select(index => (byte)index).ToArray());

    private static readonly string KeyTwo =
        Convert.ToBase64String(Enumerable.Range(100, 32).Select(index => (byte)index).ToArray());

    /// <summary>What goes in comes out.</summary>
    [Theory]
    [InlineData(Iban)]
    [InlineData("")]
    [InlineData("acentuação, ç, €, and a 🙂")]
    public void A_protected_value_reads_back(string secret)
    {
        AesGcmSecretProtector protector = Protector();

        protector.Unprotect(protector.Protect(secret)).Should().Be(secret);
    }

    /// <summary>
    /// The stored form does not contain the secret, which is the entire point and is worth one
    /// test that says so out loud.
    /// </summary>
    [Fact]
    public void The_stored_form_does_not_contain_the_secret()
    {
        string stored = Protector().Protect(Iban);

        stored.Should().NotContain("PT50");
        stored.Should().StartWith("erp1:one:");
    }

    /// <summary>
    /// Twice is twice, because the nonce is fresh each time. Deterministic ciphertext would make
    /// the column searchable, and would also mean two customers with the same bank account are
    /// visibly the same customer to anybody reading the table.
    /// </summary>
    [Fact]
    public void The_same_secret_twice_is_two_different_ciphertexts()
    {
        AesGcmSecretProtector protector = Protector();

        protector.Protect(Iban).Should().NotBe(protector.Protect(Iban));
    }

    /// <summary>
    /// A value somebody changed in the database fails, rather than decrypting to something else.
    /// This is why GCM and not CBC.
    /// </summary>
    [Fact]
    public void A_tampered_value_refuses_to_decrypt()
    {
        AesGcmSecretProtector protector = Protector();
        string stored = protector.Protect(Iban);

        string prefix = stored[..(stored.LastIndexOf(':') + 1)];
        byte[] packed = Convert.FromBase64String(stored[prefix.Length..]);

        // One bit in the ciphertext, which is what an attacker with write access to the column
        // would do if the only thing standing in the way were the encryption.
        packed[^1] ^= 0x01;

        Action reading = () => protector.Unprotect(prefix + Convert.ToBase64String(packed));

        reading.Should().Throw<CryptographicException>();
    }

    /// <summary>
    /// A value relabelled as belonging to another key fails too, because the key name is fed to
    /// GCM as associated data rather than only being a lookup.
    /// </summary>
    [Fact]
    public void A_value_relabelled_with_another_key_refuses_to_decrypt()
    {
        AesGcmSecretProtector protector = Protector();
        string stored = protector.Protect(Iban);

        Action reading = () => protector.Unprotect(stored.Replace("erp1:one:", "erp1:two:", StringComparison.Ordinal));

        reading.Should().Throw<CryptographicException>();
    }

    /// <summary>
    /// The rotation this whole design exists for: yesterday's rows were written with the old key
    /// and are still read; today's are written with the new one.
    /// </summary>
    [Fact]
    public void A_rotated_key_still_reads_what_the_old_one_wrote()
    {
        string yesterday = Protector().Protect(Iban);

        var rotated = new AesGcmSecretProtector(EncryptionKeyRing.FromBase64(
            "two",
            new Dictionary<string, string> { ["one"] = KeyOne, ["two"] = KeyTwo }));

        rotated.Unprotect(yesterday).Should().Be(Iban);
        rotated.Protect(Iban).Should().StartWith("erp1:two:");
    }

    /// <summary>
    /// A key retired while rows still reference it says so, rather than returning nothing.
    /// </summary>
    [Fact]
    public void A_retired_key_is_named_in_the_failure()
    {
        string written = Protector().Protect(Iban);

        var withoutIt = new AesGcmSecretProtector(EncryptionKeyRing.FromBase64(
            "two", new Dictionary<string, string> { ["two"] = KeyTwo }));

        Action reading = () => withoutIt.Unprotect(written);

        reading.Should().Throw<CryptographicException>().WithMessage("*'one'*");
    }

    /// <summary>
    /// A column can start being encrypted without a migration that rewrites every row first: what
    /// is already there is plain text, and plain text is handed back unchanged.
    /// </summary>
    [Fact]
    public void A_value_written_before_encryption_reads_back_unchanged()
    {
        AesGcmSecretProtector protector = Protector();

        protector.IsProtected(Iban).Should().BeFalse();
        protector.Unprotect(Iban).Should().Be(Iban);
    }

    /// <summary>
    /// Protecting twice does not encrypt twice. A save path that ran through the converter a
    /// second time would otherwise produce something that reads back as a string beginning
    /// "erp1:", which nothing would recognize as wrong.
    /// </summary>
    [Fact]
    public void Protecting_an_already_protected_value_changes_nothing()
    {
        AesGcmSecretProtector protector = Protector();
        string once = protector.Protect(Iban);

        protector.Protect(once).Should().Be(once);
    }

    /// <summary>Garbage where a ciphertext should be is a failure, not an empty string.</summary>
    [Theory]
    [InlineData("erp1:")]
    [InlineData("erp1:one:")]
    [InlineData("erp1:one:not-base64!")]
    [InlineData("erp1:one:AAAA")]
    public void A_malformed_value_refuses_to_decrypt(string stored)
    {
        Action reading = () => Protector().Unprotect(stored);

        reading.Should().Throw<CryptographicException>();
    }

    /// <summary>
    /// A key of the wrong length is a deployment that would fail on the first row it touched, so
    /// it fails at startup instead.
    /// </summary>
    [Theory]
    [InlineData(16)]
    [InlineData(24)]
    [InlineData(31)]
    public void A_key_that_is_not_256_bits_is_refused(int size)
    {
        string wrong = Convert.ToBase64String(new byte[size]);

        Action building = () => EncryptionKeyRing.FromBase64(
            "one", new Dictionary<string, string> { ["one"] = wrong });

        building.Should().Throw<ArgumentException>();
    }

    /// <summary>An active key nobody supplied is refused at startup too.</summary>
    [Fact]
    public void An_active_key_with_nothing_behind_it_is_refused()
    {
        Action building = () => EncryptionKeyRing.FromBase64(
            "missing", new Dictionary<string, string> { ["one"] = KeyOne });

        building.Should().Throw<ArgumentException>();
    }

    /// <summary>
    /// A key name with a colon in it would split a stored value in the wrong place, so the name
    /// is refused rather than the value becoming unreadable later.
    /// </summary>
    [Fact]
    public void A_key_name_with_a_colon_is_refused()
    {
        Action building = () => EncryptionKeyRing.FromBase64(
            "a:b", new Dictionary<string, string> { ["a:b"] = KeyOne });

        building.Should().Throw<ArgumentException>();
    }

    /// <summary>A generated key is the length the ring accepts, which is the only thing to say about it.</summary>
    [Fact]
    public void A_generated_key_is_256_bits()
    {
        Convert.FromBase64String(EncryptionKeyRing.GenerateKey())
            .Should().HaveCount(EncryptionKeyRing.KeySizeInBytes);
    }

    /// <summary>A long secret survives, because nothing here assumes one block.</summary>
    [Fact]
    public void A_long_secret_survives()
    {
        string secret = new('x', 10_000);
        AesGcmSecretProtector protector = Protector();

        protector.Unprotect(protector.Protect(secret)).Should().Be(secret);
    }

    private static AesGcmSecretProtector Protector() =>
        new(EncryptionKeyRing.FromBase64(
            "one",
            new Dictionary<string, string> { ["one"] = KeyOne, ["two"] = KeyTwo }));
}
