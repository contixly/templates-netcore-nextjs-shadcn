using Template.Api.Authentication;
using Template.Api.Endpoints;
using Template.Api.Errors;
using Template.Api.Observability;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddValidation();
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddHealthChecks();
builder.Services.AddApiAuthentication();
builder.Services.AddApiErrorHandling();
builder.Services.AddEndpointModules();

var app = builder.Build();

app.UseWhen(
    context => context.Request.Path.StartsWithSegments("/api"),
    api =>
    {
        api.UseMiddleware<CorrelationIdMiddleware>();
        api.UseExceptionHandler();
        api.UseStatusCodePages();
    });

app.UseAuthentication();
app.UseAuthorization();
app.MapEndpointModules();

app.Run();

public partial class Program;
