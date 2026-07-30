using Template.Domain.Organizations;

namespace Template.Application.Tests.Organizations;

public sealed class OrganizationDomainTests
{
    [Theory]
    [InlineData("Acme Team", "acme-team")]
    [InlineData("E2E-Slug", "e2e-slug")]
    [InlineData("ЖЮ", "workspace")]
    public void Generated_slug_base_is_canonical(string name, string expected) =>
        Assert.Equal(expected, OrganizationSlug.GenerateBase(name));

    [Fact]
    public void Generated_slug_base_is_limited_to_48_characters_independently_of_name_length()
    {
        var slug = OrganizationSlug.GenerateBase($"prefix {new string('a', 100)}");

        Assert.Equal(48, slug.Length);
        Assert.Equal(new string('a', 48), OrganizationSlug.GenerateBase(new string('a', 100)));
    }

    [Fact]
    public void Generated_slug_base_does_not_end_with_a_separator_at_its_length_limit()
    {
        var slug = OrganizationSlug.GenerateBase($"{new string('a', 47)} b");

        Assert.False(slug.EndsWith("-", StringComparison.Ordinal));
        Assert.True(slug.Length <= 48);
    }

    [Theory]
    [InlineData(" Acme-Team ", "acme-team")]
    [InlineData("e2e-slug", "e2e-slug")]
    public void Slug_try_create_normalizes_canonical_values(string value, string expected)
    {
        Assert.True(OrganizationSlug.TryCreate(value, out var slug));
        Assert.Equal(expected, slug.Value);
        Assert.Equal(expected, slug.ToString());
    }

    [Theory]
    [InlineData("")]
    [InlineData("acme team")]
    [InlineData("-acme")]
    [InlineData("acme-")]
    [InlineData("acme--team")]
    [InlineData("жю")]
    public void Slug_try_create_rejects_noncanonical_shapes(string value) =>
        Assert.False(OrganizationSlug.TryCreate(value, out _));

    [Fact]
    public void Organization_identifiers_round_trip_as_UUIDs()
    {
        var organizationId = OrganizationId.New();
        var memberId = OrganizationMemberId.New();

        Assert.Equal(7, organizationId.Value.Version);
        Assert.Equal(7, memberId.Value.Version);
        Assert.True(OrganizationId.TryParse(organizationId.ToString(), out var parsedOrganizationId));
        Assert.True(OrganizationMemberId.TryParse(memberId.ToString(), out var parsedMemberId));
        Assert.Equal(organizationId, parsedOrganizationId);
        Assert.Equal(memberId, parsedMemberId);
    }

    [Theory]
    [InlineData("member")]
    [InlineData("admin")]
    [InlineData("owner")]
    public void Organization_roles_are_closed_and_canonical(string value)
    {
        Assert.True(OrganizationRole.TryParse(value, out var role));
        Assert.Equal(value, role.Value);
        Assert.Equal(value, role.ToString());
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("Admin")]
    [InlineData("manager")]
    public void Unknown_organization_roles_are_rejected(string? value) =>
        Assert.False(OrganizationRole.TryParse(value, out _));

    [Fact]
    public void Member_cannot_assign_any_role()
    {
        Assert.False(OrganizationPermissionPolicy.CanAssign(OrganizationRole.Member, OrganizationRole.Member));
        Assert.False(OrganizationPermissionPolicy.CanAssign(OrganizationRole.Member, OrganizationRole.Admin));
        Assert.False(OrganizationPermissionPolicy.CanAssign(OrganizationRole.Member, OrganizationRole.Owner));
    }

    [Fact]
    public void Owner_can_assign_every_closed_role()
    {
        Assert.True(OrganizationPermissionPolicy.CanAssign(OrganizationRole.Owner, OrganizationRole.Member));
        Assert.True(OrganizationPermissionPolicy.CanAssign(OrganizationRole.Owner, OrganizationRole.Admin));
        Assert.True(OrganizationPermissionPolicy.CanAssign(OrganizationRole.Owner, OrganizationRole.Owner));
    }

    [Fact]
    public void Admin_cannot_assign_owner_or_mutate_an_owner()
    {
        Assert.False(OrganizationPermissionPolicy.CanAssign(
            OrganizationRole.Admin, OrganizationRole.Owner));
        Assert.False(OrganizationPermissionPolicy.CanChangeRole(
            OrganizationRole.Admin,
            actorUserId: Guid.Parse("00000000-0000-0000-0000-000000000001"),
            targetUserId: Guid.Parse("00000000-0000-0000-0000-000000000002"),
            currentTargetRole: OrganizationRole.Owner,
            requestedRole: OrganizationRole.Member,
            ownerCount: 2));
    }

    [Fact]
    public void Role_change_rejects_self_and_redundant_changes()
    {
        var userId = Guid.Parse("00000000-0000-0000-0000-000000000001");

        Assert.False(OrganizationPermissionPolicy.CanChangeRole(
            OrganizationRole.Owner,
            userId,
            userId,
            OrganizationRole.Member,
            OrganizationRole.Admin,
            ownerCount: 1));
        Assert.False(OrganizationPermissionPolicy.CanChangeRole(
            OrganizationRole.Owner,
            userId,
            Guid.Parse("00000000-0000-0000-0000-000000000002"),
            OrganizationRole.Admin,
            OrganizationRole.Admin,
            ownerCount: 1));
    }

    [Fact]
    public void Role_change_cannot_reduce_owner_count_to_zero()
    {
        Assert.False(OrganizationPermissionPolicy.CanChangeRole(
            OrganizationRole.Owner,
            Guid.Parse("00000000-0000-0000-0000-000000000001"),
            Guid.Parse("00000000-0000-0000-0000-000000000002"),
            OrganizationRole.Owner,
            OrganizationRole.Admin,
            ownerCount: 1));
    }

    [Fact]
    public void Role_capabilities_are_explicit_and_role_specific()
    {
        Assert.Equal(
            new OrganizationCapabilities(false, false, false, false),
            OrganizationPermissionPolicy.GetCapabilities(OrganizationRole.Member));
        Assert.Equal(
            new OrganizationCapabilities(true, false, true, true),
            OrganizationPermissionPolicy.GetCapabilities(OrganizationRole.Admin));
        Assert.Equal(
            new OrganizationCapabilities(true, true, true, true),
            OrganizationPermissionPolicy.GetCapabilities(OrganizationRole.Owner));
    }

    [Fact]
    public void Allowed_domains_are_exact_normalized_and_deduplicated()
    {
        var result = OrganizationEmailDomainPolicy.Normalize(
            [" Example.COM ", "@example.com", "admin.example.com"]);

        Assert.Equal(["example.com", "admin.example.com"], result.Domains);
        Assert.Empty(result.InvalidValues);
        Assert.False(OrganizationEmailDomainPolicy.IsAllowed(
            "person@sub.example.com", result.Domains));
    }

    [Fact]
    public void Empty_domain_list_disables_restrictions()
    {
        var eligibility = OrganizationEmailDomainPolicy.Evaluate("not an email", []);

        Assert.True(eligibility.IsAllowed);
        Assert.Null(eligibility.EmailDomain);
    }

    [Theory]
    [InlineData("first@second@example.com")]
    [InlineData("person name@example.com")]
    public void Malformed_email_does_not_produce_an_allowed_domain(string email)
    {
        var eligibility = OrganizationEmailDomainPolicy.Evaluate(email, ["example.com"]);

        Assert.False(eligibility.IsAllowed);
        Assert.Null(eligibility.EmailDomain);
        Assert.False(OrganizationEmailDomainPolicy.IsAllowed(email, ["example.com"]));
    }

    [Fact]
    public void Invalid_email_has_no_email_domain()
    {
        var eligibility = OrganizationEmailDomainPolicy.Evaluate("person@bad domain.test", ["example.com"]);

        Assert.False(eligibility.IsAllowed);
        Assert.Null(eligibility.EmailDomain);
    }
}
