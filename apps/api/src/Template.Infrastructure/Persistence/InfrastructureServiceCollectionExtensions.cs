using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.DataProtection.EntityFrameworkCore;
using Microsoft.AspNetCore.DataProtection.KeyManagement;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Template.Application.Accounts.Ports;
using Template.Application.Authentication.Ports;
using Template.Application.Common.Ports;
using Template.Application.Organizations.Ports;
using Template.Infrastructure.Accounts;
using Template.Infrastructure.Authentication;
using Template.Infrastructure.Identity;
using Template.Infrastructure.Organizations;
using AuthenticationDataProtectionOptions =
    Template.Infrastructure.Authentication.DataProtectionOptions;

namespace Template.Infrastructure.Persistence;

public static class InfrastructureServiceCollectionExtensions
{
    public static IServiceCollection AddAuthInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration,
        IHostEnvironment environment)
    {
        services.AddDbContext<TemplateDbContext>(options =>
        {
            var connectionString = configuration.GetConnectionString("Postgres");
            if (string.IsNullOrWhiteSpace(connectionString))
            {
                throw new InvalidOperationException(
                    "ConnectionStrings:Postgres is required when authentication persistence is used.");
            }

            TemplateDbContext.Configure(options, connectionString);
        });

        var dataProtectionSection = configuration.GetSection(
            AuthenticationDataProtectionOptions.SectionName);
        services
            .AddOptions<AuthenticationDataProtectionOptions>()
            .Bind(dataProtectionSection)
            .Validate(
                options => string.Equals(
                    options.ApplicationName,
                    AuthenticationDataProtectionOptions.RequiredApplicationName,
                    StringComparison.Ordinal),
                "DataProtection:ApplicationName must be exactly 'Template'.")
            .Validate(
                options =>
                    !environment.IsProduction() ||
                    (!string.IsNullOrWhiteSpace(options.CertificatePath) &&
                     !string.IsNullOrWhiteSpace(options.CertificatePassword)),
                "DataProtection certificate path and password are required in Production.")
            .ValidateOnStart();

        services
            .AddDataProtection()
            .SetApplicationName(
                AuthenticationDataProtectionOptions.RequiredApplicationName)
            .PersistKeysToDbContext<TemplateDbContext>();
        if (environment.IsProduction())
        {
            services.TryAddSingleton(provider =>
                ProductionDataProtectionCertificate.Load(
                    provider
                        .GetRequiredService<
                            IOptions<AuthenticationDataProtectionOptions>>()
                        .Value));
            services.TryAddEnumerable(
                ServiceDescriptor.Singleton<
                    IConfigureOptions<KeyManagementOptions>,
                    ProductionDataProtectionKeyManagementSetup>());
            services
                .AddHostedService<DataProtectionCertificateStartupService>();
        }

        services
            .AddIdentityCore<ApplicationUser>(options =>
            {
                options.User.RequireUniqueEmail = true;
                options.Password.RequiredLength = 12;
                options.Password.RequireDigit = false;
                options.Password.RequireLowercase = false;
                options.Password.RequireUppercase = false;
                options.Password.RequireNonAlphanumeric = false;
                options.Password.RequiredUniqueChars = 1;
                options.Lockout.AllowedForNewUsers = true;
                options.Lockout.MaxFailedAccessAttempts = 5;
                options.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(5);
                options.SignIn.RequireConfirmedEmail = false;
            })
            .AddEntityFrameworkStores<TemplateDbContext>()
            .AddSignInManager();

        services.AddScoped<ILocalIdentityGateway, IdentityGateway>();
        services.AddHttpContextAccessor();
        services.AddScoped<IBrowserSessionGateway, BrowserSessionGateway>();
        services.AddSingleton<PostgresTicketStore>();
        services.AddScoped<IApplicationUnitOfWork, EfApplicationUnitOfWork>();
        services.AddScoped<IExternalAccountStore, EfExternalAccountStore>();
        services.AddScoped<IAccountStore, EfAccountStore>();
        services.AddScoped<IAccountSessionStore, EfAccountSessionStore>();
        services.AddScoped<IOrganizationStore, EfOrganizationStore>();
        services.AddSingleton<
            ILocalAutomationCredentialGenerator,
            CryptographicLocalAutomationCredentialGenerator>();
        services.AddOpenIddictExternalClient(configuration, environment);
        return services;
    }
}
