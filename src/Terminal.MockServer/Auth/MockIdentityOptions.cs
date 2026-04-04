namespace Terminal.MockServer.Auth;

internal sealed class MockIdentityOptions
{
    public const string SectionName = "MockIdentity";

    public string Issuer { get; set; } = "http://localhost:5099/mock-entra/terminaltenant/v2.0";

    public string TenantId { get; set; } = "terminaltenant";

    public string Audience { get; set; } = "api://terminal-api";

    public TimeSpan AuthorizationCodeLifetime { get; set; } = TimeSpan.FromMinutes(5);

    public TimeSpan AccessTokenLifetime { get; set; } = TimeSpan.FromHours(1);

    public TimeSpan IdTokenLifetime { get; set; } = TimeSpan.FromHours(1);

    public TimeSpan RefreshTokenLifetime { get; set; } = TimeSpan.FromHours(8);

    public IList<MockIdentityClientOptions> Clients { get; set; } = [];

    public IList<MockIdentityUserOptions> Users { get; set; } = [];

    public string AuthorizationEndpointPath => $"/mock-entra/{TenantId}/oauth2/v2.0/authorize";

    public string TokenEndpointPath => $"/mock-entra/{TenantId}/oauth2/v2.0/token";

    public string LogoutEndpointPath => $"/mock-entra/{TenantId}/oauth2/v2.0/logout";

    public string UserInfoEndpointPath => $"/mock-entra/{TenantId}/openid/userinfo";

    public string JwksEndpointPath => $"/mock-entra/{TenantId}/discovery/v2.0/keys";
}

internal sealed class MockIdentityClientOptions
{
    public string ClientId { get; set; } = string.Empty;

    public string? ClientSecret { get; set; }

    public string DisplayName { get; set; } = string.Empty;

    public bool RequirePkce { get; set; } = true;

    public IList<string> RedirectUris { get; set; } = [];

    public IList<string> PostLogoutRedirectUris { get; set; } = [];

    public IList<string> AllowedScopes { get; set; } = [];
}

internal sealed class MockIdentityUserOptions
{
    public string SubjectId { get; set; } = Guid.NewGuid().ToString("N");

    public string ObjectId { get; set; } = Guid.NewGuid().ToString();

    public string UserName { get; set; } = string.Empty;

    public string DisplayName { get; set; } = string.Empty;

    public string Email { get; set; } = string.Empty;

    public string Password { get; set; } = string.Empty;

    public IList<string> Roles { get; set; } = [];
}
