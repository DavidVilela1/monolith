using AutoPartsErp.Modules.Invoicing.Domain.Signing;

namespace AutoPartsErp.Modules.Invoicing.Tests;

/// <summary>
/// The string a document's signature is computed over, checked against the two worked examples in
/// the tax authority's own specification.
/// <para>
/// Every character here is prescribed. These are not tests of our design — they are tests that we
/// implemented somebody else's, and the reason they matter is that getting one wrong produces
/// signatures that verify against nothing and a failure that does not surface until the AT
/// rejects a file containing months of documents.
/// </para>
/// </summary>
public sealed class SignatureSourceTests
{
    private const string PreviousSignature =
        "F8952fjEClltx2tF9m6/QTFynFjSuiboMslNZ1ag9oR5iIivgYYa0cNa0wJeWXlsf8QQVHUol303hp7Xm" +
        "Iy5/kFOiV0Cv8QH6SF0Q5zNsDtpeFh2ZJ256y0DkJMSQqCq3oSka+9zIXXRkXgEsSv6VScCYv8VTlIcGjsablpR6A4=";

    /// <summary>The first document in a series has nothing before it, and still ends in a semicolon.</summary>
    [Fact]
    public void The_first_document_matches_the_published_example()
    {
        string source = SignatureSource.Build(
            new DateOnly(2008, 3, 10),
            new DateTimeOffset(2008, 3, 10, 15, 58, 0, TimeSpan.Zero),
            "FT 1/1",
            28.07m,
            previousSignature: null);

        source.Should().Be("2008-03-10;2008-03-10T15:58:00;FT 1/1;28.07;");
    }

    [Fact]
    public void The_second_document_chains_onto_the_first_and_matches_the_published_example()
    {
        string source = SignatureSource.Build(
            new DateOnly(2008, 9, 16),
            new DateTimeOffset(2008, 9, 16, 15, 58, 0, TimeSpan.Zero),
            "FT 1/2",
            235.15m,
            PreviousSignature);

        source.Should().Be($"2008-09-16;2008-09-16T15:58:00;FT 1/2;235.15;{PreviousSignature}");
    }

    /// <summary>
    /// Converted, not truncated. Two branches an hour apart must not sign different strings for
    /// the same moment.
    /// </summary>
    [Fact]
    public void The_entry_timestamp_is_written_in_utc()
    {
        string source = SignatureSource.Build(
            new DateOnly(2026, 9, 4),
            new DateTimeOffset(2026, 9, 4, 10, 30, 0, TimeSpan.FromHours(1)),
            "FT S/1",
            10m,
            previousSignature: null);

        source.Should().Be("2026-09-04;2026-09-04T09:30:00;FT S/1;10.00;");
    }

    [Theory]
    [InlineData(10, "10.00")]
    [InlineData(1234.5, "1234.50")]
    [InlineData(1234567.89, "1234567.89")]
    [InlineData(0, "0.00")]
    [InlineData(-15, "-15.00")]
    public void Amounts_carry_two_decimals_a_dot_and_no_thousands_separator(decimal amount, string expected)
    {
        SignatureSource.FormatAmount(amount).Should().Be(expected);
    }

    /// <summary>
    /// The same rounding the rest of the system uses. A signed total and a printed total that
    /// disagree by a cent is a document nobody can verify.
    /// </summary>
    [Fact]
    public void Amounts_round_half_to_even()
    {
        SignatureSource.FormatAmount(2.225m).Should().Be("2.22");
        SignatureSource.FormatAmount(2.235m).Should().Be("2.24");
    }
}
