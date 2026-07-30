using Microsoft.Extensions.Hosting;

namespace Template.Infrastructure.Authentication;

internal sealed class DataProtectionCertificateStartupService(
    ProductionDataProtectionCertificate certificate)
    : IHostedService
{
    public Task StartAsync(CancellationToken cancellationToken)
    {
        _ = certificate.Certificate.Handle;
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken) =>
        Task.CompletedTask;
}
