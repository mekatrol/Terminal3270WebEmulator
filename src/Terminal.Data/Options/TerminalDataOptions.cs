namespace Terminal.Data.Options;

/// <summary>
/// Defines configuration for the Entity Framework Core in-memory store used by
/// the terminal application.
/// </summary>
/// <remarks>
/// The current design intentionally uses the EF Core in-memory provider for
/// runtime storage so the application can evolve its persistence contracts
/// before a durable database is introduced. The configured database name is
/// significant because EF Core uses that name to decide which in-memory store
/// instance should be shared across resolved <c>DbContext</c> instances:
/// https://learn.microsoft.com/ef/core/providers/in-memory/.
/// </remarks>
public sealed class TerminalDataOptions
{
    /// <summary>
    /// The configuration section that binds these options.
    /// </summary>
    public const string SectionName = "TerminalData";

    /// <summary>
    /// Gets or sets the logical EF Core in-memory database name.
    /// </summary>
    /// <remarks>
    /// A stable name allows scoped <c>DbContext</c> instances inside the same
    /// application host to observe the same runtime data, which is required for
    /// cross-request session tracking.
    /// </remarks>
    public string DatabaseName { get; set; } = "TerminalSessions";
}
