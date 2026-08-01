namespace Template.Domain.Organizations;

public readonly record struct OrganizationMemberId(Guid Value)
{
    public static OrganizationMemberId New() => new(Guid.CreateVersion7());

    public static OrganizationMemberId Parse(string value) => new(Guid.Parse(value));

    public static bool TryParse(string? value, out OrganizationMemberId organizationMemberId)
    {
        var parsed = Guid.TryParse(value, out var id);
        organizationMemberId = new OrganizationMemberId(id);
        return parsed;
    }

    public override string ToString() => Value.ToString();
}
