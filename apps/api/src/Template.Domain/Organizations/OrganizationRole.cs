namespace Template.Domain.Organizations;

public readonly record struct OrganizationRole
{
    public static OrganizationRole Member { get; } = new("member");
    public static OrganizationRole Admin { get; } = new("admin");
    public static OrganizationRole Owner { get; } = new("owner");

    public string Value { get; }

    private OrganizationRole(string value) => Value = value;

    public static bool TryParse(string? value, out OrganizationRole role)
    {
        role = value switch
        {
            "member" => Member,
            "admin" => Admin,
            "owner" => Owner,
            _ => default
        };
        return value is "member" or "admin" or "owner";
    }

    public override string ToString() => Value;
}
