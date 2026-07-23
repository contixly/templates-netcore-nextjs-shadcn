using Template.Api.Authentication;
using Template.Api.Endpoints;
using Template.Api.Errors;
using Template.Api.Observability;
using Template.Api.OpenApi;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddValidation();
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddHealthChecks();
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
