namespace AutoPartsErp.Web;

/// <summary>
/// What a part's status is called on screen, and how it is coloured.
/// <para>
/// The domain's statuses are English words in code, because the code is written in English. The
/// person at the counter reads Portuguese, and "Discontinued" on a row is a word they have to
/// translate every time. One place does the translation, so the day a status is added the
/// compiler does not care and this does.
/// </para>
/// </summary>
public static class PartStatusText
{
    /// <summary>The Portuguese word for a status.</summary>
    /// <param name="status">The status as the domain spells it.</param>
    /// <returns>What to render.</returns>
    public static string Of(string? status) => status switch
    {
        "Draft" => "rascunho",
        "Active" => "ativa",
        "Discontinued" => "descontinuada",
        "Obsolete" => "obsoleta",

        // Not "desconhecido". A status this method has not been taught is shown as it is, so
        // whoever sees it can say which word appeared rather than which word did not.
        _ => status ?? "—",
    };

    /// <summary>
    /// The tag class a status wears.
    /// <para>
    /// Only "active" is green. A part out of line is not an error and should not be red — it is
    /// simply not for sale, and a screen that alarms about ordinary facts is a screen people
    /// stop reading.
    /// </para>
    /// </summary>
    /// <param name="status">The status as the domain spells it.</param>
    /// <returns>The CSS class.</returns>
    public static string Tone(string? status) => status switch
    {
        "Active" => "tag-ok",
        "Draft" => "tag-wn",
        _ => string.Empty,
    };
}
