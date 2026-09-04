using AutoPartsErp.SharedKernel.Primitives;
using AutoPartsErp.SharedKernel.Results;

namespace AutoPartsErp.Modules.Invoicing.Domain.Invoices;

/// <summary>
/// The document's unique code: the series' AT validation code, a hyphen, and the document's
/// number within that series.
/// <para>
/// <c>CSDF7T5H-35</c>. Mandatory on every document since January 2023, printed on the page and
/// carried in field H of the QR code. It is the pair of facts that lets an inspector take a
/// printed invoice and ask the AT whether that exact document was declared: the validation code
/// says which registered series, the number says which document in it.
/// </para>
/// <para>
/// The number is not zero-padded. The AT's own worked examples show it plain, and padding it
/// would produce a code that does not match the one the series would generate for the same
/// document.
/// </para>
/// </summary>
public sealed class Atcud : ValueObject
{
    private Atcud(string validationCode, int number)
    {
        ValidationCode = validationCode;
        Number = number;
    }

    /// <summary>
    /// Required by object-relational mappers that materialize this type as an owned value.
    /// Domain code always goes through <see cref="Create"/>.
    /// </summary>
#pragma warning disable CS8618
    private Atcud()
    {
    }
#pragma warning restore CS8618

    /// <summary>The code the AT issued for the series.</summary>
    public string ValidationCode { get; } = string.Empty;

    /// <summary>The document's number within that series.</summary>
    public int Number { get; }

    /// <summary>The code as printed and as it goes into the QR, e.g. <c>CSDF7T5H-35</c>.</summary>
    public string Value => $"{ValidationCode}-{Number}";

    /// <summary>Builds the code for one document.</summary>
    /// <param name="validationCode">The series' AT validation code.</param>
    /// <param name="number">The document's number within the series.</param>
    public static Result<Atcud> Create(string? validationCode, int number)
    {
        if (string.IsNullOrWhiteSpace(validationCode))
        {
            return InvoicingErrors.Document.AtcudNeedsValidationCode;
        }

        return number < 1
            ? InvoicingErrors.Document.AtcudNumberNotPositive
            : new Atcud(validationCode.Trim().ToUpperInvariant(), number);
    }

    /// <summary>Rehydrates a code already known to be valid, for the persistence layer.</summary>
    public static Atcud FromStorage(string validationCode, int number) => new(validationCode, number);

    /// <inheritdoc />
    protected override IEnumerable<object?> GetEqualityComponents()
    {
        yield return ValidationCode;
        yield return Number;
    }

    /// <inheritdoc />
    public override string ToString() => Value;
}
