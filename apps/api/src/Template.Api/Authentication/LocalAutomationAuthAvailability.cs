using Microsoft.Extensions.Options;

namespace Template.Api.Authentication;

internal interface ILocalAutomationAuthAvailability
{
    bool IsEnabled { get; }
}

internal sealed class LocalAutomationAuthAvailability(
    IWebHostEnvironment environment,
    IOptions<LocalAutomationAuthOptions> options)
    : ILocalAutomationAuthAvailability
{
    public bool IsEnabled =>
        (environment.IsDevelopment() || environment.IsEnvironment("Test")) &&
        options.Value.Enabled;
}
