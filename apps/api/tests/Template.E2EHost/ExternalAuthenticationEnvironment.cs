namespace Template.E2EHost;

internal static class ExternalAuthenticationEnvironment
{
    internal static void CopyConfiguredValues(
        IDictionary<string, string?> target,
        Func<string, string?> readConfiguredValue)
    {
        const string section = "ExternalAuthentication";
        const string publicOrigin = $"{section}__PublicOrigin";
        var providerNames = new[]
        {
            "Google", "GitHub", "GitLab", "Vk", "Yandex"
        };

        foreach (var name in target.Keys
                     .Where(name => name.StartsWith(
                         $"{section}__",
                         StringComparison.OrdinalIgnoreCase))
                     .ToArray())
        {
            target.Remove(name);
        }

        CopyConfiguredValue(publicOrigin);
        foreach (var provider in providerNames)
        {
            var prefix = $"{section}__Providers__{provider}";
            var clientIdName = $"{prefix}__ClientId";
            var clientSecretName = $"{prefix}__ClientSecret";
            var clientId = ReadConfiguredValue(clientIdName);
            if (provider == "Vk")
            {
                if (clientId is not null)
                {
                    target[clientIdName] = clientId;
                }

                continue;
            }

            var clientSecret = ReadConfiguredValue(clientSecretName);
            if (clientId is not null && clientSecret is not null)
            {
                target[clientIdName] = clientId;
                target[clientSecretName] = clientSecret;
            }
        }

        void CopyConfiguredValue(string name)
        {
            var value = ReadConfiguredValue(name);
            if (value is not null)
            {
                target[name] = value;
            }
        }

        string? ReadConfiguredValue(string name)
        {
            var value = readConfiguredValue(name);
            return !string.IsNullOrWhiteSpace(value)
                && string.Equals(value, value.Trim(), StringComparison.Ordinal)
                    ? value
                    : null;
        }
    }
}
