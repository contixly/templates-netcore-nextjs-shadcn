using Microsoft.AspNetCore.Builder;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Template.Api;
using Template.Infrastructure.Persistence;
using Testcontainers.PostgreSql;

await using var postgres = new PostgreSqlBuilder("postgres:18.4")
    .WithDatabase("template_e2e")
    .WithUsername("postgres")
    .WithPassword("postgres")
    .Build();
await postgres.StartAsync();

Environment.SetEnvironmentVariable(
    "ConnectionStrings__Postgres",
    postgres.GetConnectionString());
Environment.SetEnvironmentVariable(
    "LocalAutomationAuth__Enabled",
    "true");

await using var app = ApiHost.Build(args);
await using (var scope = app.Services.CreateAsyncScope())
{
    await scope.ServiceProvider.GetRequiredService<AuthDbContext>()
        .Database.MigrateAsync();
}

// Model the production HTTPS boundary while Kestrel uses a loopback HTTP listener.
app.Use((context, next) =>
{
    context.Request.Scheme = Uri.UriSchemeHttps;
    return next(context);
});

await app.RunAsync();
