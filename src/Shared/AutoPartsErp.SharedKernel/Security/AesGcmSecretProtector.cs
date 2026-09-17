using System.Security.Cryptography;
using System.Text;

namespace AutoPartsErp.SharedKernel.Security;

/// <summary>
/// Encryption at rest with AES-256 in GCM.
/// <para>
/// GCM rather than CBC because it authenticates as well as encrypts: a ciphertext somebody edited
/// in the database fails to decrypt instead of decrypting to something else. An ERP that can be
/// made to read a different bank account by flipping bits in a column is worse than one that
/// stored the account in the clear, because the second is obviously wrong and the first is not.
/// </para>
/// <para>
/// A stored value looks like <c>erp1:main:BASE64</c> — the format, the name of the key that wrote
/// it, and then the nonce, the tag and the ciphertext. The key name travels with the value so the
/// key can be rotated without rewriting a table, and it is fed to GCM as associated data, so a
/// ciphertext relabelled as belonging to another key does not decrypt.
/// </para>
/// </summary>
public sealed class AesGcmSecretProtector : ISecretProtector
{
    /// <summary>The marker every protected value starts with.</summary>
    public const string Prefix = "erp1:";

    /// <summary>
    /// A fresh 12-byte nonce per value, which is the size GCM is specified for. It is not a
    /// secret and is stored beside the ciphertext; what matters is that it is never reused with
    /// the same key, which random bytes of this length give at the volumes an ERP writes.
    /// </summary>
    private const int NonceSizeInBytes = 12;

    /// <summary>The full 128-bit authentication tag. A truncated tag is a weaker one.</summary>
    private const int TagSizeInBytes = 16;

    private readonly EncryptionKeyRing _keys;

    /// <summary>Initializes the protector.</summary>
    /// <param name="keys">The keys this deployment holds.</param>
    public AesGcmSecretProtector(EncryptionKeyRing keys)
    {
        ArgumentNullException.ThrowIfNull(keys);

        _keys = keys;
    }

    /// <inheritdoc />
    public bool IsProtected(string value) =>
        value is not null && value.StartsWith(Prefix, StringComparison.Ordinal);

    /// <inheritdoc />
    public string Protect(string plaintext)
    {
        ArgumentNullException.ThrowIfNull(plaintext);

        // Protecting something already protected would double-encrypt it, and the second read
        // would return a string starting with "erp1:" that nothing would recognize as a failure.
        if (IsProtected(plaintext))
        {
            return plaintext;
        }

        string keyId = _keys.ActiveKeyId;
        byte[] bytes = Encoding.UTF8.GetBytes(plaintext);
        byte[] nonce = RandomNumberGenerator.GetBytes(NonceSizeInBytes);
        byte[] tag = new byte[TagSizeInBytes];
        byte[] ciphertext = new byte[bytes.Length];

        try
        {
            using var aes = new AesGcm(_keys.Active(), TagSizeInBytes);

            aes.Encrypt(nonce, bytes, ciphertext, tag, AssociatedData(keyId));
        }
        finally
        {
            // The plaintext is gone from this array before it goes back to the pool of memory the
            // process will hand to something else.
            CryptographicOperations.ZeroMemory(bytes);
        }

        byte[] packed = [.. nonce, .. tag, .. ciphertext];

        return string.Concat(Prefix, keyId, ":", Convert.ToBase64String(packed));
    }

    /// <inheritdoc />
    /// <exception cref="CryptographicException">
    /// The value is malformed, names a key this deployment does not hold, or fails its tag —
    /// which means somebody changed it after it was written.
    /// </exception>
    public string Unprotect(string value)
    {
        ArgumentNullException.ThrowIfNull(value);

        if (!IsProtected(value))
        {
            return value;
        }

        string body = value[Prefix.Length..];
        int separator = body.IndexOf(':', StringComparison.Ordinal);

        if (separator <= 0)
        {
            throw new CryptographicException("A protected value is missing its key name.");
        }

        string keyId = body[..separator];
        byte[] key = _keys.Find(keyId)
            ?? throw new CryptographicException(
                $"This value was written with encryption key '{keyId}', which this deployment "
                + "does not hold. A key was retired while rows still referenced it.");

        byte[] packed;

        try
        {
            packed = Convert.FromBase64String(body[(separator + 1)..]);
        }
        catch (FormatException exception)
        {
            throw new CryptographicException(
                "A protected value is not valid base64.", exception);
        }

        if (packed.Length < NonceSizeInBytes + TagSizeInBytes)
        {
            throw new CryptographicException("A protected value is too short to be one.");
        }

        ReadOnlySpan<byte> span = packed;
        byte[] plaintext = new byte[packed.Length - NonceSizeInBytes - TagSizeInBytes];

        try
        {
            using var aes = new AesGcm(key, TagSizeInBytes);

            aes.Decrypt(
                span[..NonceSizeInBytes],
                span[(NonceSizeInBytes + TagSizeInBytes)..],
                span.Slice(NonceSizeInBytes, TagSizeInBytes),
                plaintext,
                AssociatedData(keyId));

            return Encoding.UTF8.GetString(plaintext);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(plaintext);
        }
    }

    /// <summary>
    /// What the tag covers besides the ciphertext.
    /// <para>
    /// The format marker and the key name. Without this, a value could be moved to a row whose
    /// column says it was written with a different key, and the only thing that would notice is
    /// nothing.
    /// </para>
    /// </summary>
    private static byte[] AssociatedData(string keyId) =>
        Encoding.UTF8.GetBytes(string.Concat(Prefix, keyId));
}
