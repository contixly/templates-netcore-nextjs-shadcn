namespace Template.Domain.Organizations;

public sealed record OrganizationEmailDomainNormalization(
    IReadOnlyList<string> Domains,
    IReadOnlyList<string> InvalidValues);

public sealed record OrganizationEmailDomainEligibility(bool IsAllowed, string? EmailDomain)
{
    public bool Allowed => IsAllowed;
}

public static class OrganizationEmailDomainPolicy
{
    private const int MaximumDomainLength = 253;
    private const int MaximumLabelLength = 63;

    public static OrganizationEmailDomainNormalization Normalize(IEnumerable<string> values)
    {
        ArgumentNullException.ThrowIfNull(values);

        var domains = new List<string>();
        var invalidValues = new List<string>();
        var seenDomains = new HashSet<string>(StringComparer.Ordinal);

        foreach (var value in values)
        {
            if (!TryNormalize(value, out var domain))
            {
                invalidValues.Add(value);
                continue;
            }

            if (seenDomains.Add(domain))
            {
                domains.Add(domain);
            }
        }

        return new OrganizationEmailDomainNormalization(domains, invalidValues);
    }

    public static bool IsAllowed(string email, IReadOnlyCollection<string> allowedDomains) =>
        Evaluate(email, allowedDomains).IsAllowed;

    public static OrganizationEmailDomainEligibility Evaluate(
        string email,
        IReadOnlyCollection<string> allowedDomains)
    {
        ArgumentNullException.ThrowIfNull(email);
        ArgumentNullException.ThrowIfNull(allowedDomains);

        var emailDomain = ExtractEmailDomain(email);

        if (allowedDomains.Count == 0)
        {
            return new OrganizationEmailDomainEligibility(true, emailDomain);
        }

        return new OrganizationEmailDomainEligibility(
            emailDomain is not null && allowedDomains.Contains(emailDomain),
            emailDomain);
    }

    private static bool TryNormalize(string? value, out string domain)
    {
        domain = string.Empty;

        if (value is null)
        {
            return false;
        }

        var normalized = value.Trim().ToLowerInvariant();
        if (normalized.StartsWith('@'))
        {
            normalized = normalized[1..];
        }

        if (!IsValidDomain(normalized))
        {
            return false;
        }

        domain = normalized;
        return true;
    }

    private static string? ExtractEmailDomain(string email)
    {
        var normalizedEmail = email.Trim().ToLowerInvariant();
        var atIndex = normalizedEmail.LastIndexOf('@');

        if (atIndex < 1 || atIndex == normalizedEmail.Length - 1)
        {
            return null;
        }

        return TryNormalize(normalizedEmail[(atIndex + 1)..], out var domain) ? domain : null;
    }

    private static bool IsValidDomain(string domain)
    {
        if (domain.Length is 0 or > MaximumDomainLength)
        {
            return false;
        }

        var labels = domain.Split('.');
        if (labels.Length < 2)
        {
            return false;
        }

        return labels.All(IsValidLabel);
    }

    private static bool IsValidLabel(string label)
    {
        if (label.Length is 0 or > MaximumLabelLength || label[0] == '-' || label[^1] == '-')
        {
            return false;
        }

        return label.All(character =>
            character is >= 'a' and <= 'z' or >= '0' and <= '9' or '-');
    }
}
