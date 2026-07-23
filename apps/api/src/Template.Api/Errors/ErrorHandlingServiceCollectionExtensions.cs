namespace Template.Api.Errors;

internal static class ErrorHandlingServiceCollectionExtensions
{
    internal static IServiceCollection AddApiErrorHandling(this IServiceCollection services)
    {
        services.AddProblemDetails(options =>
            options.CustomizeProblemDetails = ApiProblemDetailsDefaults.Customize);
        services.AddExceptionHandler<ApiExceptionHandler>();
        return services;
    }
}
