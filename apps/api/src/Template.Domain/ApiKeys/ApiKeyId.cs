namespace Template.Domain.ApiKeys;

public readonly record struct ApiKeyId(Guid Value)
{
    public static ApiKeyId New(DateTimeOffset now) => new(Guid.CreateVersion7(now));

    public static bool TryParse(string? value, out ApiKeyId apiKeyId)
    {
        var parsed = Guid.TryParse(value, out var id);
        apiKeyId = new ApiKeyId(id);
        return parsed;
    }

    public override string ToString() => Value.ToString();
}
