namespace Terminal.Api.Options;

internal sealed class OidcAuthenticationOptions
{
    public const string SectionName = "Authentication";

    public string Authority { get; set; } = "http://localhost:5099/mock-entra/terminaltenant/v2.0";

    public string Audience { get; set; } = "api://terminal-api";

    public bool RequireHttpsMetadata { get; set; }

    public string TerminalUserRole { get; set; } = "Terminal.User";

    public string TerminalAdminRole { get; set; } = "Terminal.Admin";

    public string ServerAdminRole { get; set; } = "Server.Admin";
}
