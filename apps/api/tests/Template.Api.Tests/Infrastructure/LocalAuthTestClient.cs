using System.Net.Http.Json;

namespace Template.Api.Tests.Infrastructure;

internal static class LocalAuthTestClient
{
    internal static async Task<string> GetCsrfAsync(HttpClient client)
    {
        var envelope = await client.GetFromJsonAsync<CsrfEnvelope>(
            "/api/v1/auth/csrf",
            TestContext.Current.CancellationToken);
        return envelope!.Data.RequestToken;
    }

    internal static async Task<HttpResponseMessage> CreateScenarioAsync(
        HttpClient client,
        object? body = null)
    {
        var request = new HttpRequestMessage(
            HttpMethod.Post,
            "/api/local-auth/scenario");
        request.Headers.Add("X-CSRF-TOKEN", await GetCsrfAsync(client));
        request.Content = JsonContent.Create(body ?? new { });
        return await client.SendAsync(
            request,
            TestContext.Current.CancellationToken);
    }

    internal static async Task<HttpResponseMessage> SignInAsync(
        HttpClient client,
        string email,
        string password)
    {
        var request = new HttpRequestMessage(
            HttpMethod.Post,
            "/api/local-auth/sign-in");
        request.Headers.Add("X-CSRF-TOKEN", await GetCsrfAsync(client));
        request.Content = JsonContent.Create(new { email, password });
        return await client.SendAsync(
            request,
            TestContext.Current.CancellationToken);
    }

    internal static async Task<HttpResponseMessage> LogoutAsync(HttpClient client)
    {
        var request = new HttpRequestMessage(
            HttpMethod.Post,
            "/api/v1/auth/logout");
        request.Headers.Add("X-CSRF-TOKEN", await GetCsrfAsync(client));
        return await client.SendAsync(
            request,
            TestContext.Current.CancellationToken);
    }

    internal static async Task<HttpResponseMessage> CleanupAsync(HttpClient client)
    {
        var request = new HttpRequestMessage(
            HttpMethod.Delete,
            "/api/local-auth/scenario");
        request.Headers.Add("X-CSRF-TOKEN", await GetCsrfAsync(client));
        return await client.SendAsync(
            request,
            TestContext.Current.CancellationToken);
    }

    internal static async Task<HttpResponseMessage> ConfirmEmailAsync(
        HttpClient client)
    {
        var request = new HttpRequestMessage(
            HttpMethod.Post,
            "/api/local-auth/confirm-email");
        request.Headers.Add("X-CSRF-TOKEN", await GetCsrfAsync(client));
        return await client.SendAsync(
            request,
            TestContext.Current.CancellationToken);
    }

    internal sealed record CsrfEnvelope(CsrfData Data);
    internal sealed record CsrfData(string RequestToken);
    internal sealed record ScenarioEnvelope(ScenarioData Data);
    internal sealed record ScenarioData(
        UserData User,
        string Email,
        string Password,
        string CleanupUrl);
    internal sealed record UserData(
        Guid Id,
        string Name,
        string Email,
        bool EmailVerified,
        string? Image);
}
