using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AutoPartsErp.Persistence.Numbering;

/// <summary>
/// A counter that hands out the next number in a run, one row per tenant, per kind, per year.
/// <para>
/// This replaces reading the highest number already taken and adding one. That worked until two
/// people did it at the same moment, at which point both read the same highest number and both
/// got the same next one — and nothing complained, because the number is only unique by
/// construction and the construction had just failed. The order that arrives second overwrites
/// nothing and looks perfectly normal; the two documents are simply both called SO-2026-00042.
/// </para>
/// <para>
/// One of these tables lives in every module's schema, for the same reason the outbox does: the
/// increment has to happen in the same transaction as the row that uses the number, and a table
/// in somebody else's schema cannot promise that.
/// </para>
/// <para>
/// Not a domain object, and deliberately not an aggregate. It has no behaviour because all of its
/// behaviour is one SQL statement — see <c>ModuleDbContext.TakeNextNumberAsync</c> — and wrapping
/// that in a fake aggregate would only give the impression it could be used from C# safely.
/// </para>
/// </summary>
public sealed class NumberSequence
{
    /// <summary>The tenant this run of numbers belongs to.</summary>
    public Guid TenantId { get; set; }

    /// <summary>
    /// What is being numbered — <c>sales-order</c>, <c>purchase-order</c>.
    /// <para>
    /// Named <c>sequence_key</c> rather than <c>key</c> so that no quoting question ever arises
    /// around it in hand-written SQL, which is what the only statement that touches this table is.
    /// </para>
    /// </summary>
    public string SequenceKey { get; set; } = string.Empty;

    /// <summary>
    /// The year the run belongs to. Numbering restarts each year, which is what puts the year in
    /// the document number in the first place.
    /// </summary>
    public int Year { get; set; }

    /// <summary>
    /// The number the next caller will take.
    /// <para>
    /// Points at the <i>next</i> one rather than the last one taken, so that a run that has never
    /// been used and a run whose first number was taken are told apart by the row existing rather
    /// than by a sentinel value.
    /// </para>
    /// </summary>
    public int NextNumber { get; set; }
}

/// <summary>Maps <see cref="NumberSequence"/> onto <c>number_sequences</c> in the module's schema.</summary>
public sealed class NumberSequenceConfiguration : IEntityTypeConfiguration<NumberSequence>
{
    /// <summary>Longest sequence key. These are written by hand in one place each.</summary>
    public const int MaxKeyLength = 40;

    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<NumberSequence> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("number_sequences");

        // A composite natural key rather than a surrogate one with a unique index beside it. The
        // three columns are what identifies a run of numbers, and the upsert that increments it
        // needs exactly this constraint to conflict on — so it is the primary key, and there is
        // no second constraint that could be dropped without the upsert noticing.
        builder.HasKey(sequence => new
        {
            sequence.TenantId,
            sequence.SequenceKey,
            sequence.Year,
        });

        builder.Property(sequence => sequence.SequenceKey)
            .HasMaxLength(MaxKeyLength)
            .IsRequired();

        builder.Property(sequence => sequence.NextNumber).IsRequired();
    }
}
