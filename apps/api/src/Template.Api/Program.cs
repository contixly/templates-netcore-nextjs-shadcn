using Template.Api.Authentication;
using Template.Api.Endpoints;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddValidation();
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddHealthChecks();
builder.Services.AddApiAuthentication();
builder.Services.AddEndpointModules();

var app = builder.Build();

app.UseAuthentication();
app.UseAuthorization();
app.MapHealthChecks("/api/health").AllowAnonymous();
app.MapEndpointModules();

app.Run();

public partial class Program;
