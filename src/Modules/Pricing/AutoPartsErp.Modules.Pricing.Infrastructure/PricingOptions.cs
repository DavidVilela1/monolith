namespace AutoPartsErp.Modules.Pricing.Infrastructure;

/// <summary>
/// The company-wide pricing settings. Bound from <c>Erp:Pricing</c>.
/// <para>
/// One setting, and it is a fallback rather than a policy. What a given customer's line may make
/// is a decision that lives on their price list, where it is data somebody can change on a
/// Tuesday afternoon. This is the number used when no list has an opinion — which is most lists,
/// most of the time, and is exactly the case where a single figure is the honest answer.
/// </para>
/// </summary>
public sealed class PricingOptions
{
    /// <summary>The configuration section these are read from.</summary>
    public const string SectionName = "Erp:Pricing";

    /// <summary>
    /// The least a line may make when its price list says nothing, as a percentage of what the
    /// customer pays. Null — the default — means no floor at all.
    /// <para>
    /// Unset by default, and that default is deliberate. A floor nobody chose should not start
    /// refusing sales the first time somebody upgrades: an installation that wants one sets it,
    /// and until then the margin is recorded and reported without anything being blocked.
    /// </para>
    /// <para>
    /// Which is why this is nullable rather than defaulting to zero. Zero is not "no floor" —
    /// zero says the company never sells below cost, and a distributor clearing obsolete stock at
    /// a loss does exactly that on purpose. Somebody has to choose it.
    /// </para>
    /// </summary>
    public decimal? DefaultMinimumMarginPercent { get; set; }
}
