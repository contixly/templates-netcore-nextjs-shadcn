namespace Template.Api.Authentication;

internal static class SafeReturnUrl
{
    private static readonly Uri NormalizationOrigin =
        new("https://return.invalid/", UriKind.Absolute);

    internal static bool TryNormalize(
        string? candidate,
        string fallback,
        out string normalized)
    {
        ArgumentException.ThrowIfNullOrEmpty(fallback);

        if (candidate is null)
        {
            normalized = fallback;
            return true;
        }

        normalized = string.Empty;
        if (candidate.Length == 0
            || candidate[0] != '/'
            || candidate.Length > 4096
            || candidate.StartsWith("//", StringComparison.Ordinal)
            || candidate.Contains('\\')
            || candidate.Any(char.IsControl)
            || HasDangerousEncoding(candidate))
        {
            return false;
        }

        if (!Uri.TryCreate(
                NormalizationOrigin,
                candidate,
                out var resolved)
            || !string.Equals(
                resolved.Scheme,
                NormalizationOrigin.Scheme,
                StringComparison.Ordinal)
            || !string.Equals(
                resolved.Host,
                NormalizationOrigin.Host,
                StringComparison.Ordinal)
            || resolved.Port != NormalizationOrigin.Port)
        {
            return false;
        }

        var path = resolved.AbsolutePath;
        if (IsReservedPath(path, "/api")
            || IsReservedPath(path, "/auth"))
        {
            return false;
        }

        var canonical = resolved.GetComponents(
            UriComponents.PathAndQuery | UriComponents.Fragment,
            UriFormat.UriEscaped);
        if (!canonical.StartsWith("/", StringComparison.Ordinal))
        {
            canonical = $"/{canonical}";
        }

        if (canonical.StartsWith("//", StringComparison.Ordinal)
            || canonical.Contains('\\')
            || canonical.Any(char.IsControl)
            || HasDangerousEncoding(canonical))
        {
            return false;
        }

        normalized = canonical;
        return true;
    }

    private static bool HasDangerousEncoding(string value)
    {
        var current = value;
        for (var pass = 0; pass < 2; pass++)
        {
            if (ContainsEncodedControlOrSeparator(current))
            {
                return true;
            }

            try
            {
                var decoded = Uri.UnescapeDataString(current);
                if (string.Equals(decoded, current, StringComparison.Ordinal))
                {
                    return false;
                }

                current = decoded;
            }
            catch (UriFormatException)
            {
                return true;
            }
        }

        return current.Contains('\\')
            || current.Any(char.IsControl)
            || ContainsEncodedControlOrSeparator(current);
    }

    private static bool ContainsEncodedControlOrSeparator(string value)
    {
        for (var index = 0; index + 2 < value.Length; index++)
        {
            if (value[index] != '%'
                || !byte.TryParse(
                    value.AsSpan(index + 1, 2),
                    System.Globalization.NumberStyles.HexNumber,
                    System.Globalization.CultureInfo.InvariantCulture,
                    out var octet))
            {
                continue;
            }

            if (octet is <= 0x1F or 0x7F or 0x2F or 0x5C or 0x25)
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsReservedPath(string path, string segment) =>
        string.Equals(path, segment, StringComparison.OrdinalIgnoreCase)
        || path.StartsWith($"{segment}/", StringComparison.OrdinalIgnoreCase);
}
