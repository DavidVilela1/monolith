namespace AutoPartsErp.Modules.Invoicing.Domain.Signing;

/// <summary>
/// Signs a document's source string with the company's private key.
/// <para>
/// A port, in the domain, because issuing a document is not complete without a signature and the
/// aggregate cannot be made to depend on a key file. The implementation lives in Infrastructure
/// and does RSA with PKCS#1 v1.5 padding and a SHA-1 digest, which is what the legislation
/// prescribes — SHA-1 is not a choice anyone would make today, and it is not ours to make.
/// </para>
/// <para>
/// The key is the one registered with the AT under the software's certification number. It never
/// changes for the life of a certified version, because every document already issued was signed
/// with it and the chain has to stay verifiable.
/// </para>
/// </summary>
public interface IDocumentSigner
{
    /// <summary>
    /// The certification number the AT issued for this software, which goes in field R of the QR
    /// code. Four digits, and <c>0</c> for software that is not certified yet — which is a legal
    /// state for a system in development, and a very illegal one for a system issuing real
    /// invoices.
    /// </summary>
    string CertificateNumber { get; }

    /// <summary>Signs the source string and returns the base64 signature.</summary>
    /// <param name="source">The string built by <see cref="SignatureSource.Build"/>.</param>
    string Sign(string source);
}
