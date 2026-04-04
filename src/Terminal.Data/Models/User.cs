namespace Terminal.Data.Models;

/// <summary>
/// Represents an authenticated application user that can own one or more
/// terminal sessions.
/// </summary>
/// <remarks>
/// The user identifier is intentionally provider-agnostic. In most OAuth-based
/// deployments it will be an object identifier claim, but other identity
/// providers may supply a stable email address or similar unique value.
/// </remarks>
public sealed class User
{
    /// <summary>
    /// Gets or sets the stable identity-provider key for the user.
    /// </summary>
    public string UserId { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the human-readable user name recorded for the user.
    /// </summary>
    /// <remarks>
    /// This value is stored separately from <see cref="UserId"/> because many
    /// providers expose a display-oriented identifier that is useful for audit
    /// and operator diagnostics even when the primary key is opaque.
    /// </remarks>
    public string UserName { get; set; } = string.Empty;

    /// <summary>
    /// Gets the terminal sessions currently associated with the user record.
    /// </summary>
    public ICollection<TerminalSession> TerminalSessions { get; } = [];
}
