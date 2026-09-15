using AutoPartsErp.SharedKernel.Primitives;
using AutoPartsErp.SharedKernel.Results;

namespace AutoPartsErp.Modules.Finance.Domain.Ledger;

/// <summary>
/// What kind of thing an account measures, which is what decides the side it grows on.
/// </summary>
public enum AccountType
{
    /// <summary>Not set.</summary>
    Unknown = 0,

    /// <summary>Something the company has. Grows on the debit side.</summary>
    Asset = 1,

    /// <summary>Something the company owes. Grows on the credit side.</summary>
    Liability = 2,

    /// <summary>What the owners have in it. Grows on the credit side.</summary>
    Equity = 3,

    /// <summary>Money earned. Grows on the credit side.</summary>
    Income = 4,

    /// <summary>Money spent. Grows on the debit side.</summary>
    Expense = 5,
}

/// <summary>
/// One line of the chart of accounts.
/// <para>
/// The chart is a tree for reading and a flat list for posting: a group account exists so a trial
/// balance can be folded up into something a person reads, and a posting account is where an entry
/// actually lands. Nothing posts to a group, because a balance that is partly its own and partly
/// the sum of its children is one nobody can take apart again.
/// </para>
/// <para>
/// <b>No chart is shipped with this.</b> Portugal's SNC has a standard one and a distributor's
/// accountant will have opinions about the sub-accounts within it, so the structure is here and
/// the codes are data. Guessing them would produce a chart that looks official and reconciles to
/// nobody's expectations.
/// </para>
/// </summary>
public sealed class Account : AggregateRoot<AccountId>, IAuditable, ITenantScoped
{
    /// <summary>Longest permitted account code.</summary>
    public const int MaxCodeLength = 20;

    /// <summary>Longest permitted account name.</summary>
    public const int MaxNameLength = 160;

    private Account(
        AccountId id,
        string code,
        string name,
        AccountType type,
        bool allowsPosting,
        AccountId? parentId)
        : base(id)
    {
        Code = code;
        Name = name;
        Type = type;
        AllowsPosting = allowsPosting;
        ParentId = parentId;
        IsActive = true;
        CreatedBy = string.Empty;
    }

    /// <summary>Required by EF Core materialization.</summary>
#pragma warning disable CS8618
    private Account()
    {
    }
#pragma warning restore CS8618

    /// <summary>The code the accountant refers to it by. Unique within a tenant.</summary>
    public string Code { get; private set; } = string.Empty;

    /// <summary>What it is called on a trial balance.</summary>
    public string Name { get; private set; } = string.Empty;

    /// <summary>What kind of thing it measures.</summary>
    public AccountType Type { get; private set; }

    /// <summary>
    /// True when entries may land on it directly.
    /// <para>
    /// False for a group account, which exists to be summed. A balance that is partly its own
    /// postings and partly the total of its children is one nobody can take apart again.
    /// </para>
    /// </summary>
    public bool AllowsPosting { get; private set; }

    /// <summary>The account above it in the chart, or null at the top.</summary>
    public AccountId? ParentId { get; private set; }

    /// <summary>
    /// False once it is no longer used. It is never deleted.
    /// <para>
    /// Every entry ever posted to it still points here, and a trial balance for last year has to
    /// be readable next year. Deleting an account is deleting the name of a number somebody has
    /// already reported to the tax authority.
    /// </para>
    /// </summary>
    public bool IsActive { get; private set; }

    /// <inheritdoc />
    public Guid TenantId { get; set; }

    /// <inheritdoc />
    public DateTimeOffset CreatedAtUtc { get; set; }

    /// <inheritdoc />
    public string CreatedBy { get; set; } = string.Empty;

    /// <inheritdoc />
    public DateTimeOffset? ModifiedAtUtc { get; set; }

    /// <inheritdoc />
    public string? ModifiedBy { get; set; }

    /// <summary>
    /// The side this account grows on.
    /// <para>
    /// Not a stored field, because it is a fact about the kind and storing it would let the two
    /// disagree. An asset that grew on the credit side would make every report built on it wrong
    /// in a direction nobody checks.
    /// </para>
    /// </summary>
    public EntrySide NormalSide => Type switch
    {
        AccountType.Asset or AccountType.Expense => EntrySide.Debit,
        AccountType.Liability or AccountType.Equity or AccountType.Income => EntrySide.Credit,
        _ => EntrySide.Debit,
    };

    /// <summary>Opens an account.</summary>
    /// <param name="code">The code the accountant refers to it by.</param>
    /// <param name="name">What it is called.</param>
    /// <param name="type">What kind of thing it measures.</param>
    /// <param name="allowsPosting">False for a group account that exists to be summed.</param>
    /// <param name="parentId">The account above it, or null at the top.</param>
    public static Result<Account> Open(
        string? code,
        string? name,
        AccountType type,
        bool allowsPosting = true,
        AccountId? parentId = null)
    {
        if (string.IsNullOrWhiteSpace(code))
        {
            return FinanceErrors.Account.CodeRequired;
        }

        if (code.Trim().Length > MaxCodeLength)
        {
            return FinanceErrors.Account.CodeTooLong;
        }

        if (string.IsNullOrWhiteSpace(name))
        {
            return FinanceErrors.Account.NameRequired;
        }

        if (name.Trim().Length > MaxNameLength)
        {
            return FinanceErrors.Account.NameTooLong;
        }

        if (type == AccountType.Unknown)
        {
            return FinanceErrors.Account.TypeRequired;
        }

        return new Account(
            AccountId.New(),
            code.Trim().ToUpperInvariant(),
            name.Trim(),
            type,
            allowsPosting,
            parentId);
    }

    /// <summary>Renames the account. The code and the type do not move.</summary>
    /// <param name="name">The new name.</param>
    public Result Rename(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return FinanceErrors.Account.NameRequired;
        }

        if (name.Trim().Length > MaxNameLength)
        {
            return FinanceErrors.Account.NameTooLong;
        }

        Name = name.Trim();

        return Result.Success();
    }

    /// <summary>
    /// Stops the account taking new entries. What is already on it stays exactly where it is.
    /// </summary>
    public void Deactivate() => IsActive = false;

    /// <summary>Puts it back in use.</summary>
    public void Reactivate() => IsActive = true;

    /// <summary>True when an entry may land on it today.</summary>
    public bool CanTakePostings => IsActive && AllowsPosting;
}
