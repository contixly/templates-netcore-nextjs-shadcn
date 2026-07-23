using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Template.Api.Endpoints;

namespace Template.Api.Tests.Infrastructure;

public sealed class ApiWebApplicationFactory : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Test");
        builder.ConfigureLogging(logging =>
        {
            logging.SetMinimumLevel(LogLevel.Debug);
            logging.AddFilter<CapturedLogProvider>(level => level >= LogLevel.Debug);
        });
        builder.ConfigureTestServices(services =>
        {
            services
                .AddAuthentication(options =>
                {
                    options.DefaultAuthenticateScheme = TestAuthenticationHandler.SchemeName;
                    options.DefaultChallengeScheme = TestAuthenticationHandler.SchemeName;
                    options.DefaultForbidScheme = TestAuthenticationHandler.SchemeName;
                })
                .AddScheme<AuthenticationSchemeOptions, TestAuthenticationHandler>(
                    TestAuthenticationHandler.SchemeName,
                    _ => { });

            services.AddAuthorization(options =>
                options.AddPolicy(
                    TestEndpointModule.ForbiddenPolicy,
                    policy => policy.RequireClaim("test.permission", "granted")));
            services.AddSingleton<IEndpointModule, TestEndpointModule>();
            services.AddSingleton<CapturedLogProvider>();
            services.AddSingleton<ILoggerProvider>(
                provider => provider.GetRequiredService<CapturedLogProvider>());
        });
    }
}
