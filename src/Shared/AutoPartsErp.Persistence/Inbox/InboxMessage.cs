using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AutoPartsErp.Persistence.Inbox;

/// <summary>
/// A record that one handler has already dealt with one message.
/// <para>
/// The outbox guarantees at-least-once delivery, which means duplicates are not an edge case but
/// a design consequence: a handler that succeeds and then loses its connection before the row is
/// marked processed will be called again. Without this table, that redelivery receives the same
/// goods twice.
/// </para>
/// <para>
/// It lives in the <i>consuming</i> module's schema, not the publisher's, and is written by the
/// same <c>DbContext</c> as the handler's own changes. Anywhere else and the two would commit
/// separately — which is the same crash window the outbox was built to close, moved one step
/// down the pipe.
/// </para>
/// </summary>
public sealed class InboxMessage
{
    /// <summary>The outbox message's identity.</summary>
    public Guid MessageId { get; set; }

    /// <summary>
    /// The handler that dealt with it. Part of the key because two modules subscribing to the
    /// same event must each get their own turn — one having handled it says nothing about the other.
    /// </summary>
    public string HandlerName { get; set; } = string.Empty;

    /// <summary>When it was handled.</summary>
    public DateTimeOffset HandledAtUtc { get; set; }
}

/// <summary>Maps <see cref="InboxMessage"/> onto <c>inbox_messages</c> in the module's schema.</summary>
public sealed class InboxMessageConfiguration : IEntityTypeConfiguration<InboxMessage>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<InboxMessage> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("inbox_messages");

        builder.HasKey(message => new { message.MessageId, message.HandlerName });

        builder.Property(message => message.HandlerName).HasMaxLength(300);
        builder.Property(message => message.HandledAtUtc).IsRequired();

        // Nothing prunes this table yet. It grows one row per handled message per handler, so a
        // job that deletes rows older than the outbox retention is on the list before this sees
        // real volume.
        builder.HasIndex(message => message.HandledAtUtc)
            .HasDatabaseName("ix_inbox_messages_handled_at");
    }
}
