using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Terminal.Test.Integration.TestInfrastructure;

/// <summary>
/// Performs a browserless authorization-code-with-PKCE sign-in against the mock identity provider.
/// </summary>
internal sealed class MockOidcClient(HttpClient httpClient, Uri issuerUri)
{
    private static readonly JsonSerializerOptions _serializerOptions = new(JsonSerializerDefaults.Web);
    private readonly HttpClient _httpClient = httpClient;
    private readonly Uri _issuerUri = issuerUri;

    /// <summary>
    /// Signs in and returns a bearer access token suitable for the API WebSocket.
    /// </summary>
    public async Task<string> AcquireAccessTokenAsync(
        string userName,
        string password,
        CancellationToken cancellationToken)
    {
        var codeVerifier = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32))
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');
        var codeChallenge = ComputePkceChallenge(codeVerifier);
        const string clientId = "terminal-spa";
        const string redirectUri = "http://localhost:5173/auth/callback";
        const string scope = "openid profile email offline_access api://terminal-api/Terminal.Access api://terminal-api/Terminal.Admin";
        var state = Guid.NewGuid().ToString("N");
        var nonce = Guid.NewGuid().ToString("N");

        using var authorizationRequest = new HttpRequestMessage(
            HttpMethod.Post,
            BuildAuthorityEndpointUri("oauth2/v2.0/authorize"))
        {
            Content = new FormUrlEncodedContent(
                new Dictionary<string, string>
                {
                    ["client_id"] = clientId,
                    ["redirect_uri"] = redirectUri,
                    ["response_type"] = "code",
                    ["scope"] = scope,
                    ["code_challenge"] = codeChallenge,
                    ["code_challenge_method"] = "S256",
                    ["state"] = state,
                    ["nonce"] = nonce,
                    ["user_name"] = userName,
                    ["password"] = password,
                }),
        };

        using var authorizationResponse = await _httpClient.SendAsync(
            authorizationRequest,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken);

        if (authorizationResponse.StatusCode != HttpStatusCode.Redirect)
        {
            var body = await authorizationResponse.Content.ReadAsStringAsync(cancellationToken);
            throw new InvalidOperationException(
                $"Mock authorization request failed with status {(int)authorizationResponse.StatusCode}.{Environment.NewLine}{body}");
        }

        var location = authorizationResponse.Headers.Location
            ?? throw new InvalidOperationException("Mock authorization endpoint did not return a redirect location.");
        var code = ExtractQueryParameter(location, "code");

        using var tokenRequest = new HttpRequestMessage(
            HttpMethod.Post,
            BuildAuthorityEndpointUri("oauth2/v2.0/token"))
        {
            Content = new FormUrlEncodedContent(
                new Dictionary<string, string>
                {
                    ["grant_type"] = "authorization_code",
                    ["client_id"] = clientId,
                    ["redirect_uri"] = redirectUri,
                    ["code"] = code,
                    ["code_verifier"] = codeVerifier,
                }),
        };
        tokenRequest.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        using var tokenResponse = await _httpClient.SendAsync(tokenRequest, cancellationToken);
        var tokenJson = await tokenResponse.Content.ReadAsStringAsync(cancellationToken);

        if (!tokenResponse.IsSuccessStatusCode)
        {
            throw new InvalidOperationException(
                $"Mock token request failed with status {(int)tokenResponse.StatusCode}.{Environment.NewLine}{tokenJson}");
        }

        var tokenPayload = JsonSerializer.Deserialize<TokenResponsePayload>(tokenJson, _serializerOptions)
            ?? throw new InvalidOperationException("Mock token response could not be deserialized.");

        if (string.IsNullOrWhiteSpace(tokenPayload.AccessToken))
        {
            throw new InvalidOperationException("Mock token response did not include an access token.");
        }

        return tokenPayload.AccessToken;
    }

    private Uri BuildAuthorityEndpointUri(string relativePath)
    {
        const string authorityVersionSegment = "/v2.0";
        var issuerPath = _issuerUri.AbsolutePath.TrimEnd('/');
        var authorityPath = issuerPath.EndsWith(authorityVersionSegment, StringComparison.OrdinalIgnoreCase)
            ? issuerPath[..^authorityVersionSegment.Length]
            : issuerPath;
        var path = $"{authorityPath}/{relativePath.TrimStart('/')}";
        return new Uri($"{_issuerUri.Scheme}://{_issuerUri.Authority}{path}", UriKind.Absolute);
    }

    private static string ComputePkceChallenge(string codeVerifier)
    {
        var digest = SHA256.HashData(Encoding.ASCII.GetBytes(codeVerifier));
        return Convert.ToBase64String(digest)
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');
    }

    private static string ExtractQueryParameter(Uri uri, string parameterName)
    {
        var query = uri.Query.TrimStart('?');
        foreach (var segment in query.Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var parts = segment.Split('=', 2);
            if (parts.Length == 2 && string.Equals(parts[0], parameterName, StringComparison.Ordinal))
            {
                return Uri.UnescapeDataString(parts[1]);
            }
        }

        throw new InvalidOperationException($"Query parameter '{parameterName}' was not present in redirect URI '{uri}'.");
    }

    private sealed record TokenResponsePayload(
        [property: JsonPropertyName("access_token")] string AccessToken);
}
