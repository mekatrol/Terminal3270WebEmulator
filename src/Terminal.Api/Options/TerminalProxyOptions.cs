namespace Terminal.Api.Options;

/// <summary>
/// Configures the browser-to-host proxy session exposed over ASP.NET Core WebSockets.
/// </summary>
public sealed class TerminalProxyOptions
{
    public const string SectionName = "TerminalProxy";

    public string WebSocketPath { get; set; } = "/ws/terminal";

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
