using Terminal.Common.Models;

namespace Terminal.Common.Services;

/// <summary>
/// Provides TN3270E terminal session management over a Telnet/TCP connection.
/// </summary>
public interface ITn3270EService
{
    /// <summary>Gets a value indicating whether a TN3270E session is active.</summary>
    bool IsConnected { get; }

    /// <summary>Gets the host name of the active connection, or <c>null</c> if not connected.</summary>
    string? ConnectedHost { get; }

    /// <summary>Gets the port of the active connection, or <c>0</c> if not connected.</summary>
    int ConnectedPort { get; }

    /// <summary>
    /// Opens a TCP connection to the specified host and port.
    /// </summary>
    Task ConnectAsync(string host, int port, CancellationToken cancellationToken = default);

    /// <summary>
    /// Performs TN3270E option negotiation with the server.
    /// </summary>
    /// <param name="terminalType">The 3270 terminal model string, e.g. <c>IBM-3278-2-E</c>.</param>
    /// <param name="deviceName">Optional LU name requested from the server.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns><c>true</c> if negotiation completed successfully; <c>false</c> if the server rejected it.</returns>
    Task<bool> NegotiateAsync(
        string terminalType,
        string? deviceName = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Sends a TN3270E data frame to the server.
    /// </summary>
    Task SendAsync(Tn3270EFrame frame, CancellationToken cancellationToken = default);

    /// <summary>
    /// Reads the next TN3270E data frame from the server.
    /// </summary>
    Task<Tn3270EFrame> ReceiveAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Closes the active session and releases the underlying TCP connection.
    /// </summary>
    Task DisconnectAsync(CancellationToken cancellationToken = default);
}
