using AutoPartsErp.Modules.Catalog.Domain.Parts;

namespace AutoPartsErp.Web.Tests;

/// <summary>
/// The one place that turns a domain status into a word somebody reads.
/// </summary>
public sealed class PartStatusTextTests
{
    /// <summary>Each status has its word.</summary>
    [Theory]
    [InlineData("Draft", "rascunho")]
    [InlineData("Active", "ativa")]
    [InlineData("Discontinued", "descontinuada")]
    [InlineData("Obsolete", "obsoleta")]
    public void A_status_reads_in_portuguese(string status, string word)
    {
        PartStatusText.Of(status).Should().Be(word);
    }

    /// <summary>
    /// Every status the domain has is translated here.
    /// <para>
    /// This is the test that earns the class. Adding a status to the enum compiles, ships, and
    /// shows an English word on a Portuguese screen — there is nothing in the language that would
    /// object. So the enum is walked rather than spelled out.
    /// </para>
    /// </summary>
    [Fact]
    public void Every_status_the_domain_has_is_translated()
    {
        foreach (PartStatus status in Enum.GetValues<PartStatus>())
        {
            if (status == PartStatus.Unknown)
            {
                continue;
            }

            string name = status.ToString();

            PartStatusText.Of(name).Should().NotBe(name);
        }
    }

    /// <summary>
    /// A word nothing recognizes is shown as it is, rather than as "desconhecido". Whoever sees
    /// it can then say which word appeared, which is the only useful thing about a bug like that.
    /// </summary>
    [Fact]
    public void An_unrecognized_status_is_shown_as_it_is()
    {
        PartStatusText.Of("Frozen").Should().Be("Frozen");
    }

    /// <summary>Nothing at all renders as a dash, not as an empty cell.</summary>
    [Fact]
    public void Nothing_renders_as_a_dash()
    {
        PartStatusText.Of(null).Should().Be("—");
    }

    /// <summary>
    /// Only a part that can be sold is green, and nothing is red.
    /// <para>
    /// A part out of line is not an error. A screen that alarms about ordinary facts is a screen
    /// people stop reading, and then it cannot alarm them about anything.
    /// </para>
    /// </summary>
    [Fact]
    public void Only_a_sellable_part_is_green()
    {
        PartStatusText.Tone("Active").Should().Be("tag-ok");
        PartStatusText.Tone("Draft").Should().Be("tag-wn");
        PartStatusText.Tone("Discontinued").Should().BeEmpty();
        PartStatusText.Tone("Obsolete").Should().BeEmpty();
        PartStatusText.Tone(null).Should().BeEmpty();
    }
}
