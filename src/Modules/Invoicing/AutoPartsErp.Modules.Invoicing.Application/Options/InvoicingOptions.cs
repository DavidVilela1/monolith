using AutoPartsErp.Modules.Invoicing.Domain.Invoices;

namespace AutoPartsErp.Modules.Invoicing.Application.Options;

/// <summary>
/// Everything about the company that issues the documents, rather than about the documents.
/// <para>
/// All four of these are facts the tax authority holds about a specific installation, and none of
/// them belongs on an aggregate: the NIF and the region are properties of the establishment, and
/// the certificate number and key are properties of a certified build of this software. They are
/// configuration because they are the same for every document a deployment ever issues.
/// </para>
/// <para>
/// Handlers take this type directly rather than <c>IOptions&lt;InvoicingOptions&gt;</c>. Every
/// Application project in this system references only the domain and the shared contracts — no
/// packages at all — and one <c>using Microsoft.Extensions.Options</c> would be the first crack in
/// that. The module registration resolves the value once and registers it, so the binding and the
/// startup validation still happen; the handlers just never learn where it came from.
/// </para>
/// </summary>
public sealed class InvoicingOptions
{
    /// <summary>The configuration section these are read from.</summary>
    public const string SectionName = "Erp:Invoicing";

    /// <summary>
    /// The company's own NIF, without a country prefix. Field A of every QR code.
    /// </summary>
    public string IssuerTaxNumber { get; set; } = string.Empty;

    /// <summary>Which set of Portuguese rates this establishment invoices at.</summary>
    public TaxRegion TaxRegion { get; set; } = TaxRegion.Mainland;

    /// <summary>
    /// The software certification number the tax authority issued, which goes in field R.
    /// <para>
    /// <c>0</c> means uncertified. That is a legal value for software in development and a very
    /// illegal one for software issuing real documents, so it is the default — a deployment that
    /// has not been told its number cannot accidentally look certified.
    /// </para>
    /// </summary>
    public string CertificateNumber { get; set; } = "0";

    /// <summary>
    /// The version of the signing key, which goes in the SAF-T <c>HashControl</c> field.
    /// <para>
    /// It says which of the company's registered key pairs signed a document, so that a file
    /// spanning a key rotation can still be verified. Almost every installation has exactly one
    /// key and this is "1" forever; it is configuration rather than a constant because the
    /// installation that does rotate cannot have the number hard-coded.
    /// </para>
    /// </summary>
    public string PrivateKeyVersion { get; set; } = "1";

    /// <summary>
    /// The RSA private key registered with the tax authority, in PEM form.
    /// <para>
    /// Empty in development, where the signer generates a throwaway key at startup and says so
    /// loudly. A real deployment supplies it from a secret store, never from appsettings.json —
    /// anyone holding this key can sign documents in the company's name.
    /// </para>
    /// </summary>
    public string PrivateKeyPem { get; set; } = string.Empty;

    /// <summary>Who the company is, as the SAF-T header names them.</summary>
    public SaftCompanyOptions Company { get; set; } = new();

    /// <summary>Which program produced the file, as the SAF-T header names it.</summary>
    public SaftProductOptions Product { get; set; } = new();
}

/// <summary>
/// The company, as a tax filing describes it.
/// <para>
/// None of this is on an aggregate for the same reason the NIF is not: it is the same for every
/// document a deployment ever issues. It lives here rather than in a database table because a
/// SAF-T file that names the wrong company is not a data problem to be corrected later — it is a
/// filing, and the fix is another filing.
/// </para>
/// </summary>
public sealed class SaftCompanyOptions
{
    /// <summary>The registered name.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// The <c>CompanyID</c>: the registry office and company number, e.g. <c>Lisboa 12345</c>.
    /// <para>
    /// Left empty, the export falls back to the tax number, which is what the tax authority
    /// accepts from a sole trader who has no registration to quote.
    /// </para>
    /// </summary>
    public string CompanyId { get; set; } = string.Empty;

    /// <summary>The trading name, when it differs from the registered one.</summary>
    public string? BusinessName { get; set; }

    /// <summary>Street and number, on one line.</summary>
    public string AddressDetail { get; set; } = string.Empty;

    /// <summary>The town.</summary>
    public string City { get; set; } = string.Empty;

    /// <summary>The postcode.</summary>
    public string PostalCode { get; set; } = string.Empty;
}

/// <summary>
/// The program, as the tax authority knows it.
/// <para>
/// <see cref="CompanyTaxId"/> is the software <i>vendor's</i> NIF and not the user's — the two are
/// the same only when a company writes its own billing software, which is the case here and will
/// stop being the case the first time this is sold to anybody.
/// </para>
/// </summary>
public sealed class SaftProductOptions
{
    /// <summary>The NIF of whoever wrote the software.</summary>
    public string CompanyTaxId { get; set; } = string.Empty;

    /// <summary>
    /// The <c>ProductID</c>, which the tax authority defines as
    /// <c>product name/company that produced it</c>.
    /// </summary>
    public string ProductId { get; set; } = "AutoPartsErp/AutoPartsErp";

    /// <summary>The version of the program that produced the file.</summary>
    public string Version { get; set; } = "1.0";
}
