namespace Template.Domain.Accounts;

public readonly record struct ExternalProvider(string Value)
{
    public static ExternalProvider Google { get; } = new("google");
    public static ExternalProvider GitHub { get; } = new("github");
    public static ExternalProvider GitLab { get; } = new("gitlab");
    public static ExternalProvider Vk { get; } = new("vk");
    public static ExternalProvider Yandex { get; } = new("yandex");

    public static bool TryParse(string value, out ExternalProvider provider)
    {
        switch (value)
        {
            case "google":
                provider = Google;
                return true;
            case "github":
                provider = GitHub;
                return true;
            case "gitlab":
                provider = GitLab;
                return true;
            case "vk":
                provider = Vk;
                return true;
            case "yandex":
                provider = Yandex;
                return true;
            default:
                provider = default;
                return false;
        }
    }
}
