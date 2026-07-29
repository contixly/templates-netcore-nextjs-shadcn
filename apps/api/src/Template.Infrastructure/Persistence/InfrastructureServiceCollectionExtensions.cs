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
using Template.Infrastructure.Accounts;
using Template.Infrastructure.Authentication;
using Template.Infrastructure.Identity;
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
        services.AddDbContext<AuthDbContext>(options =>
        {
            var connectionString = configuration.GetConnectionString("Postgres");
            if (string.IsNullOrWhiteSpace(connectionString))
            {
                throw new InvalidOperationException(
                    "ConnectionStrings:Postgres is required when authentication persistence is used.");
            }

            AuthDbContext.Configure(options, connectionString);
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
            .PersistKeysToDbContext<AuthDbContext>();
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
            .AddEntityFrameworkStores<AuthDbContext>()
            .AddSignInManager();

        services.AddScoped<ILocalIdentityGateway, IdentityGateway>();
        services.AddHttpContextAccessor();
        services.AddScoped<IBrowserSessionGateway, BrowserSessionGateway>();
        services.AddSingleton<PostgresTicketStore>();
        services.AddScoped<IAuthenticationUnitOfWork, EfAuthenticationUnitOfWork>();
        services.AddScoped<IExternalAccountStore, EfExternalAccountStore>();
        services.AddScoped<IAccountStore, EfAccountStore>();
        services.AddScoped<IAccountSessionStore, EfAccountSessionStore>();
        services.AddSingleton<
            ILocalAutomationCredentialGenerator,
            CryptographicLocalAutomationCredentialGenerator>();
        return services;
    }
}
