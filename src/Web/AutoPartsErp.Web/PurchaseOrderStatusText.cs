namespace AutoPartsErp.Web;

/// <summary>
/// What a purchase order's state is called on screen, and how it is coloured.
/// </summary>
public static class PurchaseOrderStatusText
{
    /// <summary>Every state the list can be filtered by, in the order an order passes through them.</summary>
    public static IReadOnlyList<string> All { get; } =
    [
        "Draft", "Submitted", "Confirmed", "PartiallyReceived", "Received", "ClosedShort", "Cancelled",
    ];

    /// <summary>The Portuguese words for a status.</summary>
    /// <param name="status">The status as Purchasing spells it.</param>
    /// <returns>What to render.</returns>
    public static string Of(string? status) => status switch
    {
        "Draft" => "rascunho",
        "Submitted" => "enviada",
        "Confirmed" => "confirmada",
        "PartiallyReceived" => "parcial",
        "Received" => "recebida",
        "ClosedShort" => "fechada a menos",
        "Cancelled" => "cancelada",

        // Shown as it is rather than as "desconhecido", so whoever sees it can say which word
        // appeared.
        _ => status ?? "—",
    };

    /// <summary>
    /// The tag class a status wears.
    /// <para>
    /// Red is kept for the two ends a buyer has to look at twice: an order cancelled, and one
    /// closed with goods that never came. An order merely in progress is work, not a fault.
    /// </para>
    /// </summary>
    /// <param name="status">The status as Purchasing spells it.</param>
    /// <returns>The CSS class.</returns>
    public static string Tone(string? status) => status switch
    {
        "Received" => "tag-ok",
        "Draft" => "tag-wn",
        "Cancelled" or "ClosedShort" => "tag-dg",
        "Confirmed" or "PartiallyReceived" => "tag-ac",
        _ => string.Empty,
    };
}
