namespace AutoPartsErp.Web;

/// <summary>
/// What a replenishment suggestion's state is called on screen, and how it is coloured.
/// <para>
/// The same job <see cref="PartStatusText"/> does for a part, for the other vocabulary this
/// surface shows. One place per module's words, so that adding a state is a change the compiler
/// stays quiet about and this class does not.
/// </para>
/// </summary>
public static class SuggestionStatusText
{
    /// <summary>The Portuguese word for a status.</summary>
    /// <param name="status">The status as Purchasing spells it.</param>
    /// <returns>What to render.</returns>
    public static string Of(string? status) => status switch
    {
        "Open" => "por decidir",
        "Ordered" => "encomendada",
        "Dismissed" => "retirada",

        // Shown as it is rather than as "desconhecido", so whoever sees it can say which word
        // appeared. That is the only useful thing about a bug of this kind.
        _ => status ?? "—",
    };

    /// <summary>
    /// The tag class a status wears.
    /// <para>
    /// Nothing is red. A suggestion nobody has dealt with is work, not a fault, and a screen that
    /// alarms about its own to-do list is a screen people stop reading.
    /// </para>
    /// </summary>
    /// <param name="status">The status as Purchasing spells it.</param>
    /// <returns>The CSS class.</returns>
    public static string Tone(string? status) => status switch
    {
        "Open" => "tag-wn",
        "Ordered" => "tag-ok",
        _ => string.Empty,
    };
}
