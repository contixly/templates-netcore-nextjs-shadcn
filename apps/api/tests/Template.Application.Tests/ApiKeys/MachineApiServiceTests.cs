using Template.Application.ApiKeys;
using Template.Domain.ApiKeys;

namespace Template.Application.Tests.ApiKeys;

public sealed class MachineApiServiceTests
{
    [Theory]
    [InlineData(MachineApiResource.Me, new[] { "basic:read" })]
    [InlineData(MachineApiResource.Organizations, new[] { "organization:read" })]
    [InlineData(MachineApiResource.Members, new[] { "organization:read", "member:read" })]
    [InlineData(MachineApiResource.Teams, new[] { "organization:read", "team:read" })]
    [InlineData(MachineApiResource.TeamMembers, new[] { "organization:read", "team:read", "teamMember:read" })]
    public void Machine_resources_have_the_exact_required_scope_sets(MachineApiResource resource, string[] expected) =>
        Assert.Equal(expected, MachineApiService.RequiredScopes(resource));
}
