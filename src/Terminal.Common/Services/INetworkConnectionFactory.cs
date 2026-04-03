namespace Terminal.Common.Services;

/// <summary>
/// Abstracts TCP stream creation to enable unit testing without a real network.
/// </summary>
public interface INetworkConnectionFactory
{
    /// <summary>
    /// Opens a TCP connection to <paramref name="host"/>:<paramref name="port"/>
    /// and returns the underlying <see cref="Stream"/>.
    /// </summary>
    Task<Stream> ConnectAsync(string host, int port, CancellationToken cancellationToken);
}
