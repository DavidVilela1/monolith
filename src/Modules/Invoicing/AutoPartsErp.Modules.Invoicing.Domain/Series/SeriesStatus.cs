namespace AutoPartsErp.Modules.Invoicing.Domain.Series;

/// <summary>
/// Where a document series is in its life.
/// <para>
/// <c>Registered → Active → Closed</c>, one way. A series that has issued a document can never go
/// back, because the documents are out in the world and the AT has been told the series exists.
/// </para>
/// </summary>
public enum SeriesStatus
{
    /// <summary>Unspecified. Never persisted.</summary>
    Unknown = 0,

    /// <summary>
    /// Created here, not yet usable. Waiting for the validation code the AT returns when the
    /// series is declared — without it there is no ATCUD, and without an ATCUD there is no legal
    /// document.
    /// </summary>
    Registered = 1,

    /// <summary>Live. Hands out numbers.</summary>
    Active = 2,

    /// <summary>
    /// Finished. Issues nothing further, and everything it issued stays exactly as it is. The AT
    /// has to be told, because a series that goes quiet without being closed looks like paperwork
    /// somebody is hiding.
    /// </summary>
    Closed = 3,
}
