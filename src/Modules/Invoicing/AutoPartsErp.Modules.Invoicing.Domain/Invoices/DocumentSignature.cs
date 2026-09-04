using AutoPartsErp.SharedKernel.Primitives;
using AutoPartsErp.SharedKernel.Results;

namespace AutoPartsErp.Modules.Invoicing.Domain.Invoices;

/// <summary>
/// A document's signature: the full base64 value that goes into a SAF-T export, and the four
/// characters of it that go on the printed page and into the QR code.
/// <para>
/// The four are not a summary or a checksum — they are literally the 1st, 11th, 21st and 31st
/// characters of the base64 signature, which is what Portaria 363/2010 asks for. Anyone holding
/// the printed document and the SAF-T file can check that they line up, which is the entire
/// point: a document reprinted with a different total would have a different signature and
/// therefore different characters on the page.
/// </para>
/// </summary>
public sealed class DocumentSignature : ValueObject
{
    /// <summary>
    /// The character positions taken from the base64 signature, 1-indexed, exactly as the
    /// legislation states them.
    /// </summary>
    public static readonly int[] PrintedPositions = [1, 11, 21, 31];

    private DocumentSignature(string value, string printed)
    {
        Value = value;
        Printed = printed;
    }

    /// <summary>
    /// Required by object-relational mappers that materialize this type as an owned value.
    /// Domain code always goes through <see cref="Create"/>.
    /// </summary>
#pragma warning disable CS8618
    private DocumentSignature()
    {
    }
#pragma warning restore CS8618

    /// <summary>
    /// The full base64 signature. This is what the next document in the series chains onto, and
    /// what goes in the <c>Hash</c> element of a SAF-T export — which the legislation calls a
    /// hash and which is in fact a signature.
    /// </summary>
    public string Value { get; } = string.Empty;

    /// <summary>
    /// The four characters printed on the document and carried in field Q of the QR code.
    /// Exactly four, because that field admits exactly four.
    /// </summary>
    public string Printed { get; } = string.Empty;

    /// <summary>Takes a base64 signature and pulls the four characters out of it.</summary>
    /// <param name="base64Signature">What the signer returned.</param>
    public static Result<DocumentSignature> Create(string? base64Signature)
    {
        if (string.IsNullOrWhiteSpace(base64Signature))
        {
            return InvoicingErrors.Document.SignatureRequired;
        }

        string value = base64Signature.Trim();

        // A 1024-bit RSA signature is 172 base64 characters, so position 31 always exists. A
        // shorter string means a key that is too small or a signer returning something that is
        // not a signature at all - and taking characters out of it would produce four plausible
        // symbols on a document that could never be verified.
        if (value.Length < 31)
        {
            return InvoicingErrors.Document.SignatureTooShort;
        }

        Span<char> printed = stackalloc char[PrintedPositions.Length];

        for (int i = 0; i < PrintedPositions.Length; i++)
        {
            printed[i] = value[PrintedPositions[i] - 1];
        }

        return new DocumentSignature(value, new string(printed));
    }

    /// <summary>Rehydrates a signature already known to be valid, for the persistence layer.</summary>
    public static DocumentSignature FromStorage(string value, string printed) => new(value, printed);

    /// <inheritdoc />
    protected override IEnumerable<object?> GetEqualityComponents()
    {
        yield return Value;
    }

    /// <inheritdoc />
    public override string ToString() => Printed;
}
