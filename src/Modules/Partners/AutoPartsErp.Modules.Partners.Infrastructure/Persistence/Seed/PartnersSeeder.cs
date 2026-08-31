using AutoPartsErp.Modules.Partners.Domain.Partners;
using AutoPartsErp.SharedKernel.Results;
using AutoPartsErp.SharedKernel.ValueObjects;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace AutoPartsErp.Modules.Partners.Infrastructure.Persistence.Seed;

/// <summary>
/// Puts a few partners in an empty database: an account customer, a cash customer, and a
/// supplier who is also a customer — the case that justifies one aggregate with roles.
/// </summary>
public sealed class PartnersSeeder
{
    private readonly PartnersDbContext _context;
    private readonly ILogger<PartnersSeeder> _logger;

    /// <summary>Initializes the seeder.</summary>
    public PartnersSeeder(PartnersDbContext context, ILogger<PartnersSeeder> logger)
    {
        _context = context;
        _logger = logger;
    }

    /// <summary>Seeds partners if, and only if, none exist.</summary>
    public async Task SeedAsync(CancellationToken cancellationToken = default)
    {
        if (await _context.Partners.AnyAsync(cancellationToken).ConfigureAwait(false))
        {
            _logger.LogInformation("Partners already exist; skipping seed.");
            return;
        }

        _context.Partners.AddRange(
            BuildAccountCustomer(),
            BuildCashCustomer(),
            BuildSupplierWhoIsAlsoACustomer());

        await _context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        _logger.LogInformation("Seeded 3 partners.");
    }

    private static Partner BuildAccountCustomer()
    {
        Partner partner = Require(Partner.Create(
            "C0001", "Oficina Central Unipessoal Lda",
            Require(TaxNumber.Create("PT", "501442600")),
            "Oficina Central"));

        Apply(partner.AddAddress(Require(Address.Create(
            AddressKind.Billing, "Rua das Oficinas 12", "1000-100", "Lisboa", "PT"))));

        Apply(partner.AddAddress(Require(Address.Create(
            AddressKind.Delivery, "Rua das Oficinas 12", "1000-100", "Lisboa", "PT",
            notes: "Entregas pela porta lateral, 8h-17h"))));

        Apply(partner.AddContact(Require(ContactDetail.Create(
            "João Ferreira", "Chefe de oficina", "joao@oficinacentral.pt", "912345678", isPrimary: true))));

        Apply(partner.GrantCustomerRole(Require(CustomerTerms.Create(
            Money.Of(5000m, Currency.Eur),
            Require(PaymentTerms.Create(30, PaymentMethod.BankTransfer, endOfMonth: true)),
            "TRADE"))));

        return partner;
    }

    private static Partner BuildCashCustomer()
    {
        Partner partner = Require(Partner.Create(
            "C0002", "Auto Reparações do Norte Lda",
            Require(TaxNumber.Create("PT", "980405319"))));

        Apply(partner.AddAddress(Require(Address.Create(
            AddressKind.Billing, "Avenida da Boavista 455", "4100-100", "Porto", "PT"))));

        Apply(partner.GrantCustomerRole(CustomerTerms.CashOnly(Currency.Eur)));

        return partner;
    }

    private static Partner BuildSupplierWhoIsAlsoACustomer()
    {
        // The case that makes one aggregate with roles worth having: a factor we buy from,
        // who occasionally buys from us when they are short.
        Partner partner = Require(Partner.Create(
            "S0001", "Distribuidora Ibérica de Peças SA",
            Require(TaxNumber.Create("PT", "123456789")),
            "DIP"));

        Apply(partner.AddAddress(Require(Address.Create(
            AddressKind.Billing, "Zona Industrial, Lote 14", "2870-100", "Montijo", "PT"))));

        Apply(partner.AddContact(Require(ContactDetail.Create(
            "Marta Nunes", "Gestora de conta", "marta@dip.pt", isPrimary: true))));

        Apply(partner.GrantSupplierRole(Require(SupplierTerms.Create(
            Require(PaymentTerms.Create(60, PaymentMethod.BankTransfer, endOfMonth: true)),
            leadTimeDays: 2,
            Money.Of(250m, Currency.Eur),
            "PT-44821"))));

        Apply(partner.GrantCustomerRole(Require(CustomerTerms.Create(
            Money.Of(2000m, Currency.Eur),
            Require(PaymentTerms.Create(30, PaymentMethod.BankTransfer))))));

        return partner;
    }

    private static T Require<T>(Result<T> result) =>
        result.IsSuccess
            ? result.Value
            : throw new InvalidOperationException($"Seed data is invalid: {result.Error}");

    private static void Apply(Result result)
    {
        if (result.IsFailure)
        {
            throw new InvalidOperationException($"Seed data is invalid: {result.Error}");
        }
    }
}
