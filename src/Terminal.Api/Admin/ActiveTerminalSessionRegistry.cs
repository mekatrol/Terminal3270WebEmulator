using System.Collections.Concurrent;

namespace Terminal.Api.Admin;

/// <summary>
/// Tracks live WebSocket-backed terminal sessions so administrative actions can
/// request termination of active browser-to-host connections.
/// </summary>
/// <remarks>
/// The session database records describe durable session state, but only the
/// in-process host can cancel an active proxy loop. This registry bridges those
/// concerns by associating a persisted session identifier with the cancellation
/// source that controls the live proxy pipeline.
/// </remarks>
internal sealed class ActiveTerminalSessionRegistry
{
    private readonly ConcurrentDictionary<Guid, SessionRegistration> _registrations = new();

    /// <summary>
    /// Registers the cancellation source that controls an active terminal
    /// session.
    /// </summary>
    /// <param name="sessionId">The persisted terminal session identifier.</param>
    /// <param name="cancellationSource">
    /// The linked cancellation source that terminates the proxy loop.
    /// </param>
    public void Register(Guid sessionId, CancellationTokenSource cancellationSource)
    {
        ArgumentNullException.ThrowIfNull(cancellationSource);

        _registrations[sessionId] = new SessionRegistration(cancellationSource);
    }

    /// <summary>
    /// Removes a previously-registered active session from the registry.
    /// </summary>
    /// <param name="sessionId">The persisted terminal session identifier.</param>
    public void Unregister(Guid sessionId)
    {
        _ = _registrations.TryRemove(sessionId, out _);
    }

    /// <summary>
    /// Requests termination of an active session when one is currently tracked.
    /// </summary>
    /// <param name="sessionId">The session identifier to terminate.</param>
    /// <returns>
    /// <see langword="true"/> when an active in-process session was found and a
    /// cancellation request was issued; otherwise <see langword="false"/>.
    /// </returns>
    public bool TryRequestTermination(Guid sessionId)
    {
        if (!_registrations.TryGetValue(sessionId, out var registration))
        {
            return false;
        }

        registration.MarkTerminationRequested();
        registration.CancellationSource.Cancel();
        return true;
    }

    /// <summary>
    /// Gets whether administrative termination was requested for a session.
    /// </summary>
    /// <param name="sessionId">The session identifier to inspect.</param>
    /// <returns>
    /// <see langword="true"/> when termination was requested through the admin
    /// surface; otherwise <see langword="false"/>.
    /// </returns>
    public bool WasTerminationRequested(Guid sessionId)
        => _registrations.TryGetValue(sessionId, out var registration) &&
           registration.TerminationRequested;

    private sealed class SessionRegistration(CancellationTokenSource cancellationSource)
    {
        private int _terminationRequested;

        public CancellationTokenSource CancellationSource { get; } = cancellationSource;

        public bool TerminationRequested => _terminationRequested == 1;

        public void MarkTerminationRequested()
        {
            _ = Interlocked.Exchange(ref _terminationRequested, 1);
        }
    }
}
