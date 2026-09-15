using AutoPartsErp.SharedKernel.Primitives;
using AutoPartsErp.SharedKernel.ValueObjects;

namespace AutoPartsErp.Modules.Finance.Domain.Ledger;

/// <summary>
/// One line of a journal entry: an account, a side, and an amount.
/// <para>
/// The account code is copied onto the line, not joined. A trial balance printed in March has to
/// keep reading the same way in December, and an account renamed in between would silently
/// restate every report that ever showed it. The identifier is there for the queries that want to
/// follow the tree; the code is there for the paper.
/// </para>
/// <para>
/// The amount is always positive and the side carries the direction. A negative debit is the same
/// fact as a credit written in a way the other half of the ledger cannot see, and once one is in
/// the data every sum has to know to look for it.
/// </para>
/// </summary>
public sealed class JournalLine : Entity<JournalLineId>, ITenantScoped
{
    private JournalLine(
        JournalLineId id,
        AccountId accountId,
        string accountCode,
        EntrySide side,
        Money amount,
        string? narrative)
        : base(id)
    {
        AccountId = accountId;
        AccountCode = accountCode;
        Side = side;
        Amount = amount;
        Narrative = narrative;
    }

    /// <summary>Required by EF Core materialization.</summary>
#pragma warning disable CS8618
    private JournalLine()
    {
    }
#pragma warning restore CS8618

    /// <summary>The account it lands on.</summary>
    public AccountId AccountId { get; private set; }

    /// <summary>That account's code, copied at the moment of the entry.</summary>
    public string AccountCode { get; private set; } = string.Empty;

    /// <summary>Which side it lands on.</summary>
    public EntrySide Side { get; private set; }

    /// <summary>How much. Positive; the side carries the direction.</summary>
    public Money Amount { get; private set; } = null!;

    /// <summary>What this line in particular is for, when the entry's own description is not enough.</summary>
    public string? Narrative { get; private set; }

    /// <inheritdoc />
    public Guid TenantId { get; set; }

    /// <summary>What this line contributes to the account's balance, signed for the debit side.</summary>
    public Money SignedAmount => Side == EntrySide.Debit ? Amount : Amount.Negate();

    /// <summary>Creates a line. Called by <see cref="JournalEntry"/>, never directly.</summary>
    /// <param name="accountId">The account.</param>
    /// <param name="accountCode">Its code.</param>
    /// <param name="side">Debit or credit.</param>
    /// <param name="amount">How much.</param>
    /// <param name="narrative">What this line is for.</param>
    internal static JournalLine Create(
        AccountId accountId,
        string accountCode,
        EntrySide side,
        Money amount,
        string? narrative) =>
        new(JournalLineId.New(), accountId, accountCode, side, amount, narrative);
}
