using AutoPartsErp.SharedKernel.Abstractions;
using AutoPartsErp.SharedKernel.Primitives;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace AutoPartsErp.Modules.Inventory.Infrastructure.Persistence;

/// <summary>
/// Stamps who changed what and when, and assigns the tenant on insert.
/// <para>
/// Each module owns its own interceptor rather than sharing one, because it is registered against
/// that module's context. The behaviour is deliberately identical to Catalog's — if a third module
/// repeats it again, that is the point to lift it into the shared kernel.
/// </para>
/// <para>
/// Unlike Catalog's, this one does not convert deletes into archival updates. Nothing in Inventory
/// implements soft delete except warehouses and bins, and stock rows are never deleted at all.
/// </para>
/// </summary>
public sealed class InventoryAuditingInterceptor : SaveChangesInterceptor
{
    private readonly ICurrentUser _currentUser;
    private readonly ITenantContext _tenantContext;
    private readonly IDateTimeProvider _clock;

    /// <summary>Initializes the interceptor.</summary>
    public InventoryAuditingInterceptor(
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

            if (entry.Entity is not IAuditable auditable)
            {
                continue;
            }

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

        foreach (EntityEntry entry in context.ChangeTracker.Entries())
        {
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
