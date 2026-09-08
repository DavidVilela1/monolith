using AutoPartsErp.Modules.Access.Domain;
using AutoPartsErp.Modules.Access.Domain.Roles;
using AutoPartsErp.Modules.Access.Domain.Sessions;
using AutoPartsErp.Modules.Access.Domain.Users;
using AutoPartsErp.Persistence;
using Microsoft.EntityFrameworkCore;

namespace AutoPartsErp.Modules.Access.Infrastructure.Persistence;

/// <summary>
/// The Access module's database context, scoped to the <c>access</c> schema.
/// <para>
/// Its own schema like every other module, and the tenant filter matters more here than anywhere:
/// a query that forgot it would let one company administer another company's users.
/// </para>
/// </summary>
public sealed class AccessDbContext : ModuleDbContext, IAccessUnitOfWork
{
    /// <summary>The PostgreSQL schema this context owns.</summary>
    public const string SchemaName = "access";

    /// <summary>Initializes the context.</summary>
    /// <param name="options">EF Core options, supplied by the container.</param>
    /// <param name="dependencies">Shared plumbing: the tenant, domain events and the outbox.</param>
    public AccessDbContext(
        DbContextOptions<AccessDbContext> options,
        ModuleDbContextDependencies? dependencies = null)
        : base(options, dependencies)
    {
    }

    /// <summary>The people who may use this system.</summary>
    public DbSet<User> Users => Set<User>();

    /// <summary>What jobs exist and what they may do.</summary>
    public DbSet<Role> Roles => Set<Role>();

    /// <summary>Live sessions, as hashed handles.</summary>
    public DbSet<RefreshToken> RefreshTokens => Set<RefreshToken>();

    /// <inheritdoc />
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);

        modelBuilder.HasDefaultSchema(SchemaName);
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(AccessDbContext).Assembly);

        modelBuilder.Entity<User>()
            .HasQueryFilter(user => user.TenantId == CurrentTenantId);

        modelBuilder.Entity<Role>()
            .HasQueryFilter(role => role.TenantId == CurrentTenantId);

        modelBuilder.Entity<RefreshToken>()
            .HasQueryFilter(token => token.TenantId == CurrentTenantId);

        base.OnModelCreating(modelBuilder);
    }
}
