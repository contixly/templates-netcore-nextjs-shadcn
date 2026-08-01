namespace Template.Domain.Organizations;

public readonly record struct OrganizationId(Guid Value)
{
    public static OrganizationId New() => new(Guid.CreateVersion7());

    public static OrganizationId Parse(string value) => new(Guid.Parse(value));

    public static bool TryParse(string? value, out OrganizationId organizationId)
    {
        var parsed = Guid.TryParse(value, out var id);
        organizationId = new OrganizationId(id);
        return parsed;
    }

    public override string ToString() => Value.ToString();
}
