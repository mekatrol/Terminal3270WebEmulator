namespace Terminal.Data.Models;

/// <summary>
/// Represents a proxied TN3270 or TN3270E session that is active or was
/// previously active through the web application.
/// </summary>
/// <remarks>
/// Session records exist so the API can track the lifecycle of each browser to
/// mainframe connection, including when it began, whether it is still active,
/// and when it closed.
/// </remarks>
public sealed class TerminalSession
{
    /// <summary>
    /// Gets or sets the primary key for the session record.
    /// </summary>
    public Guid TerminalSessionId { get; set; }

    /// <summary>
    /// Gets or sets the UTC timestamp describing when the session was created.
    /// </summary>
    public DateTimeOffset CreatedDateTimeUtc { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether the session is currently active.
    /// </summary>
    /// <remarks>
    /// The explicit active flag makes it cheap to query only live sessions
    /// without needing to infer activity from nullable close timestamps.
    /// </remarks>
    public bool IsActive { get; set; }

    /// <summary>
    /// Gets or sets the UTC timestamp describing when the session closed.
    /// </summary>
    /// <remarks>
    /// This value remains <see langword="null"/> while the terminal session is
    /// active and is populated when the proxy session ends.
    /// </remarks>
    public DateTimeOffset? ClosedDateTimeUtc { get; set; }

    /// <summary>
    /// Gets or sets the user identifier for the session owner.
    /// </summary>
    public string UserId { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the user that owns the terminal session.
    /// </summary>
    public User User { get; set; } = null!;
}
