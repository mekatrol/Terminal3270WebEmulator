using Microsoft.Extensions.Options;
using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;

namespace Terminal.MockServer.Auth;

internal sealed class MockIdentityStore(IOptions<MockIdentityOptions> identityOptions)
{
    private readonly MockIdentityOptions _identityOptions = identityOptions.Value;
    private readonly ConcurrentDictionary<string, AuthorizationCodeRecord> _authorizationCodes = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, RefreshTokenRecord> _refreshTokens = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, AccessTokenRecord> _accessTokens = new(StringComparer.Ordinal);
    private readonly RsaSecurityMaterial _securityMaterial = RsaSecurityMaterial.Create();

    public RsaSecurityMaterial SecurityMaterial => _securityMaterial;

    public MockIdentityClientOptions GetClient(string clientId)
    {
        var client = _identityOptions.Clients.SingleOrDefault(candidate =>
            string.Equals(candidate.ClientId, clientId, StringComparison.Ordinal));

        return client ?? throw new InvalidOperationException($"Unknown client '{clientId}'.");
    }

    public MockIdentityUserOptions GetUser(string userName, string password)
    {
        var user = _identityOptions.Users.SingleOrDefault(candidate =>
            string.Equals(candidate.UserName, userName, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(candidate.Password, password, StringComparison.Ordinal));

        return user ?? throw new InvalidOperationException("The supplied username or password is invalid.");
    }

    public AuthorizationRequest ValidateAuthorizationRequest(
        string clientId,
        string redirectUri,
        string responseType,
        string scope,
        string? codeChallenge,
        string? codeChallengeMethod,
        string? state,
        string? nonce)
    {
        if (!string.Equals(responseType, "code", StringComparison.Ordinal))
        {
            throw new InvalidOperationException("Only the authorization code response type is supported.");
        }

        var client = GetClient(clientId);

        if (!client.RedirectUris.Contains(redirectUri, StringComparer.Ordinal))
        {
            throw new InvalidOperationException("The requested redirect URI is not registered for this client.");
        }

        if (client.RequirePkce)
        {
            if (string.IsNullOrWhiteSpace(codeChallenge))
            {
                throw new InvalidOperationException("PKCE is required for this client.");
            }

            if (!string.Equals(codeChallengeMethod, "S256", StringComparison.Ordinal))
            {
                throw new InvalidOperationException("Only S256 PKCE challenges are supported.");
            }
        }

        var requestedScopes = scope
            .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        if (requestedScopes.Length == 0)
        {
            throw new InvalidOperationException("At least one scope must be requested.");
        }

        var unsupportedScope = requestedScopes.FirstOrDefault(requestedScope =>
            !client.AllowedScopes.Contains(requestedScope, StringComparer.Ordinal));

        if (unsupportedScope is not null)
        {
            throw new InvalidOperationException($"The scope '{unsupportedScope}' is not registered for client '{clientId}'.");
        }

        return new AuthorizationRequest(client, redirectUri, requestedScopes, codeChallenge, state, nonce);
    }

    public string CreateAuthorizationCode(AuthorizationRequest request, MockIdentityUserOptions user)
    {
        RemoveExpiredEntries();

        var code = CreateToken();
        var expiresAtUtc = DateTimeOffset.UtcNow.Add(_identityOptions.AuthorizationCodeLifetime);
        _authorizationCodes[code] = new AuthorizationCodeRecord(request, user, expiresAtUtc);
        return code;
    }

    public TokenResponse RedeemAuthorizationCode(
        string clientId,
        string redirectUri,
        string code,
        string? codeVerifier,
        string? clientSecret)
    {
        RemoveExpiredEntries();

        if (!_authorizationCodes.TryRemove(code, out var codeRecord))
        {
            throw new InvalidOperationException("The supplied authorization code is invalid or has already been redeemed.");
        }

        if (codeRecord.ExpiresAtUtc <= DateTimeOffset.UtcNow)
        {
            throw new InvalidOperationException("The supplied authorization code has expired.");
        }

        ValidateClientForTokenRequest(clientId, redirectUri, clientSecret, codeRecord.Request.Client);

        if (codeRecord.Request.Client.RequirePkce)
        {
            if (string.IsNullOrWhiteSpace(codeVerifier))
            {
                throw new InvalidOperationException("The PKCE code verifier is required.");
            }

            var actualCodeChallenge = ComputePkceChallenge(codeVerifier);

            if (!string.Equals(actualCodeChallenge, codeRecord.Request.CodeChallenge, StringComparison.Ordinal))
            {
                throw new InvalidOperationException("The supplied PKCE code verifier does not match the authorization request.");
            }
        }

        return IssueTokens(codeRecord.User, codeRecord.Request.Client, codeRecord.Request.Scopes, codeRecord.Request.Nonce);
    }

    public TokenResponse RedeemRefreshToken(
        string clientId,
        string? clientSecret,
        string refreshToken)
    {
        RemoveExpiredEntries();

        if (!_refreshTokens.TryGetValue(refreshToken, out var refreshRecord))
        {
            throw new InvalidOperationException("The supplied refresh token is invalid.");
        }

        if (refreshRecord.ExpiresAtUtc <= DateTimeOffset.UtcNow)
        {
            _refreshTokens.TryRemove(refreshToken, out _);
            throw new InvalidOperationException("The supplied refresh token has expired.");
        }

        ValidateClientSecret(clientId, clientSecret, refreshRecord.Client);
        return IssueTokens(refreshRecord.User, refreshRecord.Client, refreshRecord.Scopes, nonce: null);
    }

    public AccessTokenRecord GetAccessToken(string accessToken)
    {
        RemoveExpiredEntries();

        if (!_accessTokens.TryGetValue(accessToken, out var accessTokenRecord))
        {
            throw new InvalidOperationException("The supplied access token is invalid.");
        }

        if (accessTokenRecord.ExpiresAtUtc <= DateTimeOffset.UtcNow)
        {
            _accessTokens.TryRemove(accessToken, out _);
            throw new InvalidOperationException("The supplied access token has expired.");
        }

        return accessTokenRecord;
    }

    public TokenResponse IssueTokens(
        MockIdentityUserOptions user,
        MockIdentityClientOptions client,
        IReadOnlyCollection<string> scopes,
        string? nonce)
    {
        var nowUtc = DateTimeOffset.UtcNow;
        var accessTokenExpiresAtUtc = nowUtc.Add(_identityOptions.AccessTokenLifetime);
        var idTokenExpiresAtUtc = nowUtc.Add(_identityOptions.IdTokenLifetime);

        var accessToken = CreateJwt(
            BuildAccessTokenClaims(user, scopes, nowUtc, accessTokenExpiresAtUtc),
            audience: _identityOptions.Audience);

        var idToken = scopes.Contains("openid", StringComparer.Ordinal)
            ? CreateJwt(
                BuildIdTokenClaims(user, client, nonce, nowUtc, idTokenExpiresAtUtc),
                audience: client.ClientId)
            : null;

        _accessTokens[accessToken] = new AccessTokenRecord(
            user,
            scopes,
            accessTokenExpiresAtUtc);

        string? refreshToken = null;

        if (scopes.Contains("offline_access", StringComparer.Ordinal))
        {
            refreshToken = CreateToken();
            _refreshTokens[refreshToken] = new RefreshTokenRecord(
                user,
                client,
                scopes,
                nowUtc.Add(_identityOptions.RefreshTokenLifetime));
        }

        return new TokenResponse(
            accessToken,
            accessTokenExpiresAtUtc,
            idToken,
            refreshToken,
            scopes);
    }

    private static void ValidateClientForTokenRequest(
        string clientId,
        string redirectUri,
        string? clientSecret,
        MockIdentityClientOptions client)
    {
        if (!string.Equals(client.ClientId, clientId, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("The client ID does not match the original authorization request.");
        }

        if (!client.RedirectUris.Contains(redirectUri, StringComparer.Ordinal))
        {
            throw new InvalidOperationException("The supplied redirect URI is not registered for this client.");
        }

        ValidateClientSecret(clientId, clientSecret, client);
    }

    private static void ValidateClientSecret(
        string clientId,
        string? clientSecret,
        MockIdentityClientOptions client)
    {
        if (!string.Equals(client.ClientId, clientId, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("The supplied client is unknown.");
        }

        if (string.IsNullOrWhiteSpace(client.ClientSecret))
        {
            return;
        }

        if (!string.Equals(client.ClientSecret, clientSecret, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("The supplied client secret is invalid.");
        }
    }

    private IEnumerable<KeyValuePair<string, object?>> BuildAccessTokenClaims(
        MockIdentityUserOptions user,
        IReadOnlyCollection<string> scopes,
        DateTimeOffset issuedAtUtc,
        DateTimeOffset expiresAtUtc)
    {
        var apiScopes = scopes
            .Where(scope => scope.StartsWith($"{_identityOptions.Audience}/", StringComparison.Ordinal))
            .Select(scope => scope[(scope.LastIndexOf('/') + 1)..])
            .ToArray();

        yield return new("iss", _identityOptions.Issuer);
        yield return new("sub", user.SubjectId);
        yield return new("aud", _identityOptions.Audience);
        yield return new("iat", issuedAtUtc.ToUnixTimeSeconds());
        yield return new("nbf", issuedAtUtc.ToUnixTimeSeconds());
        yield return new("exp", expiresAtUtc.ToUnixTimeSeconds());
        yield return new("jti", Guid.NewGuid().ToString("N"));
        yield return new("tid", _identityOptions.TenantId);
        yield return new("oid", user.ObjectId);
        yield return new("name", user.DisplayName);
        yield return new("preferred_username", user.UserName);
        yield return new("email", user.Email);
        yield return new("scp", string.Join(' ', apiScopes));
        yield return new("roles", user.Roles);
    }

    private IEnumerable<KeyValuePair<string, object?>> BuildIdTokenClaims(
        MockIdentityUserOptions user,
        MockIdentityClientOptions client,
        string? nonce,
        DateTimeOffset issuedAtUtc,
        DateTimeOffset expiresAtUtc)
    {
        yield return new("iss", _identityOptions.Issuer);
        yield return new("sub", user.SubjectId);
        yield return new("aud", client.ClientId);
        yield return new("iat", issuedAtUtc.ToUnixTimeSeconds());
        yield return new("nbf", issuedAtUtc.ToUnixTimeSeconds());
        yield return new("exp", expiresAtUtc.ToUnixTimeSeconds());
        yield return new("jti", Guid.NewGuid().ToString("N"));
        yield return new("tid", _identityOptions.TenantId);
        yield return new("oid", user.ObjectId);
        yield return new("name", user.DisplayName);
        yield return new("preferred_username", user.UserName);
        yield return new("email", user.Email);
        yield return new("roles", user.Roles);

        if (!string.IsNullOrWhiteSpace(nonce))
        {
            yield return new("nonce", nonce);
        }
    }

    private string CreateJwt(IEnumerable<KeyValuePair<string, object?>> claims, string audience)
    {
        var header = new Dictionary<string, object?>
        {
            ["alg"] = "RS256",
            ["typ"] = "JWT",
            ["kid"] = _securityMaterial.KeyId,
        };

        var payload = claims.ToDictionary(claim => claim.Key, claim => claim.Value, StringComparer.Ordinal);
        payload["aud"] = audience;

        var headerSegment = Base64UrlEncode(System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(header));
        var payloadSegment = Base64UrlEncode(System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(payload));
        var signingInput = Encoding.UTF8.GetBytes($"{headerSegment}.{payloadSegment}");
        var signature = _securityMaterial.Sign(signingInput);
        return $"{headerSegment}.{payloadSegment}.{Base64UrlEncode(signature)}";
    }

    private static string CreateToken()
    {
        Span<byte> buffer = stackalloc byte[32];
        RandomNumberGenerator.Fill(buffer);
        return Base64UrlEncode(buffer);
    }

    private static string ComputePkceChallenge(string verifier)
    {
        var bytes = SHA256.HashData(Encoding.ASCII.GetBytes(verifier));
        return Base64UrlEncode(bytes);
    }

    private void RemoveExpiredEntries()
    {
        var nowUtc = DateTimeOffset.UtcNow;
        RemoveExpiredEntries(_authorizationCodes, entry => entry.Value.ExpiresAtUtc <= nowUtc);
        RemoveExpiredEntries(_refreshTokens, entry => entry.Value.ExpiresAtUtc <= nowUtc);
        RemoveExpiredEntries(_accessTokens, entry => entry.Value.ExpiresAtUtc <= nowUtc);
    }

    private static void RemoveExpiredEntries<T>(
        ConcurrentDictionary<string, T> entries,
        Func<KeyValuePair<string, T>, bool> isExpired)
    {
        foreach (var entry in entries.Where(isExpired))
        {
            entries.TryRemove(entry.Key, out _);
        }
    }

    private static string Base64UrlEncode(ReadOnlySpan<byte> bytes)
    {
        return Convert.ToBase64String(bytes)
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');
    }
}

internal sealed record AuthorizationRequest(
    MockIdentityClientOptions Client,
    string RedirectUri,
    IReadOnlyCollection<string> Scopes,
    string? CodeChallenge,
    string? State,
    string? Nonce);

internal sealed record TokenResponse(
    string AccessToken,
    DateTimeOffset AccessTokenExpiresAtUtc,
    string? IdToken,
    string? RefreshToken,
    IReadOnlyCollection<string> Scopes);

internal sealed record AuthorizationCodeRecord(
    AuthorizationRequest Request,
    MockIdentityUserOptions User,
    DateTimeOffset ExpiresAtUtc);

internal sealed record RefreshTokenRecord(
    MockIdentityUserOptions User,
    MockIdentityClientOptions Client,
    IReadOnlyCollection<string> Scopes,
    DateTimeOffset ExpiresAtUtc);

internal sealed record AccessTokenRecord(
    MockIdentityUserOptions User,
    IReadOnlyCollection<string> Scopes,
    DateTimeOffset ExpiresAtUtc);

internal sealed class RsaSecurityMaterial : IDisposable
{
    private readonly RSA _rsa;

    private RsaSecurityMaterial(RSA rsa, string keyId)
    {
        _rsa = rsa;
        KeyId = keyId;
    }

    public string KeyId { get; }

    public static RsaSecurityMaterial Create()
    {
        var rsa = RSA.Create(2048);
        return new RsaSecurityMaterial(rsa, Guid.NewGuid().ToString("N"));
    }

    public byte[] Sign(ReadOnlySpan<byte> data)
    {
        return _rsa.SignData(data, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
    }

    public object CreateJsonWebKey()
    {
        var parameters = _rsa.ExportParameters(false);
        return new
        {
            kty = "RSA",
            use = "sig",
            kid = KeyId,
            alg = "RS256",
            n = Base64UrlEncode(parameters.Modulus),
            e = Base64UrlEncode(parameters.Exponent),
        };
    }

    public void Dispose()
    {
        _rsa.Dispose();
    }

    private static string Base64UrlEncode(byte[]? bytes)
    {
        ArgumentNullException.ThrowIfNull(bytes);

        return Convert.ToBase64String(bytes)
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');
    }
}
