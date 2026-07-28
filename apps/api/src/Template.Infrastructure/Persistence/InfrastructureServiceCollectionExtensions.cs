using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Template.Application.Accounts.Ports;
using Template.Application.Authentication.Ports;
using Template.Infrastructure.Accounts;
using Template.Infrastructure.Authentication;
using Template.Infrastructure.Identity;

namespace Template.Infrastructure.Persistence;

public static class InfrastructureServiceCollectionExtensions
{
    public static IServiceCollection AddAuthInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration)
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
