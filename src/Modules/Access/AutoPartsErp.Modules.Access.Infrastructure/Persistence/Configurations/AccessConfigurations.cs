using AutoPartsErp.Modules.Access.Domain;
using AutoPartsErp.Modules.Access.Domain.Roles;
using AutoPartsErp.Modules.Access.Domain.Sessions;
using AutoPartsErp.Modules.Access.Domain.Users;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AutoPartsErp.Modules.Access.Infrastructure.Persistence.Configurations;

/// <summary>Maps <see cref="User"/> onto <c>access.users</c>.</summary>
public sealed class UserConfiguration : IEntityTypeConfiguration<User>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<User> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("users");

        builder.HasKey(user => user.Id);

        builder.Property(user => user.Id)
            .HasConversion(id => id.Value, value => new UserId(value))
            .ValueGeneratedNever();

        builder.Property(user => user.Version)
            .IsRowVersion()
            .HasColumnName("xmin")
            .HasColumnType("xid");

        builder.Property(user => user.TenantId).IsRequired();

        builder.Property(user => user.Email)
            .HasMaxLength(User.MaxEmailLength)
            .IsRequired();

        builder.Property(user => user.DisplayName)
            .HasMaxLength(User.MaxDisplayNameLength)
            .IsRequired();

        // No length limit worth setting: the stored format carries a version byte, and the day
        // the hashing parameters change the string gets longer. A column sized to today's
        // algorithm is a truncation waiting for that day.
        builder.Property(user => user.PasswordHash).IsRequired();

        builder.Property(user => user.IsActive).IsRequired();
        builder.Property(user => user.MustChangePassword).IsRequired();
        builder.Property(user => user.FailedAttempts).IsRequired();
        builder.Property(user => user.LockedUntilUtc);
        builder.Property(user => user.LastSignedInAtUtc);

        builder.Property(user => user.CreatedAtUtc).IsRequired();
        builder.Property(user => user.CreatedBy).HasMaxLength(120).IsRequired();
        builder.Property(user => user.ModifiedBy).HasMaxLength(120);

        builder.OwnsMany(user => user.Roles, role =>
        {
            role.ToTable("user_roles");
            role.WithOwner().HasForeignKey("user_id");

            role.Property(item => item.RoleId)
                .HasConversion(id => id.Value, value => new RoleId(value))
                .HasColumnName("role_id")
                .IsRequired();

            role.HasKey("user_id", "RoleId");

            // "Who holds this role?" - asked before deleting one, and by the check that stops a
            // company removing its last administrator.
            role.HasIndex(item => item.RoleId).HasDatabaseName("ix_user_roles_role");
        });

        builder.Navigation(user => user.Roles).UsePropertyAccessMode(PropertyAccessMode.Field);

        // One account per address per company, enforced here rather than by the handler's check
        // alone: two people registering the same address at once would otherwise both find it
        // free and both insert.
        builder.HasIndex(user => new { user.TenantId, user.Email })
            .IsUnique()
            .HasDatabaseName("ux_users_tenant_email");
    }
}

/// <summary>Maps <see cref="Role"/> onto <c>access.roles</c>.</summary>
public sealed class RoleConfiguration : IEntityTypeConfiguration<Role>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<Role> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("roles");

        builder.HasKey(role => role.Id);

        builder.Property(role => role.Id)
            .HasConversion(id => id.Value, value => new RoleId(value))
            .ValueGeneratedNever();

        builder.Property(role => role.Version)
            .IsRowVersion()
            .HasColumnName("xmin")
            .HasColumnType("xid");

        builder.Property(role => role.TenantId).IsRequired();

        builder.Property(role => role.Code).HasMaxLength(Role.MaxCodeLength).IsRequired();
        builder.Property(role => role.Name).HasMaxLength(Role.MaxNameLength).IsRequired();
        builder.Property(role => role.Description).HasMaxLength(Role.MaxDescriptionLength);
        builder.Property(role => role.IsSystem).IsRequired();

        builder.Property(role => role.CreatedAtUtc).IsRequired();
        builder.Property(role => role.CreatedBy).HasMaxLength(120).IsRequired();
        builder.Property(role => role.ModifiedBy).HasMaxLength(120);

        // A child table rather than a text[] column or a joined string, and the same owned-
        // collection shape as every other child in this system. EF 8's primitive collections were
        // the obvious fit and are not one: they map no navigation, so there is nothing to set the
        // field access mode on, and they have no reliable mapping for a set.
        //
        // The table also earns its keep. "Which roles can issue invoices" is a WHERE clause here
        // and a scan of every role in the other two designs.
        builder.OwnsMany(role => role.Permissions, permission =>
        {
            permission.ToTable("role_permissions");
            permission.WithOwner().HasForeignKey("role_id");

            permission.Property(item => item.Name)
                .HasColumnName("permission")
                .HasMaxLength(80)
                .IsRequired();

            permission.HasKey("role_id", "Name");

            permission.HasIndex(item => item.Name)
                .HasDatabaseName("ix_role_permissions_permission");
        });

        builder.Navigation(role => role.Permissions)
            .UsePropertyAccessMode(PropertyAccessMode.Field);

        builder.HasIndex(role => new { role.TenantId, role.Code })
            .IsUnique()
            .HasDatabaseName("ux_roles_tenant_code");
    }
}

/// <summary>Maps <see cref="RefreshToken"/> onto <c>access.refresh_tokens</c>.</summary>
public sealed class RefreshTokenConfiguration : IEntityTypeConfiguration<RefreshToken>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<RefreshToken> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("refresh_tokens");

        builder.HasKey(token => token.Id);
        builder.Property(token => token.Id).ValueGeneratedNever();

        builder.Property(token => token.TenantId).IsRequired();

        builder.Property(token => token.UserId)
            .HasConversion(id => id.Value, value => new UserId(value))
            .HasColumnName("user_id")
            .IsRequired();

        builder.Property(token => token.TokenHash)
            .HasColumnName("token_hash")
            .HasMaxLength(64)
            .IsRequired();

        builder.Property(token => token.IssuedAtUtc).IsRequired();
        builder.Property(token => token.ExpiresAtUtc).IsRequired();
        builder.Property(token => token.RevokedAtUtc);

        // The lookup every renewal makes, and it has to be unique: two rows with one hash would
        // make "has this handle already been used" ambiguous, which is the exact question the
        // stolen-token check turns on.
        builder.HasIndex(token => token.TokenHash)
            .IsUnique()
            .HasDatabaseName("ux_refresh_tokens_hash");

        // Ending every session a user has, on a password change or a lockout.
        builder.HasIndex(token => new { token.UserId, token.RevokedAtUtc })
            .HasDatabaseName("ix_refresh_tokens_user_live");

        // The purge.
        builder.HasIndex(token => token.ExpiresAtUtc)
            .HasDatabaseName("ix_refresh_tokens_expiry");
    }
}
