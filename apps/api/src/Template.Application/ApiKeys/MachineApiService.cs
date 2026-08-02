using Template.Domain.ApiKeys;

namespace Template.Application.ApiKeys;

public enum MachineApiResource { Me, Organizations, Members, Teams, TeamMembers }

public sealed class MachineApiService
{
    public static IReadOnlyList<string> RequiredScopes(MachineApiResource resource) => resource switch
    {
        MachineApiResource.Me => [ApiKeyScopes.BasicRead],
        MachineApiResource.Organizations => [ApiKeyScopes.OrganizationRead],
        MachineApiResource.Members => [ApiKeyScopes.OrganizationRead, ApiKeyScopes.MemberRead],
        MachineApiResource.Teams => [ApiKeyScopes.OrganizationRead, ApiKeyScopes.TeamRead],
        MachineApiResource.TeamMembers => [ApiKeyScopes.OrganizationRead, ApiKeyScopes.TeamRead, ApiKeyScopes.TeamMemberRead],
        _ => throw new ArgumentOutOfRangeException(nameof(resource))
    };
}
