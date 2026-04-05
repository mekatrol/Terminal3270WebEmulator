namespace Terminal.Api.Options;

/// <summary>
/// Configures the browser-to-host proxy session exposed over ASP.NET Core WebSockets.
/// </summary>
public sealed class TerminalProxyOptions
{
    public const string SectionName = "TerminalProxy";

    public string WebSocketPath { get; set; } = "/ws/terminal";

    /// <summary>
    /// Gets or sets the human-readable name of the TN3270 or TN3270E endpoint
    /// shown to browser users when the remote system ends their terminal
    /// session.
    /// </summary>
    /// <remarks>
    /// The proxy knows the transport host and port, but operational UI usually
    /// needs a stable business-facing system name rather than a raw socket
    /// address. Keeping that name in configuration allows the deployment to
    /// show "PITS (Platform Integrated Terminal Server)" in development and the real mainframe or
    /// gateway name in production without code changes.
    /// </remarks>
    public string TerminalEndpointDisplayName { get; set; } =
        "PITS (Platform Integrated Terminal Server)";

    /// <summary>
    /// Gets or sets the maximum session lifetime enforced by the proxy. A long default is used so
    /// interactive mainframe sessions are not dropped prematurely while Entra ID and finer-grained
    /// idle/session management are still being introduced.
    /// </summary>
    public TimeSpan SessionLifetime { get; set; } = TimeSpan.FromHours(4);

    /// <summary>
    /// Gets or sets the WebSocket keep-alive cadence used by ASP.NET Core to keep intermediaries from
    /// assuming the upgraded connection has gone idle and silently dropping it.
    /// </summary>
    public TimeSpan WebSocketKeepAliveInterval { get; set; } = TimeSpan.FromSeconds(30);
}
