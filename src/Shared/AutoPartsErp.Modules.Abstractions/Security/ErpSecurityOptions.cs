namespace AutoPartsErp.Modules.Abstractions.Security;

/// <summary>
/// The knobs on the transport and browser hardening, so that a deployment behind a load balancer
/// and a laptop running the thing locally can differ without either of them editing code.
/// </summary>
public sealed class ErpSecurityOptions
{
    /// <summary>Where these settings live in configuration.</summary>
    public const string SectionName = "Erp:Security";

    /// <summary>
    /// The proxies whose <c>X-Forwarded-*</c> headers are believed, as addresses or CIDR ranges.
    /// <para>
    /// Empty means loopback only, which is the framework's default and the right answer on a
    /// laptop. It is deliberately not "trust whoever sends the header": a system that did would
    /// let any caller claim any address, and the rate limit below counts by address.
    /// </para>
    /// </summary>
    public IList<string> TrustedProxies { get; } = [];

    /// <summary>
    /// How long a browser should refuse to speak plain HTTP to this host, in days.
    /// <para>
    /// A year, which is what a preload list requires. It is a promise that is hard to take back —
    /// a browser that has seen the header will not try HTTP again until it expires — so it is
    /// sent outside Development only.
    /// </para>
    /// </summary>
    public int HstsDays { get; set; } = 365;

    /// <summary>
    /// Whether to ask browser vendors to ship this host in their preload list. Off by default:
    /// it is a submission a person makes deliberately, for a domain they are sure about.
    /// </summary>
    public bool HstsPreload { get; set; }

    /// <summary>
    /// The port a plain HTTP request is redirected to. Zero lets the host work it out from the
    /// addresses it is listening on, which is right everywhere except behind a proxy that
    /// terminates TLS on a port this process never sees.
    /// </summary>
    public int HttpsPort { get; set; }

    /// <summary>
    /// Origins besides this host that a page may load scripts, stylesheets and fonts from, space
    /// separated.
    /// <para>
    /// One entry today, the CDN Bootstrap comes from. Emptying this is the second half of
    /// vendoring Bootstrap locally, and the day that happens the content security policy says
    /// every asset on every page came from this host and nowhere else.
    /// </para>
    /// </summary>
    public string AssetOrigins { get; set; } = "https://cdn.jsdelivr.net";

    /// <summary>
    /// Sends the content security policy as a report-only header instead of an enforced one.
    /// <para>
    /// For the afternoon a policy is tightened: the browser reports what the new policy would
    /// have blocked and blocks nothing, so a screen nobody opened that week is not discovered
    /// broken by the person who needed it.
    /// </para>
    /// </summary>
    public bool ContentSecurityPolicyReportOnly { get; set; }

    /// <summary>How many sign-in attempts one address gets per window.</summary>
    public int SignInAttempts { get; set; } = 10;

    /// <summary>How long that window is, in minutes.</summary>
    public int SignInWindowMinutes { get; set; } = 5;

    /// <summary>The keys the few encrypted columns are written with.</summary>
    public EncryptionOptions Encryption { get; } = new();
}

/// <summary>
/// Where the encryption keys come from.
/// <para>
/// A dictionary rather than one key, because a system that holds exactly one key is a system whose
/// key can never be changed. The names are arbitrary and only have to be stable: a stored value
/// carries the name of the key that wrote it.
/// </para>
/// </summary>
public sealed class EncryptionOptions
{
    /// <summary>The name of the key new values are written with.</summary>
    public string ActiveKeyId { get; set; } = "main";

    /// <summary>Every key this deployment holds, by name, base64 encoded, 32 bytes each.</summary>
    public IDictionary<string, string> Keys { get; } =
        new Dictionary<string, string>(StringComparer.Ordinal);
}
