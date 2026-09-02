using AutoPartsErp.Persistence.Outbox;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace AutoPartsErp.Persistence;

/// <summary>Registers the persistence plumbing every module shares.</summary>
public static class PersistenceServiceCollectionExtensions
{
    /// <summary>
    /// Registers what <see cref="ModuleDbContext"/> needs, and binds the outbox settings.
    /// Call once, from the host, alongside <c>AddErpCore</c>.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configuration">Application configuration, read for <c>Erp:Outbox</c>.</param>
    public static IServiceCollection AddErpPersistence(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        services.TryAddScoped<ModuleDbContextDependencies>();

        // Validated at startup rather than trusted, because every one of these settings has a
        // value that breaks the sweep silently rather than loudly: a batch size of zero spins
        // the loop hot against the database, a max-attempts of zero makes every message
        // undeliverable the moment it is written, and a negative delay throws out of the
        // background service and takes the host down with it. Better to refuse to start.
        services.AddOptions<OutboxOptions>()
            .Bind(configuration.GetSection(OutboxOptions.SectionName))
            .Validate(
                options => options.BatchSize > 0,
                $"{OutboxOptions.SectionName}:BatchSize must be greater than zero.")
            .Validate(
                options => options.MaxAttempts > 0,
                $"{OutboxOptions.SectionName}:MaxAttempts must be greater than zero.")
            .Validate(
                options => options.PollInterval > TimeSpan.Zero,
                $"{OutboxOptions.SectionName}:PollInterval must be greater than zero.")
            .Validate(
                options => options.StartupDelay >= TimeSpan.Zero,
                $"{OutboxOptions.SectionName}:StartupDelay cannot be negative.")
            .Validate(
                options => options.MaxBackoff > TimeSpan.Zero,
                $"{OutboxOptions.SectionName}:MaxBackoff must be greater than zero.")
            .ValidateOnStart();

        return services;
    }

    /// <summary>
    /// Starts an outbox sweep for one module.
    /// <para>
    /// One per module rather than one for the whole application, because each module's outbox is
    /// a table in its own schema — which is what let the row be written in the same transaction
    /// as the change it describes.
    /// </para>
    /// </summary>
    /// <typeparam name="TContext">The module's database context.</typeparam>
    /// <param name="services">The service collection.</param>
    public static IServiceCollection AddModuleOutbox<TContext>(this IServiceCollection services)
        where TContext : ModuleDbContext
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddHostedService<OutboxProcessor<TContext>>();

        return services;
    }
}
