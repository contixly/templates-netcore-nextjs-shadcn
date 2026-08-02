using Template.Application.ApiKeys.Ports;

namespace Template.Application.ApiKeys;

public sealed class ApiKeyAuthenticationService(
    IApiKeyCredentialService credentials,
    IApiKeyStore store)
{
    private static readonly TimeSpan MaximumRetryAfter = TimeSpan.FromDays(1);

    public async Task<ApiKeyAuthenticationResult> AuthenticateAsync(string credential, CancellationToken cancellationToken)
    {
        if (credential is null || !credentials.TryHashCanonical(credential, out var hash))
        {
            return ApiKeyAuthenticationResult.Invalid();
        }

        var result = await store.AuthenticateAndConsumeAsync(hash, cancellationToken);
        return result.Outcome == ApiKeyAuthenticationOutcome.RateLimited
            ? ApiKeyAuthenticationResult.RateLimited(
                result.Principal ?? throw new InvalidOperationException(
                    "A rate-limited API key result requires a principal."),
                BoundRetryAfter(result.RetryAfter))
            : result;
    }

    private static TimeSpan BoundRetryAfter(TimeSpan? retryAfter)
    {
        var value = retryAfter.GetValueOrDefault();
        return value <= TimeSpan.Zero ? TimeSpan.FromSeconds(1) : value > MaximumRetryAfter ? MaximumRetryAfter : value;
    }
}
