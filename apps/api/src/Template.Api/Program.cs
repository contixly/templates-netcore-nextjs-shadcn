using Template.Application.Authentication;
using Template.Api.Authentication;
using Template.Api.Endpoints;
using Template.Api.Errors;
using Template.Api.Observability;
using Template.Api.OpenApi;
using Template.Infrastructure.Health;
using Template.Infrastructure.Persistence;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddValidation();
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddAuthInfrastructure(builder.Configuration);
builder.Services.AddScoped<LocalAutomationAuthService>();
builder.Services.AddScoped<BrowserAuthenticationService>();
builder.Services
    .AddHealthChecks()
    .AddCheck<AuthDatabaseHealthCheck>(
        "postgres-auth-schema",
        tags: ["ready"],
        timeout: TimeSpan.FromSeconds(2));
builder.Services.AddApiAuthentication();
builder.Services.AddApiErrorHandling();
builder.Services.AddApiOpenApi();
builder.Services.AddEndpointModules();

var app = builder.Build();

app.UseWhen(
    context => context.Request.Path.StartsWithSegments("/api"),
    api =>
    {
        api.UseMiddleware<CorrelationIdMiddleware>();
        api.UseExceptionHandler();
        api.UseStatusCodePages();
        api.UseMiddleware<RequestLoggingMiddleware>();
    });

app.UseAuthentication();
app.UseAuthorization();
app.MapEndpointModules();

if (app.Environment.IsDevelopment() || app.Environment.IsEnvironment("Test"))
{
    app.MapOpenApi("/api/openapi/{documentName}.json").AllowAnonymous();
}

app.Run();

public partial class Program;
