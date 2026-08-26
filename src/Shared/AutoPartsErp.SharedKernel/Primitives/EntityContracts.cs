namespace AutoPartsErp.SharedKernel.Primitives;

/// <summary>
/// Marks an entity whose creation and last modification are tracked automatically.
/// The infrastructure layer fills these in on <c>SaveChanges</c>; domain code never sets them.
/// </summary>
public interface IAuditable
{
    /// <summary>When the row was first written, in UTC.</summary>
    DateTimeOffset CreatedAtUtc { get; set; }

    /// <summary>Identifier of the user or process that created the row.</summary>
    string CreatedBy { get; set; }

    /// <summary>When the row was last changed, in UTC. Null until the first update.</summary>
    DateTimeOffset? ModifiedAtUtc { get; set; }

    /// <summary>Identifier of the user or process that last changed the row.</summary>
    string? ModifiedBy { get; set; }
}

/// <summary>
/// Marks an entity that is never physically deleted. ERP records are referenced by
/// documents, ledgers and audit trails, so deletion is a state change, not a DELETE.
/// A global query filter hides soft-deleted rows from every normal query.
/// </summary>
public interface ISoftDeletable
{
    /// <summary>True once the record has been archived.</summary>
    bool IsDeleted { get; set; }

    /// <summary>When the record was archived, in UTC.</summary>
    DateTimeOffset? DeletedAtUtc { get; set; }

    /// <summary>Identifier of the user or process that archived the record.</summary>
    string? DeletedBy { get; set; }
}

/// <summary>
/// Marks an entity that belongs to exactly one tenant (a legal entity / operating company).
/// A global query filter scopes every query to the current tenant, so a multi-company
/// deployment cannot leak data between branches by forgetting a WHERE clause.
/// </summary>
public interface ITenantScoped
{
    /// <summary>The owning tenant.</summary>
    Guid TenantId { get; set; }
}
