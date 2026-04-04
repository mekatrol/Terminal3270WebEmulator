using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Options;
using System.Net;

namespace Terminal.MockServer.Auth;

internal static class MockIdentityEndpointRouteBuilderExtensions
{
    public static IEndpointRouteBuilder MapMockIdentityEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var options = endpoints.ServiceProvider.GetRequiredService<IOptions<MockIdentityOptions>>().Value;
        var issuerUri = new Uri(options.Issuer, UriKind.Absolute);
        var discoveryPath = $"{issuerUri.AbsolutePath.TrimEnd('/')}/.well-known/openid-configuration";

        endpoints.MapGet("/", () => Results.Redirect(options.AuthorizationEndpointPath));

        endpoints.MapGet(discoveryPath, () => Results.Json(new
        {
            issuer = options.Issuer,
            authorization_endpoint = CreateAbsoluteUrl(issuerUri, options.AuthorizationEndpointPath),
            token_endpoint = CreateAbsoluteUrl(issuerUri, options.TokenEndpointPath),
            jwks_uri = CreateAbsoluteUrl(issuerUri, options.JwksEndpointPath),
            userinfo_endpoint = CreateAbsoluteUrl(issuerUri, options.UserInfoEndpointPath),
            end_session_endpoint = CreateAbsoluteUrl(issuerUri, options.LogoutEndpointPath),
            response_types_supported = new[] { "code" },
            subject_types_supported = new[] { "public" },
            id_token_signing_alg_values_supported = new[] { "RS256" },
            scopes_supported = new[]
            {
                "openid",
                "profile",
                "email",
                "offline_access",
                $"{options.Audience}/Terminal.Access",
                $"{options.Audience}/Terminal.Admin",
            },
            claims_supported = new[]
            {
                "sub",
                "oid",
                "tid",
                "name",
                "preferred_username",
                "email",
                "roles",
                "scp",
            },
            token_endpoint_auth_methods_supported = new[] { "none", "client_secret_post" },
            grant_types_supported = new[] { "authorization_code", "refresh_token" },
            code_challenge_methods_supported = new[] { "S256" },
        }));

        endpoints.MapGet(options.JwksEndpointPath, (MockIdentityStore store) =>
            Results.Json(new
            {
                keys = new[] { store.SecurityMaterial.CreateJsonWebKey() },
            }));

        endpoints.MapGet(options.AuthorizationEndpointPath, (
            [FromQuery(Name = "client_id")] string clientId,
            [FromQuery(Name = "redirect_uri")] string redirectUri,
            [FromQuery(Name = "response_type")] string responseType,
            [FromQuery] string scope,
            [FromQuery(Name = "code_challenge")] string? codeChallenge,
            [FromQuery(Name = "code_challenge_method")] string? codeChallengeMethod,
            [FromQuery] string? state,
            [FromQuery] string? nonce,
            MockIdentityStore store) =>
        {
            try
            {
                var request = store.ValidateAuthorizationRequest(
                    clientId,
                    redirectUri,
                    responseType,
                    scope,
                    codeChallenge,
                    codeChallengeMethod,
                    state,
                    nonce);

                return Results.Content(
                    BuildAuthorizePage(request),
                    "text/html; charset=utf-8");
            }
            catch (Exception exception)
            {
                return Results.BadRequest(new
                {
                    error = "invalid_request",
                    error_description = exception.Message,
                });
            }
        });

        endpoints.MapPost(options.AuthorizationEndpointPath, async (
            HttpContext context,
            MockIdentityStore store) =>
        {
            var form = await context.Request.ReadFormAsync(context.RequestAborted);

            try
            {
                var request = store.ValidateAuthorizationRequest(
                    form["client_id"].ToString(),
                    form["redirect_uri"].ToString(),
                    form["response_type"].ToString(),
                    form["scope"].ToString(),
                    form["code_challenge"].ToString(),
                    form["code_challenge_method"].ToString(),
                    form["state"].ToString(),
                    form["nonce"].ToString());

                var user = store.GetUser(
                    form["user_name"].ToString(),
                    form["password"].ToString());
                var code = store.CreateAuthorizationCode(request, user);
                var redirectUri = QueryHelpers.AddQueryString(request.RedirectUri, new Dictionary<string, string?>
                {
                    ["code"] = code,
                    ["state"] = request.State,
                });

                return Results.Redirect(redirectUri);
            }
            catch (Exception exception)
            {
                return Results.Content(
                    BuildAuthorizeErrorPage(exception.Message, form["client_id"].ToString(), form["scope"].ToString()),
                    "text/html; charset=utf-8",
                    statusCode: StatusCodes.Status400BadRequest);
            }
        });

        endpoints.MapPost(options.TokenEndpointPath, async (
            HttpContext context,
            MockIdentityStore store) =>
        {
            var form = await context.Request.ReadFormAsync(context.RequestAborted);

            try
            {
                TokenResponse tokenResponse;
                var grantType = form["grant_type"].ToString();

                if (string.Equals(grantType, "authorization_code", StringComparison.Ordinal))
                {
                    tokenResponse = store.RedeemAuthorizationCode(
                        form["client_id"].ToString(),
                        form["redirect_uri"].ToString(),
                        form["code"].ToString(),
                        form["code_verifier"].ToString(),
                        form["client_secret"].ToString());
                }
                else if (string.Equals(grantType, "refresh_token", StringComparison.Ordinal))
                {
                    tokenResponse = store.RedeemRefreshToken(
                        form["client_id"].ToString(),
                        form["client_secret"].ToString(),
                        form["refresh_token"].ToString());
                }
                else
                {
                    throw new InvalidOperationException("Only authorization_code and refresh_token grants are supported.");
                }

                return Results.Json(new
                {
                    token_type = "Bearer",
                    access_token = tokenResponse.AccessToken,
                    expires_in = (int)Math.Max(1, (tokenResponse.AccessTokenExpiresAtUtc - DateTimeOffset.UtcNow).TotalSeconds),
                    scope = string.Join(' ', tokenResponse.Scopes),
                    id_token = tokenResponse.IdToken,
                    refresh_token = tokenResponse.RefreshToken,
                });
            }
            catch (Exception exception)
            {
                context.Response.StatusCode = StatusCodes.Status400BadRequest;
                return Results.Json(new
                {
                    error = "invalid_grant",
                    error_description = exception.Message,
                });
            }
        });

        endpoints.MapGet(options.UserInfoEndpointPath, (
            HttpContext context,
            MockIdentityStore store) =>
        {
            try
            {
                var accessToken = ResolveBearerToken(context.Request);
                var accessTokenRecord = store.GetAccessToken(accessToken);
                return Results.Json(new
                {
                    sub = accessTokenRecord.User.SubjectId,
                    name = accessTokenRecord.User.DisplayName,
                    preferred_username = accessTokenRecord.User.UserName,
                    email = accessTokenRecord.User.Email,
                    oid = accessTokenRecord.User.ObjectId,
                    roles = accessTokenRecord.User.Roles,
                });
            }
            catch (Exception exception)
            {
                return Results.Json(new
                {
                    error = "invalid_token",
                    error_description = exception.Message,
                }, statusCode: StatusCodes.Status401Unauthorized);
            }
        });

        endpoints.MapGet(options.LogoutEndpointPath, (
            [FromQuery(Name = "post_logout_redirect_uri")] string? postLogoutRedirectUri,
            [FromQuery] string? state,
            MockIdentityStore store) =>
        {
            if (string.IsNullOrWhiteSpace(postLogoutRedirectUri))
            {
                return Results.Content(BuildLoggedOutPage(), "text/html; charset=utf-8");
            }

            var client = options.Clients.SingleOrDefault(candidate =>
                candidate.PostLogoutRedirectUris.Contains(postLogoutRedirectUri, StringComparer.Ordinal));

            if (client is null)
            {
                return Results.BadRequest(new
                {
                    error = "invalid_request",
                    error_description = "The supplied post_logout_redirect_uri is not registered.",
                });
            }

            var redirectUri = string.IsNullOrWhiteSpace(state)
                ? postLogoutRedirectUri
                : QueryHelpers.AddQueryString(postLogoutRedirectUri, "state", state);
            return Results.Redirect(redirectUri);
        });

        return endpoints;
    }

    private static string CreateAbsoluteUrl(Uri issuerUri, string relativePath)
    {
        return new Uri(issuerUri, relativePath).ToString();
    }

    private static string ResolveBearerToken(HttpRequest request)
    {
        var headerValue = request.Headers.Authorization.ToString();

        if (!headerValue.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("A bearer token is required.");
        }

        return headerValue["Bearer ".Length..].Trim();
    }

    private static string BuildAuthorizePage(AuthorizationRequest request)
    {
        var clientName = string.IsNullOrWhiteSpace(request.Client.DisplayName)
            ? request.Client.ClientId
            : request.Client.DisplayName;

        return $$"""
<!doctype html>
<html lang="en">
<head>
  <meta charset="utf-8">
  <title>Terminal Mock Identity</title>
  <style>
    body { font-family: "Segoe UI", sans-serif; background: #eef4f8; color: #122535; margin: 0; }
    main { max-width: 36rem; margin: 4rem auto; padding: 2rem; background: #fff; border-radius: 1rem; box-shadow: 0 1rem 2rem rgba(18, 37, 53, 0.12); }
    h1 { margin-top: 0; }
    p, li { line-height: 1.5; }
    label { display: block; margin-top: 1rem; font-weight: 600; }
    input { width: 100%; box-sizing: border-box; padding: 0.75rem; margin-top: 0.35rem; border: 1px solid #aac0cf; border-radius: 0.5rem; }
    button { margin-top: 1.5rem; padding: 0.85rem 1.25rem; border: none; border-radius: 0.5rem; background: #0f5cab; color: #fff; font-weight: 700; cursor: pointer; }
    .muted { color: #496577; }
    .scope-list { padding-left: 1.25rem; }
  </style>
</head>
<body>
  <main>
    <p class="muted">Mock OpenID Connect provider</p>
    <h1>Sign in to {{WebUtility.HtmlEncode(clientName)}}</h1>
    <p>This endpoint uses an authorization code flow with PKCE and emits Entra-style claims such as <code>oid</code>, <code>preferred_username</code>, <code>roles</code>, and <code>tid</code>.</p>
    <p class="muted">Requested scopes:</p>
    <ul class="scope-list">
      {{string.Join(Environment.NewLine, request.Scopes.Select(scope => $"<li>{WebUtility.HtmlEncode(scope)}</li>"))}}
    </ul>
    <form method="post">
      <input type="hidden" name="client_id" value="{{WebUtility.HtmlEncode(request.Client.ClientId)}}">
      <input type="hidden" name="redirect_uri" value="{{WebUtility.HtmlEncode(request.RedirectUri)}}">
      <input type="hidden" name="response_type" value="code">
      <input type="hidden" name="scope" value="{{WebUtility.HtmlEncode(string.Join(' ', request.Scopes))}}">
      <input type="hidden" name="code_challenge" value="{{WebUtility.HtmlEncode(request.CodeChallenge ?? string.Empty)}}">
      <input type="hidden" name="code_challenge_method" value="S256">
      <input type="hidden" name="state" value="{{WebUtility.HtmlEncode(request.State ?? string.Empty)}}">
      <input type="hidden" name="nonce" value="{{WebUtility.HtmlEncode(request.Nonce ?? string.Empty)}}">
      <label for="user_name">User name</label>
      <input id="user_name" name="user_name" autocomplete="username" value="operator@mockidp.local">
      <label for="password">Password</label>
      <input id="password" name="password" type="password" autocomplete="current-password" value="Passw0rd!">
      <button type="submit">Continue</button>
    </form>
  </main>
</body>
</html>
""";
    }

    private static string BuildAuthorizeErrorPage(string message, string clientId, string scope)
    {
        return $$"""
<!doctype html>
<html lang="en">
<head>
  <meta charset="utf-8">
  <title>Authorization failed</title>
  <style>
    body { font-family: "Segoe UI", sans-serif; background: #f8ecec; color: #4a1919; margin: 0; }
    main { max-width: 32rem; margin: 4rem auto; padding: 2rem; background: #fff; border-radius: 1rem; box-shadow: 0 1rem 2rem rgba(74, 25, 25, 0.12); }
  </style>
</head>
<body>
  <main>
    <h1>Authorization failed</h1>
    <p>{{WebUtility.HtmlEncode(message)}}</p>
    <p>Client: <code>{{WebUtility.HtmlEncode(clientId)}}</code></p>
    <p>Scopes: <code>{{WebUtility.HtmlEncode(scope)}}</code></p>
  </main>
</body>
</html>
""";
    }

    private static string BuildLoggedOutPage()
    {
        return """
<!doctype html>
<html lang="en">
<head>
  <meta charset="utf-8">
  <title>Signed out</title>
  <style>
    body { font-family: "Segoe UI", sans-serif; background: #eef4f8; color: #122535; margin: 0; }
    main { max-width: 32rem; margin: 4rem auto; padding: 2rem; background: #fff; border-radius: 1rem; box-shadow: 0 1rem 2rem rgba(18, 37, 53, 0.12); }
  </style>
</head>
<body>
  <main>
    <h1>Signed out</h1>
    <p>The mock identity session has ended.</p>
  </main>
</body>
</html>
""";
    }
}
