using System.Net.Http.Headers;

namespace UrlShortener.IntegrationTests.TestAuth;

public static class HttpClientAuthExtensions
{
    public static HttpClient WithBearerToken(this HttpClient client, string token)
    {
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }
}
