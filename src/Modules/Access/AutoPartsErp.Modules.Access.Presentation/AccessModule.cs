using AutoPartsErp.Modules.Abstractions.DependencyInjection;
using AutoPartsErp.Modules.Abstractions.Modules;
using AutoPartsErp.Modules.Access.Application;
using AutoPartsErp.Modules.Access.Domain;
using AutoPartsErp.Modules.Access.Infrastructure.Persistence;
using AutoPartsErp.Modules.Access.Infrastructure.Persistence.Repositories;
using AutoPartsErp.Modules.Access.Infrastructure.Persistence.Seed;
using AutoPartsErp.Modules.Access.Infrastructure.Security;
using AutoPartsErp.Modules.Access.Presentation.Endpoints;
using AutoPartsErp.Persistence;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace AutoPartsErp.Modules.Access.Presentation;

/// <summary>
/// The Access module's entry point.
/// <para>
/// <see cref="Order"/> is 0, before every other module. Nothing depends on this one at compile
/// time — the rest of the system asks who the user is through <c>ICurrentUser</c> in the shared
/// kernel and never learns that this module exists — but its tables have to be there and seeded
/// first, because a deployment where the schema is up and nobody can sign in is a deployment that
/// looks finished and is not usable.
/// </para>
/// </summary>
public sealed class AccessModule : IModule
{
    /// <inheritdoc />
    public string Name => "Access";

    /// <inheritdoc />
    public string SchemaName => AccessDbContext.SchemaName;

    /// <inheritdoc />
    public int Order => 0;

    /// <inheritdoc />
    public void RegisterServices(IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        string connectionString = configuration.GetConnectionString("Erp")
            ?? throw new InvalidOperationException("Connection string 'Erp' is not configured.");

        // Validated on start rather than on the first sign-in. A missing or short signing key is
        // a deployment that cannot authenticate anybody, and the moment to find that out is while
        // somebody is still watching the console.
        services.AddOptions<AccessOptions>()
            .Bind(configuration.GetSection(AccessOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        services.AddScoped<AuditingInterceptor>();

        services.AddDbContext<AccessDbContext>((provider, options) =>
        {
            options.UseNpgsql(connectionString, npgsql =>
            {
                npgsql.MigrationsHistoryTable("__migrations_history", AccessDbContext.SchemaName);
                npgsql.EnableRetryOnFailure(3);
                npgsql.UseQuerySplittingBehavior(QuerySplittingBehavior.SplitQuery);
            });

            options.UseSnakeCaseNamingConvention();
            options.AddInterceptors(provider.GetRequiredService<AuditingInterceptor>());
        });

        services.AddModuleOutbox<AccessDbContext>();

        services.AddScoped<IAccessUnitOfWork>(provider =>
            provider.GetRequiredService<AccessDbContext>());

        services.AddScoped<IUserRepository, UserRepository>();
        services.AddScoped<IRoleRepository, RoleRepository>();
        services.AddScoped<IRefreshTokenRepository, RefreshTokenRepository>();
        services.AddScoped<AccessSeeder>();

        // The two cryptographic jobs, both singletons: the password hasher builds one throwaway
        // hash at construction and the token issuer builds its signing credentials once.
        services.AddSingleton<IPasswordHasher, AspNetPasswordHasher>();
        services.AddScoped<IAccessTokenIssuer, JwtAccessTokenIssuer>();

        services.AddModuleHandlers(
            typeof(Application.Authentication.SignInCommand).Assembly);
    }

    /// <inheritdoc />
    public void MapEndpoints(IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);

        RouteGroupBuilder group = endpoints
            .MapGroup("/access")
            .WithTags("Access");

        new AuthenticationEndpoints().Map(group);
        new UserEndpoints().Map(group);
        new RoleEndpoints().Map(group);
    }
}
