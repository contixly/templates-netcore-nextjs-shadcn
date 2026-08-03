using Microsoft.Extensions.DependencyInjection;
using Template.Application.Documents.Ports;

namespace Template.Infrastructure.Documents;

public static class DocumentSearchInfrastructureServiceCollectionExtensions
{
    public static IServiceCollection AddDocumentSearchInfrastructure(
        this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddSingleton<
            IDocumentSearchIndexProvider,
            EmbeddedDocumentSearchIndexProvider>();
        return services;
    }
}
