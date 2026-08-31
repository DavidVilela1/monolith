using AutoPartsErp.Modules.Partners.Domain;
using AutoPartsErp.Modules.Partners.Domain.Partners;
using AutoPartsErp.SharedKernel.Messaging;
using AutoPartsErp.SharedKernel.Results;
using AutoPartsErp.SharedKernel.ValueObjects;

namespace AutoPartsErp.Modules.Partners.Application.Commands;

/// <summary>Registers a partner. Roles are granted separately.</summary>
/// <param name="Code">Short code, uppercased automatically.</param>
/// <param name="LegalName">The registered company name.</param>
/// <param name="TaxCountryCode">ISO two-letter country code for the tax number.</param>
/// <param name="TaxNumber">Their tax number.</param>
/// <param name="TradingName">Optional trading name.</param>
public sealed record CreatePartnerCommand(
    string Code,
    string LegalName,
    string TaxCountryCode,
    string TaxNumber,
    string? TradingName = null) : ICommand<Guid>;

/// <summary>Checks the shape of a <see cref="CreatePartnerCommand"/>.</summary>
public sealed class CreatePartnerCommandValidator : IValidator<CreatePartnerCommand>
{
    /// <inheritdoc />
    public ValueTask<IReadOnlyList<ValidationFailure>> ValidateAsync(
        CreatePartnerCommand instance,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(instance);

        var failures = new List<ValidationFailure>();

        if (string.IsNullOrWhiteSpace(instance.Code))
        {
            failures.Add(new ValidationFailure(nameof(instance.Code), "required", "A partner code is required."));
        }

        if (string.IsNullOrWhiteSpace(instance.LegalName))
        {
            failures.Add(new ValidationFailure(nameof(instance.LegalName), "required", "A legal name is required."));
        }

        if (string.IsNullOrWhiteSpace(instance.TaxNumber))
        {
            failures.Add(new ValidationFailure(nameof(instance.TaxNumber), "required", "A tax number is required."));
        }

        if (string.IsNullOrWhiteSpace(instance.TaxCountryCode) || instance.TaxCountryCode.Trim().Length != 2)
        {
            failures.Add(new ValidationFailure(
                nameof(instance.TaxCountryCode), "invalid", "A country code must be two letters."));
        }

        return ValueTask.FromResult<IReadOnlyList<ValidationFailure>>(failures);
    }
}

/// <summary>Creates the partner.</summary>
public sealed class CreatePartnerCommandHandler : ICommandHandler<CreatePartnerCommand, Guid>
{
    private readonly IPartnerRepository _partners;
    private readonly IPartnersUnitOfWork _unitOfWork;

    /// <summary>Initializes the handler.</summary>
    public CreatePartnerCommandHandler(IPartnerRepository partners, IPartnersUnitOfWork unitOfWork)
    {
        _partners = partners;
        _unitOfWork = unitOfWork;
    }

    /// <inheritdoc />
    public async Task<Result<Guid>> HandleAsync(
        CreatePartnerCommand request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        Result<TaxNumber> taxNumber = TaxNumber.Create(request.TaxCountryCode, request.TaxNumber);
        if (taxNumber.IsFailure)
        {
            return Result.Failure<Guid>(taxNumber.Error);
        }

        string code = request.Code?.Trim().ToUpperInvariant() ?? string.Empty;

        if (await _partners.CodeExistsAsync(code, null, cancellationToken).ConfigureAwait(false))
        {
            return Result.Failure<Guid>(PartnerErrors.Partner.CodeAlreadyExists(code));
        }

        // Catching the same company entered twice is worth a round trip. The duplicate that
        // slips through becomes two credit conversations and balances nobody can net.
        bool taxTaken = await _partners
            .TaxNumberExistsAsync(
                taxNumber.Value.CountryCode, taxNumber.Value.Value, null, cancellationToken)
            .ConfigureAwait(false);

        if (taxTaken)
        {
            return Result.Failure<Guid>(
                PartnerErrors.Partner.TaxNumberAlreadyExists(taxNumber.Value.Formatted));
        }

        Result<Partner> partner = Partner.Create(
            request.Code, request.LegalName, taxNumber.Value, request.TradingName);

        if (partner.IsFailure)
        {
            return Result.Failure<Guid>(partner.Error);
        }

        _partners.Add(partner.Value);
        await _unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return partner.Value.Id.Value;
    }
}

/// <summary>Records an address against a partner.</summary>
/// <param name="PartnerId">The partner.</param>
/// <param name="Kind">Billing, Delivery or Registered.</param>
/// <param name="Line1">Street and number.</param>
/// <param name="Postcode">Postcode.</param>
/// <param name="City">City or town.</param>
/// <param name="CountryCode">ISO two-letter country code.</param>
/// <param name="Line2">Optional second line.</param>
/// <param name="Notes">Optional delivery notes.</param>
public sealed record AddPartnerAddressCommand(
    Guid PartnerId,
    string Kind,
    string Line1,
    string Postcode,
    string City,
    string CountryCode,
    string? Line2 = null,
    string? Notes = null) : ICommand;

/// <summary>Adds the address.</summary>
public sealed class AddPartnerAddressCommandHandler : ICommandHandler<AddPartnerAddressCommand>
{
    private readonly IPartnerRepository _partners;
    private readonly IPartnersUnitOfWork _unitOfWork;

    /// <summary>Initializes the handler.</summary>
    public AddPartnerAddressCommandHandler(IPartnerRepository partners, IPartnersUnitOfWork unitOfWork)
    {
        _partners = partners;
        _unitOfWork = unitOfWork;
    }

    /// <inheritdoc />
    public async Task<Result> HandleAsync(
        AddPartnerAddressCommand request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        Partner? partner = await _partners
            .GetByIdAsync(new PartnerId(request.PartnerId), cancellationToken)
            .ConfigureAwait(false);

        if (partner is null)
        {
            return PartnerErrors.Partner.NotFound(request.PartnerId.ToString());
        }

        if (!Enum.TryParse(request.Kind, ignoreCase: true, out AddressKind kind))
        {
            return PartnerErrors.Address.KindRequired;
        }

        Result<Address> address = Address.Create(
            kind, request.Line1, request.Postcode, request.City,
            request.CountryCode, request.Line2, request.Notes);

        if (address.IsFailure)
        {
            return Result.FromError(address.Error);
        }

        Result added = partner.AddAddress(address.Value);
        if (added.IsFailure)
        {
            return added;
        }

        await _unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return Result.Success();
    }
}

/// <summary>Records a contact against a partner.</summary>
/// <param name="PartnerId">The partner.</param>
/// <param name="Name">The person's name.</param>
/// <param name="Role">What they do.</param>
/// <param name="Email">Email address.</param>
/// <param name="Phone">Phone number.</param>
/// <param name="IsPrimary">True for the person to call by default.</param>
public sealed record AddPartnerContactCommand(
    Guid PartnerId,
    string Name,
    string? Role = null,
    string? Email = null,
    string? Phone = null,
    bool IsPrimary = false) : ICommand;

/// <summary>Adds the contact.</summary>
public sealed class AddPartnerContactCommandHandler : ICommandHandler<AddPartnerContactCommand>
{
    private readonly IPartnerRepository _partners;
    private readonly IPartnersUnitOfWork _unitOfWork;

    /// <summary>Initializes the handler.</summary>
    public AddPartnerContactCommandHandler(IPartnerRepository partners, IPartnersUnitOfWork unitOfWork)
    {
        _partners = partners;
        _unitOfWork = unitOfWork;
    }

    /// <inheritdoc />
    public async Task<Result> HandleAsync(
        AddPartnerContactCommand request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        Partner? partner = await _partners
            .GetByIdAsync(new PartnerId(request.PartnerId), cancellationToken)
            .ConfigureAwait(false);

        if (partner is null)
        {
            return PartnerErrors.Partner.NotFound(request.PartnerId.ToString());
        }

        Result<ContactDetail> contact = ContactDetail.Create(
            request.Name, request.Role, request.Email, request.Phone, request.IsPrimary);

        if (contact.IsFailure)
        {
            return Result.FromError(contact.Error);
        }

        Result added = partner.AddContact(contact.Value);
        if (added.IsFailure)
        {
            return added;
        }

        await _unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return Result.Success();
    }
}

/// <summary>Starts selling to a partner on the given terms.</summary>
/// <param name="PartnerId">The partner.</param>
/// <param name="CreditLimit">How much they may owe at once. Zero means cash only.</param>
/// <param name="CurrencyCode">Currency of the limit.</param>
/// <param name="PaymentDueInDays">Days to pay. Zero means on delivery.</param>
/// <param name="PaymentMethod">Cash, Card, BankTransfer, DirectDebit or Cheque.</param>
/// <param name="EndOfMonth">True to count payment days from the end of the invoice month.</param>
/// <param name="PriceListCode">Which price list applies.</param>
public sealed record GrantCustomerRoleCommand(
    Guid PartnerId,
    decimal CreditLimit,
    string CurrencyCode,
    int PaymentDueInDays,
    string PaymentMethod,
    bool EndOfMonth = false,
    string? PriceListCode = null) : ICommand;

/// <summary>Checks the shape of a <see cref="GrantCustomerRoleCommand"/>.</summary>
public sealed class GrantCustomerRoleCommandValidator : IValidator<GrantCustomerRoleCommand>
{
    /// <inheritdoc />
    public ValueTask<IReadOnlyList<ValidationFailure>> ValidateAsync(
        GrantCustomerRoleCommand instance,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(instance);

        var failures = new List<ValidationFailure>();

        if (instance.CreditLimit < 0m)
        {
            failures.Add(new ValidationFailure(
                nameof(instance.CreditLimit), "negative", "A credit limit cannot be negative."));
        }

        if (!Currency.TryFromCode(instance.CurrencyCode, out _))
        {
            failures.Add(new ValidationFailure(
                nameof(instance.CurrencyCode), "unknown_currency",
                $"'{instance.CurrencyCode}' is not a supported currency."));
        }

        if (!Enum.TryParse(instance.PaymentMethod, ignoreCase: true, out PaymentMethod method) ||
            method == Domain.Partners.PaymentMethod.Unknown)
        {
            failures.Add(new ValidationFailure(
                nameof(instance.PaymentMethod), "unknown",
                "Payment method must be one of: Cash, Card, BankTransfer, DirectDebit, Cheque."));
        }

        return ValueTask.FromResult<IReadOnlyList<ValidationFailure>>(failures);
    }
}

/// <summary>Grants the customer role.</summary>
public sealed class GrantCustomerRoleCommandHandler : ICommandHandler<GrantCustomerRoleCommand>
{
    private readonly IPartnerRepository _partners;
    private readonly IPartnersUnitOfWork _unitOfWork;

    /// <summary>Initializes the handler.</summary>
    public GrantCustomerRoleCommandHandler(IPartnerRepository partners, IPartnersUnitOfWork unitOfWork)
    {
        _partners = partners;
        _unitOfWork = unitOfWork;
    }

    /// <inheritdoc />
    public async Task<Result> HandleAsync(
        GrantCustomerRoleCommand request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        Partner? partner = await _partners
            .GetByIdAsync(new PartnerId(request.PartnerId), cancellationToken)
            .ConfigureAwait(false);

        if (partner is null)
        {
            return PartnerErrors.Partner.NotFound(request.PartnerId.ToString());
        }

        var method = Enum.Parse<PaymentMethod>(request.PaymentMethod, ignoreCase: true);

        Result<PaymentTerms> paymentTerms = PaymentTerms.Create(
            request.PaymentDueInDays, method, request.EndOfMonth);

        if (paymentTerms.IsFailure)
        {
            return Result.FromError(paymentTerms.Error);
        }

        Result<CustomerTerms> terms = CustomerTerms.Create(
            Money.Of(request.CreditLimit, Currency.FromCode(request.CurrencyCode)),
            paymentTerms.Value,
            request.PriceListCode);

        if (terms.IsFailure)
        {
            return Result.FromError(terms.Error);
        }

        Result granted = partner.GrantCustomerRole(terms.Value);
        if (granted.IsFailure)
        {
            return granted;
        }

        await _unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return Result.Success();
    }
}

/// <summary>Starts buying from a partner on the given terms.</summary>
/// <param name="PartnerId">The partner.</param>
/// <param name="PaymentDueInDays">Days we have to pay them.</param>
/// <param name="PaymentMethod">How we pay them.</param>
/// <param name="LeadTimeDays">Typical days from order to delivery.</param>
/// <param name="EndOfMonth">True to count payment days from month end.</param>
/// <param name="MinimumOrderValue">The value below which they will not ship.</param>
/// <param name="CurrencyCode">Currency of the minimum order value.</param>
/// <param name="OurAccountNumber">Our account number with them.</param>
public sealed record GrantSupplierRoleCommand(
    Guid PartnerId,
    int PaymentDueInDays,
    string PaymentMethod,
    int LeadTimeDays,
    bool EndOfMonth = false,
    decimal? MinimumOrderValue = null,
    string? CurrencyCode = null,
    string? OurAccountNumber = null) : ICommand;

/// <summary>Grants the supplier role.</summary>
public sealed class GrantSupplierRoleCommandHandler : ICommandHandler<GrantSupplierRoleCommand>
{
    private readonly IPartnerRepository _partners;
    private readonly IPartnersUnitOfWork _unitOfWork;

    /// <summary>Initializes the handler.</summary>
    public GrantSupplierRoleCommandHandler(IPartnerRepository partners, IPartnersUnitOfWork unitOfWork)
    {
        _partners = partners;
        _unitOfWork = unitOfWork;
    }

    /// <inheritdoc />
    public async Task<Result> HandleAsync(
        GrantSupplierRoleCommand request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        Partner? partner = await _partners
            .GetByIdAsync(new PartnerId(request.PartnerId), cancellationToken)
            .ConfigureAwait(false);

        if (partner is null)
        {
            return PartnerErrors.Partner.NotFound(request.PartnerId.ToString());
        }

        if (!Enum.TryParse(request.PaymentMethod, ignoreCase: true, out PaymentMethod method) ||
            method == Domain.Partners.PaymentMethod.Unknown)
        {
            return PartnerErrors.Terms.PaymentMethodRequired;
        }

        Result<PaymentTerms> paymentTerms = PaymentTerms.Create(
            request.PaymentDueInDays, method, request.EndOfMonth);

        if (paymentTerms.IsFailure)
        {
            return Result.FromError(paymentTerms.Error);
        }

        Money? minimum = null;

        if (request.MinimumOrderValue is { } value)
        {
            if (!Currency.TryFromCode(request.CurrencyCode, out Currency currency))
            {
                return PartnerErrors.Partner.CountryCodeInvalid;
            }

            minimum = Money.Of(value, currency);
        }

        Result<SupplierTerms> terms = SupplierTerms.Create(
            paymentTerms.Value, request.LeadTimeDays, minimum, request.OurAccountNumber);

        if (terms.IsFailure)
        {
            return Result.FromError(terms.Error);
        }

        Result granted = partner.GrantSupplierRole(terms.Value);
        if (granted.IsFailure)
        {
            return granted;
        }

        await _unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return Result.Success();
    }
}

/// <summary>Stops a partner placing new orders.</summary>
/// <param name="PartnerId">The partner.</param>
/// <param name="Reason">Why. Required.</param>
public sealed record PlacePartnerOnHoldCommand(Guid PartnerId, string Reason) : ICommand;

/// <summary>Places the partner on hold.</summary>
public sealed class PlacePartnerOnHoldCommandHandler : ICommandHandler<PlacePartnerOnHoldCommand>
{
    private readonly IPartnerRepository _partners;
    private readonly IPartnersUnitOfWork _unitOfWork;

    /// <summary>Initializes the handler.</summary>
    public PlacePartnerOnHoldCommandHandler(IPartnerRepository partners, IPartnersUnitOfWork unitOfWork)
    {
        _partners = partners;
        _unitOfWork = unitOfWork;
    }

    /// <inheritdoc />
    public async Task<Result> HandleAsync(
        PlacePartnerOnHoldCommand request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        Partner? partner = await _partners
            .GetByIdAsync(new PartnerId(request.PartnerId), cancellationToken)
            .ConfigureAwait(false);

        if (partner is null)
        {
            return PartnerErrors.Partner.NotFound(request.PartnerId.ToString());
        }

        Result held = partner.PlaceOnHold(request.Reason);
        if (held.IsFailure)
        {
            return held;
        }

        await _unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return Result.Success();
    }
}

/// <summary>Lifts a hold.</summary>
/// <param name="PartnerId">The partner.</param>
public sealed record ReleasePartnerHoldCommand(Guid PartnerId) : ICommand;

/// <summary>Releases the hold.</summary>
public sealed class ReleasePartnerHoldCommandHandler : ICommandHandler<ReleasePartnerHoldCommand>
{
    private readonly IPartnerRepository _partners;
    private readonly IPartnersUnitOfWork _unitOfWork;

    /// <summary>Initializes the handler.</summary>
    public ReleasePartnerHoldCommandHandler(IPartnerRepository partners, IPartnersUnitOfWork unitOfWork)
    {
        _partners = partners;
        _unitOfWork = unitOfWork;
    }

    /// <inheritdoc />
    public async Task<Result> HandleAsync(
        ReleasePartnerHoldCommand request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        Partner? partner = await _partners
            .GetByIdAsync(new PartnerId(request.PartnerId), cancellationToken)
            .ConfigureAwait(false);

        if (partner is null)
        {
            return PartnerErrors.Partner.NotFound(request.PartnerId.ToString());
        }

        Result released = partner.ReleaseHold();
        if (released.IsFailure)
        {
            return released;
        }

        await _unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return Result.Success();
    }
}
