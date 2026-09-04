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
    /// The RSA private key registered with the tax authority, in PEM form.
    /// <para>
    /// Empty in development, where the signer generates a throwaway key at startup and says so
    /// loudly. A real deployment supplies it from a secret store, never from appsettings.json —
    /// anyone holding this key can sign documents in the company's name.
    /// </para>
    /// </summary>
    public string PrivateKeyPem { get; set; } = string.Empty;
}
