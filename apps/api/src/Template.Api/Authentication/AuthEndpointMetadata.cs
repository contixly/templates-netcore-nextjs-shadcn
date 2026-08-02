namespace Template.Api.Authentication;

internal sealed class AntiforgeryProtectedEndpointMetadata;
internal sealed class LocalOnlyEndpointMetadata;
internal sealed class MachineApiEndpointMetadata;
internal sealed class MixedConsumerEndpointMetadata;

internal static class AuthEndpointConventionExtensions
{
    internal static RouteHandlerBuilder RequireApiAntiforgery(
        this RouteHandlerBuilder builder) =>
        builder
            .WithMetadata(new AntiforgeryProtectedEndpointMetadata())
            .AddEndpointFilter<AntiforgeryEndpointFilter>();

    internal static RouteHandlerBuilder WithLocalOnly(
        this RouteHandlerBuilder builder) =>
        builder
            .WithMetadata(new LocalOnlyEndpointMetadata())
            .WithTags("local-only");
}
