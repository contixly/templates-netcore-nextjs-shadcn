using Template.Domain.ApiKeys;

namespace Template.Api.Authentication;

internal static class ApiKeyAuthenticationDefaults
{
    internal const string SchemeName = "Template.ApiKey";
    internal const string ConsumerSelectorSchemeName =
        "Template.Consumer.Selector";
    internal const string HeaderName = "x-api-key";
    internal const string UserConfigId = "user-keys";
    internal const string OrganizationConfigId = "org-keys";

    internal static string ConfigId(ApiKeyOwnerKind ownerKind) => ownerKind switch
    {
        ApiKeyOwnerKind.User => UserConfigId,
        ApiKeyOwnerKind.Organization => OrganizationConfigId,
        _ => throw new ArgumentOutOfRangeException(nameof(ownerKind))
    };
}

internal static class ApiKeyClaimTypes
{
    internal const string Id = "urn:template:claim:api-key:id";
    internal const string Start = "urn:template:claim:api-key:start";
    internal const string OwnerKind = "urn:template:claim:api-key:owner-kind";
    internal const string UserId = "urn:template:claim:api-key:user-id";
    internal const string OrganizationId =
        "urn:template:claim:api-key:organization-id";
    internal const string Scope = "urn:template:claim:api-key:scope";
}
