using AutoPartsErp.Modules.Access.Application;
using AutoPartsErp.Modules.Access.Domain;
using AutoPartsErp.Modules.Access.Domain.Roles;
using AutoPartsErp.Modules.Access.Domain.Users;
using AutoPartsErp.Modules.Access.Infrastructure.Security;
using AutoPartsErp.SharedKernel.Authorization;
using AutoPartsErp.SharedKernel.Results;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AutoPartsErp.Modules.Access.Infrastructure.Persistence.Seed;

/// <summary>
/// Creates the roles a company starts with, and the first administrator.
/// <para>
/// The one seeder in this system that runs outside Development, and it has to: a database with
/// no users is a system nobody can sign in to, and the screen that would create the first user is
/// behind the sign-in. Every other module's tables can start empty because a person fills them.
/// </para>
/// <para>
/// It does nothing at all once one user exists. A company with an administrator has somebody who
/// can create the rest, and a seeder that kept adding accounts afterwards would be a back door.
/// </para>
/// </summary>
public sealed class AccessSeeder
{
    /// <summary>The code of the role that can do everything.</summary>
    public const string AdministratorRoleCode = "ADMIN";

    private readonly AccessDbContext _context;
    private readonly IPasswordHasher _passwords;
    private readonly AccessOptions _options;
    private readonly ILogger<AccessSeeder> _logger;

    /// <summary>Initializes the seeder.</summary>
    public AccessSeeder(
        AccessDbContext context,
        IPasswordHasher passwords,
        IOptions<AccessOptions> options,
        ILogger<AccessSeeder> logger)
    {
        ArgumentNullException.ThrowIfNull(options);

        _context = context;
        _passwords = passwords;
        _options = options.Value;
        _logger = logger;
    }

    /// <summary>Puts the starting roles and the first administrator in place.</summary>
    public async Task SeedAsync(CancellationToken cancellationToken = default)
    {
        Role administrator = await EnsureRolesAsync(cancellationToken).ConfigureAwait(false);

        if (await _context.Users.AnyAsync(cancellationToken).ConfigureAwait(false))
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(_options.BootstrapAdminEmail)
            || string.IsNullOrWhiteSpace(_options.BootstrapAdminPassword))
        {
            // Loud, because the alternative is a system that starts cleanly and cannot be used,
            // and whoever installed it finding that out at the login screen.
            _logger.LogWarning(
                "There are no users and no bootstrap administrator is configured. Set "
                + "{Section}:BootstrapAdminEmail and {Section}:BootstrapAdminPassword, or nobody "
                + "will be able to sign in.",
                AccessOptions.SectionName,
                AccessOptions.SectionName);

            return;
        }

        Result<User> admin = User.Create(
            _options.BootstrapAdminEmail,
            "Administrator",
            _passwords.Hash(_options.BootstrapAdminPassword),
            mustChangePassword: true);

        if (admin.IsFailure)
        {
            _logger.LogWarning(
                "The configured bootstrap administrator could not be created: {Error}",
                admin.Error.Description);

            return;
        }

        admin.Value.SetRoles([administrator.Id]);
        _context.Users.Add(admin.Value);

        await _context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        _logger.LogWarning(
            "Created the first administrator, {Email}. The password came from configuration and "
            + "must be changed on first sign-in.",
            _options.BootstrapAdminEmail);
    }

    /// <summary>
    /// Creates the roles a parts distributor actually has, if they are not there.
    /// <para>
    /// Real jobs rather than tidy abstractions. Every one of these is a person somebody in a
    /// branch could point at, and the permissions on each are what that person's day needs and
    /// nothing else — a counter salesperson who can issue an invoice but not void one, a buyer
    /// who can commit money to a supplier but cannot touch a price list.
    /// </para>
    /// <para>
    /// They are a starting point, not a policy. Every one of them except the administrator can be
    /// renamed, re-permissioned or deleted, because no two distributors divide the work the same
    /// way.
    /// </para>
    /// </summary>
    private async Task<Role> EnsureRolesAsync(CancellationToken cancellationToken)
    {
        Role? administrator = await _context.Roles
            .FirstOrDefaultAsync(role => role.Code == AdministratorRoleCode, cancellationToken)
            .ConfigureAwait(false);

        if (administrator is not null)
        {
            return administrator;
        }

        administrator = Create(
            AdministratorRoleCode,
            "Administrator",
            "Everything, including who else may do what.",
            [.. Permissions.All],
            isSystem: true);

        Role counter = Create(
            "COUNTER",
            "Counter sales",
            "Takes orders and sells over the counter. Can issue documents, not undo them.",
            [
                Permissions.Catalog.Read,
                Permissions.Inventory.Read,
                Permissions.Partners.Read,
                Permissions.Pricing.Quote,
                Permissions.Pricing.Read,
                Permissions.Sales.Read,
                Permissions.Sales.Manage,
                Permissions.Sales.Confirm,
                Permissions.Sales.Dispatch,
                Permissions.Sales.Return,
                Permissions.Invoicing.Read,
                Permissions.Invoicing.Draft,
                Permissions.Invoicing.Issue,
                Permissions.Finance.Read,
                Permissions.Finance.RecordReceipt,
            ]);

        Role warehouse = Create(
            "WAREHOUSE",
            "Warehouse",
            "Moves stock, books deliveries in, counts shelves. Cannot accept the differences.",
            [
                Permissions.Catalog.Read,
                Permissions.Inventory.Read,
                Permissions.Inventory.Move,
                Permissions.Inventory.Count,
                Permissions.Inventory.Transfer,
                Permissions.Purchasing.Read,
                Permissions.Purchasing.Receive,
                Permissions.Sales.Read,
                Permissions.Sales.Dispatch,
            ]);

        Role warehouseManager = Create(
            "WAREHOUSE_MGR",
            "Warehouse manager",
            "The warehouse, plus the authority to accept a stock difference and correct a balance.",
            [
                Permissions.Catalog.Read,
                Permissions.Inventory.Read,
                Permissions.Inventory.Move,
                Permissions.Inventory.Adjust,
                Permissions.Inventory.Count,
                Permissions.Inventory.PostCount,
                Permissions.Inventory.Transfer,
                Permissions.Inventory.Configure,
                Permissions.Purchasing.Read,
                Permissions.Purchasing.Receive,
                Permissions.Sales.Read,
                Permissions.Sales.Dispatch,
            ]);

        Role buyer = Create(
            "BUYER",
            "Buyer",
            "Decides what to order and commits the company to paying for it.",
            [
                Permissions.Catalog.Read,
                Permissions.Catalog.Manage,
                Permissions.Inventory.Read,
                Permissions.Inventory.Configure,
                Permissions.Partners.Read,
                Permissions.Purchasing.Read,
                Permissions.Purchasing.Manage,
                Permissions.Purchasing.Submit,
            ]);

        Role accounts = Create(
            "ACCOUNTS",
            "Accounts",
            "The money: what is owed, what has arrived, terms, credit, and the SAF-T file.",
            [
                Permissions.Partners.Read,
                Permissions.Partners.ManageCredit,
                Permissions.Pricing.Quote,
                Permissions.Pricing.Read,
                Permissions.Sales.Read,
                Permissions.Invoicing.Read,
                Permissions.Invoicing.Draft,
                Permissions.Invoicing.Issue,
                Permissions.Invoicing.Void,
                Permissions.Invoicing.ManageSeries,
                Permissions.Invoicing.ExportSaft,
                Permissions.Finance.Read,
                Permissions.Finance.RecordReceipt,
            ]);

        _context.Roles.AddRange(administrator, counter, warehouse, warehouseManager, buyer, accounts);
        await _context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return administrator;
    }

    private static Role Create(
        string code,
        string name,
        string description,
        IReadOnlyCollection<string> permissions,
        bool isSystem = false)
    {
        Role role = Role.Create(code, name, description, isSystem).Value;
        role.SetPermissions(permissions);

        return role;
    }
}
