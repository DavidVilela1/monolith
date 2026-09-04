using System.Security.Cryptography;
using System.Text;
using AutoPartsErp.Modules.Invoicing.Application.Options;
using AutoPartsErp.Modules.Invoicing.Domain.Signing;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AutoPartsErp.Modules.Invoicing.Infrastructure.Signing;

/// <summary>
/// Signs documents with the company's registered RSA key.
/// <para>
/// RSA, PKCS#1 v1.5 padding, SHA-1 digest. SHA-1 has been unsafe for signatures since 2017 and is
/// not a choice anybody would make today — it is what Portaria 363/2010 prescribes, the tax
/// authority's validator checks for exactly this, and changing it would make every document this
/// system produces unverifiable. Being wrong in the way the law is wrong is the only option.
/// </para>
/// <para>
/// The key is loaded once and held for the life of the process. It never changes for a certified
/// version, because every document already issued was signed with it and the chain has to stay
/// verifiable.
/// </para>
/// </summary>
public sealed class RsaDocumentSigner : IDocumentSigner, IDisposable
{
    private readonly RSA _key;

    /// <summary>Initializes the signer from configuration.</summary>
    /// <param name="options">The company's certificate number and private key.</param>
    /// <param name="logger">Used to say loudly when a throwaway key is in use.</param>
    public RsaDocumentSigner(IOptions<InvoicingOptions> options, ILogger<RsaDocumentSigner> logger)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(logger);

        InvoicingOptions settings = options.Value;
        CertificateNumber = settings.CertificateNumber;

        _key = RSA.Create();

        if (string.IsNullOrWhiteSpace(settings.PrivateKeyPem))
        {
            // A throwaway key, regenerated on every start. Documents signed with it chain among
            // themselves and verify against nothing, which is the correct behaviour for a
            // development database: it lets the whole flow be exercised, and it guarantees that
            // nobody mistakes the result for a legal document.
            _key.KeySize = 1024;

            logger.LogWarning(
                "Invoicing is signing with a throwaway key generated at startup. Documents issued "
                + "now cannot be verified by anybody and are not legal documents. Set "
                + "{Section}:PrivateKeyPem from a secret store before issuing anything real.",
                InvoicingOptions.SectionName);
        }
        else
        {
            _key.ImportFromPem(settings.PrivateKeyPem);

            // The legislation was written around 1024-bit keys and the AT's validator accepts
            // larger ones. Smaller is refused outright rather than warned about, because a 512-bit
            // signature is short enough that the four printed characters would run off its end.
            if (_key.KeySize < 1024)
            {
                throw new InvalidOperationException(
                    $"The invoicing signing key is {_key.KeySize} bits. The tax authority requires "
                    + "at least 1024.");
            }
        }
    }

    /// <inheritdoc />
    public string CertificateNumber { get; }

    /// <inheritdoc />
    public string Sign(string source)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(source);

        // UTF-8, and it matters: a source string containing a customer name with an accent would
        // sign to different bytes under a different encoding, and the document would verify on
        // the machine that made it and nowhere else.
        byte[] bytes = Encoding.UTF8.GetBytes(source);

#pragma warning disable CA5350 // SHA-1 is what Portaria 363/2010 prescribes. See the class remarks.
        byte[] signature = _key.SignData(bytes, HashAlgorithmName.SHA1, RSASignaturePadding.Pkcs1);
#pragma warning restore CA5350

        return Convert.ToBase64String(signature);
    }

    /// <inheritdoc />
    public void Dispose() => _key.Dispose();
}
