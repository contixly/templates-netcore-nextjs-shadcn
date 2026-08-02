namespace Template.Api.Endpoints;

internal sealed record EndpointRouteContext(
    IEndpointRouteBuilder Root,
    RouteGroupBuilder VersionedApi,
    RouteGroupBuilder VersionedMachineApi);
