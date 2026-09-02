using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AutoPartsErp.Persistence.Outbox;

/// <summary>
/// An integration event that has been committed but not yet delivered.
/// <para>
/// One of these tables lives in every module's schema, because the row has to be written by the
/// same <c>DbContext</c> — and therefore the same transaction — as the change it describes. A
/// single shared outbox would defeat the whole mechanism: the write would be a second
/// transaction, and a crash between the two would lose exactly what this exists to protect.
/// </para>
/// <para>
/// Not a domain object. It has public setters and no behaviour because it is a queue row, and
/// pretending otherwise would put a fake aggregate in the middle of the plumbing.
/// </para>
/// </summary>
public sealed class OutboxMessage
{
    /// <summary>
    /// The event's own identity, reused as the row's primary key.
    /// <para>
    /// Not a fresh Guid: consumers deduplicate on this, so it has to be the same value the
    /// publisher stamped on the event rather than something the outbox invented.
    /// </para>
    /// </summary>
    public Guid Id { get; set; }

    /// <summary>The event's contract name, used to rebuild it. Effectively part of the public API.</summary>
    public string Type { get; set; } = string.Empty;

    /// <summary>The serialized event.</summary>
    public string Content { get; set; } = string.Empty;

    /// <summary>The tenant the event belongs to, carried so handlers do not have to guess.</summary>
    public Guid TenantId { get; set; }

    /// <summary>When the event happened.</summary>
    public DateTimeOffset OccurredAtUtc { get; set; }

    /// <summary>When every handler finished with it. Null while it is still owed delivery.</summary>
    public DateTimeOffset? ProcessedAtUtc { get; set; }

    /// <summary>How many delivery attempts have failed.</summary>
    public int Attempts { get; set; }

    /// <summary>
    /// The last failure, kept because "why has this not been delivered?" is otherwise
    /// unanswerable from the database alone.
    /// </summary>
    public string? Error { get; set; }

    /// <summary>The earliest time to try again. Null means try on the next sweep.</summary>
    public DateTimeOffset? NextAttemptAtUtc { get; set; }
}

/// <summary>Maps <see cref="OutboxMessage"/> onto <c>outbox_messages</c> in the module's schema.</summary>
public sealed class OutboxMessageConfiguration : IEntityTypeConfiguration<OutboxMessage>
{
    /// <summary>Longest error text kept. A stack trace is useful; a novel is not.</summary>
    public const int MaxErrorLength = 4000;

    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<OutboxMessage> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("outbox_messages");

        builder.HasKey(message => message.Id);
        builder.Property(message => message.Id).ValueGeneratedNever();

        builder.Property(message => message.Type).HasMaxLength(300).IsRequired();

        builder.Property(message => message.Content)
            .HasColumnType("jsonb")
            .IsRequired();

        builder.Property(message => message.TenantId).IsRequired();
        builder.Property(message => message.OccurredAtUtc).IsRequired();
        builder.Property(message => message.Error).HasMaxLength(MaxErrorLength);

        // The processor's poll, and the only query that runs against this table in normal
        // operation. Partial, so the index stays the size of the backlog rather than the size
        // of every event the system has ever published.
        builder.HasIndex(message => new { message.NextAttemptAtUtc, message.OccurredAtUtc })
            .HasFilter("processed_at_utc IS NULL")
            .HasDatabaseName("ix_outbox_messages_pending");
    }
}
