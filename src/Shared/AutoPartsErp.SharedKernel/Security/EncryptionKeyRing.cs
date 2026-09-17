using System.Globalization;
using System.Security.Cryptography;

namespace AutoPartsErp.SharedKernel.Security;

/// <summary>
/// The keys this system encrypts with, and the one it is encrypting with today.
/// <para>
/// More than one, always, because the alternative is a system that can never change its key. A
/// value carries the name of the key that wrote it, so rotating means adding a key and making it
/// active: everything written from then on uses the new one, everything written before is still
/// readable, and the old key is retired the day nothing references it any more.
/// </para>
/// </summary>
public sealed class EncryptionKeyRing
{
    /// <summary>The only key length this system accepts: AES-256.</summary>
    public const int KeySizeInBytes = 32;

    private readonly Dictionary<string, byte[]> _keys;

    private EncryptionKeyRing(string activeKeyId, Dictionary<string, byte[]> keys)
    {
        ActiveKeyId = activeKeyId;
        _keys = keys;
    }

    /// <summary>The name of the key new values are written with.</summary>
    public string ActiveKeyId { get; }

    /// <summary>
    /// Builds a key ring from base64 keys, as they arrive from configuration or a secret store.
    /// </summary>
    /// <param name="activeKeyId">Which of them writes new values.</param>
    /// <param name="keys">Every key, by name, base64 encoded.</param>
    /// <returns>The key ring.</returns>
    /// <exception cref="ArgumentException">
    /// A name that is empty or contains a colon, a key that is not valid base64, a key that is not
    /// 32 bytes, or an active name with no key behind it. Every one of these is a deployment that
    /// would start and then fail on the first row it touched, so it fails here instead.
    /// </exception>
    public static EncryptionKeyRing FromBase64(
        string activeKeyId, IReadOnlyDictionary<string, string> keys)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(activeKeyId);
        ArgumentNullException.ThrowIfNull(keys);

        var material = new Dictionary<string, byte[]>(StringComparer.Ordinal);

        foreach (KeyValuePair<string, string> entry in keys)
        {
            if (string.IsNullOrWhiteSpace(entry.Key) || entry.Key.Contains(':', StringComparison.Ordinal))
            {
                throw new ArgumentException(
                    "An encryption key name cannot be empty or contain a colon; the colon "
                    + "separates the name from the ciphertext in a stored value.",
                    nameof(keys));
            }

            byte[] key;

            try
            {
                key = Convert.FromBase64String(entry.Value ?? string.Empty);
            }
            catch (FormatException exception)
            {
                throw new ArgumentException(
                    $"Encryption key '{entry.Key}' is not valid base64.", nameof(keys), exception);
            }

            if (key.Length != KeySizeInBytes)
            {
                throw new ArgumentException(
                    "Encryption key '" + entry.Key + "' is "
                    + key.Length.ToString(CultureInfo.InvariantCulture)
                    + " bytes. AES-256 takes exactly "
                    + KeySizeInBytes.ToString(CultureInfo.InvariantCulture) + ".",
                    nameof(keys));
            }

            material[entry.Key] = key;
        }

        if (!material.ContainsKey(activeKeyId))
        {
            throw new ArgumentException(
                $"The active encryption key is named '{activeKeyId}', and no key by that name was "
                + "supplied.",
                nameof(activeKeyId));
        }

        return new EncryptionKeyRing(activeKeyId, material);
    }

    /// <summary>Generates a key, for a deployment that is setting itself up for the first time.</summary>
    /// <returns>32 random bytes, base64 encoded.</returns>
    public static string GenerateKey() =>
        Convert.ToBase64String(RandomNumberGenerator.GetBytes(KeySizeInBytes));

    /// <summary>The key new values are written with.</summary>
    /// <returns>The active key.</returns>
    public byte[] Active() => _keys[ActiveKeyId];

    /// <summary>Finds the key a stored value names.</summary>
    /// <param name="keyId">The name the value carries.</param>
    /// <returns>The key, or null when this deployment does not have it.</returns>
    public byte[]? Find(string keyId)
    {
        ArgumentNullException.ThrowIfNull(keyId);

        return _keys.TryGetValue(keyId, out byte[]? key) ? key : null;
    }
}
