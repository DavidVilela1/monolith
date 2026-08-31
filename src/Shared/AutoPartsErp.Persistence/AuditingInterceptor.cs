using AutoPartsErp.SharedKernel.Abstractions;
using AutoPartsErp.SharedKernel.Primitives;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace AutoPartsErp.Persistence;

/// <summary>
/// Stamps who changed what and when, assigns the tenant on insert, and turns deletes into
/// archival updates.
/// <para>
/// One implementation for every module. It was written three times — once each in Catalog,
/// Inventory and Partners — and never varied, which is what made lifting it here the right call
/// rather than a premature abstraction. It works off the marker interfaces in the shared kernel,
/// so it needs to know nothing about any module's entities.
/// </para>
/// <para>
/// Doing this in an interceptor rather than in each handler means it cannot be forgotten. In an
/// ERP the audit trail is what an auditor asks for, and what answers "who changed this price at
/// 4am?" three months later.
/// </para>
/// </summary>
public sealed class AuditingInterceptor : SaveChangesInterceptor
{
    private readonly ICurrentUser _currentUser;
    private readonly ITenantContext _tenantContext;
    private readonly IDateTimeProvider _clock;

    /// <summary>Initializes the interceptor.</summary>
    public AuditingInterceptor(
        ICurrentUser currentUser,
        ITenantContext tenantContext,
        IDateTimeProvider clock)
    {
        _currentUser = currentUser;
        _tenantContext = tenantContext;
        _clock = clock;
    }

    /// <inheritdoc />
    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData,
        InterceptionResult<int> result,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(eventData);

        if (eventData.Context is not null)
        {
            Apply(eventData.Context);
        }

        return base.SavingChangesAsync(eventData, result, cancellationToken);
    }

    /// <inheritdoc />
    public override InterceptionResult<int> SavingChanges(
        DbContextEventData eventData,
        InterceptionResult<int> result)
    {
        ArgumentNullException.ThrowIfNull(eventData);

        if (eventData.Context is not null)
        {
            Apply(eventData.Context);
        }

        return base.SavingChanges(eventData, result);
    }

    private void Apply(DbContext context)
    {
        DateTimeOffset now = _clock.UtcNow;
        string user = _currentUser.UserId;

        foreach (EntityEntry entry in context.ChangeTracker.Entries())
        {
            if (entry.Entity is ITenantScoped tenantScoped && entry.State == EntityState.Added)
            {
                tenantScoped.TenantId = _tenantContext.TenantId;
            }

            if (entry.Entity is IAuditable auditable)
            {
                switch (entry.State)
                {
                    case EntityState.Added:
                        auditable.CreatedAtUtc = now;
                        auditable.CreatedBy = user;
                        break;

                    case EntityState.Modified:
                        auditable.ModifiedAtUtc = now;
                        auditable.ModifiedBy = user;
                        break;

                    default:
                        break;
                }
            }

            // A record referenced by ten years of documents is never physically removed.
            // Entities that do not implement ISoftDeletable — stock movements, for instance —
            // fall through and are deleted normally, which is correct: nothing deletes those.
            if (entry.State == EntityState.Deleted && entry.Entity is ISoftDeletable deletable)
            {
                entry.State = EntityState.Modified;
                deletable.IsDeleted = true;
                deletable.DeletedAtUtc = now;
                deletable.DeletedBy = user;
            }
        }
    }
}
