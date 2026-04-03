using System.Net.Sockets;

namespace Terminal.Common.Services.Implementation;

internal sealed class TcpNetworkConnectionFactory : INetworkConnectionFactory
{
    public async Task<Stream> ConnectAsync(string host, int port, CancellationToken cancellationToken)
    {
        var client = new TcpClient();
        await client.ConnectAsync(host, port, cancellationToken);
        return client.GetStream();
    }
}
