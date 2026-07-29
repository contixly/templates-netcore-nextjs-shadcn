using System.Security.Cryptography.X509Certificates;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.DataProtection.EntityFrameworkCore;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
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
        var dataProtectionOptions =
            dataProtectionSection.Get<AuthenticationDataProtectionOptions>() ?? new();
        services
            .AddOptions<AuthenticationDataProtectionOptions>()
            .Bind(dataProtectionSection)
            .Validate(
                options => !string.IsNullOrWhiteSpace(options.ApplicationName),
                "DataProtection:ApplicationName is required.")
            .Validate(
                options =>
                    !environment.IsProduction() ||
                    (!string.IsNullOrWhiteSpace(options.CertificatePath) &&
                     !string.IsNullOrWhiteSpace(options.CertificatePassword)),
                "DataProtection certificate path and password are required in Production.")
            .ValidateOnStart();

        var dataProtection = services
            .AddDataProtection()
            .SetApplicationName(dataProtectionOptions.ApplicationName)
            .PersistKeysToDbContext<AuthDbContext>();
        if (environment.IsProduction())
        {
            var certificate = LoadProductionCertificate(dataProtectionOptions);
            services.AddSingleton(certificate);
            dataProtection.ProtectKeysWithCertificate(certificate);
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

    private static X509Certificate2 LoadProductionCertificate(
        AuthenticationDataProtectionOptions options)
    {
        if (string.IsNullOrWhiteSpace(options.CertificatePath) ||
            string.IsNullOrWhiteSpace(options.CertificatePassword))
        {
            throw InvalidDataProtectionOptions(
                "DataProtection production certificate path and password are required.");
        }

        try
        {
            var certificate = X509CertificateLoader.LoadPkcs12FromFile(
                options.CertificatePath,
                options.CertificatePassword);
            var now = DateTime.UtcNow;
            using var publicKey = certificate.GetRSAPublicKey();
            using var privateKey = certificate.GetRSAPrivateKey();
            if (!certificate.HasPrivateKey ||
                publicKey is null ||
                privateKey is null ||
                certificate.NotBefore.ToUniversalTime() > now ||
                certificate.NotAfter.ToUniversalTime() < now)
            {
                certificate.Dispose();
                throw InvalidDataProtectionOptions(
                    "DataProtection production certificate must be current and include its private key.");
            }

            return certificate;
        }
        catch (OptionsValidationException)
        {
            throw;
        }
        catch (Exception exception) when (
            exception is IOException or
            UnauthorizedAccessException or
            System.Security.Cryptography.CryptographicException or
            ArgumentException)
        {
            throw InvalidDataProtectionOptions(
                "DataProtection production certificate could not be loaded.");
        }
    }

    private static OptionsValidationException InvalidDataProtectionOptions(
        string failure) =>
        new(
            AuthenticationDataProtectionOptions.SectionName,
            typeof(AuthenticationDataProtectionOptions),
            [failure]);
}
