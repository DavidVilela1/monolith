namespace AutoPartsErp.SharedKernel.Security;

/// <summary>
/// Turns a secret into something a database column can hold, and back again.
/// <para>
/// This is encryption at rest for the handful of fields that are worth it: a bank account number,
/// a portal password the company keeps on a supplier's behalf, the private key a document series
/// is signed with. Not for passwords — a password is hashed and never read back, which is a
/// different job with a different tool.
/// </para>
/// <para>
/// A protected value cannot be searched, sorted or joined on. That is the cost, and it is why the
/// decision of what to protect is made field by field rather than table by table.
/// </para>
/// </summary>
public interface ISecretProtector
{
    /// <summary>Protects a value.</summary>
    /// <param name="plaintext">The value as it is in the world.</param>
    /// <returns>The value as it should be stored.</returns>
    string Protect(string plaintext);

    /// <summary>
    /// Reads a protected value back.
    /// <para>
    /// A value that was never protected is returned unchanged, so a column can be encrypted from a
    /// given day forward without a migration that has to decrypt-and-rewrite every row at once —
    /// and without the rows written before that day becoming unreadable.
    /// </para>
    /// </summary>
    /// <param name="value">What the column holds.</param>
    /// <returns>The value as it is in the world.</returns>
    string Unprotect(string value);

    /// <summary>Whether a stored value is one this protector wrote.</summary>
    /// <param name="value">What the column holds.</param>
    /// <returns>True when it is protected.</returns>
    bool IsProtected(string value);
}
