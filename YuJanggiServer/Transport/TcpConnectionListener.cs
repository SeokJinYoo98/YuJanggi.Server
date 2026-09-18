using System.Net;
using System.Net.Sockets;

namespace YuJanggi.Server.Transport;

public sealed class TcpConnectionListener : IConnectionListener
{
    private readonly TcpListener _listener;

    public TcpConnectionListener(int port)
        : this(new IPEndPoint(IPAddress.Any, port))
    {
    }

    public TcpConnectionListener(IPEndPoint endPoint)
    {
        _listener = new TcpListener(endPoint);
    }

    public void Start() => _listener.Start();

    public async Task<IClientConnection> AcceptAsync(
        CancellationToken cancellationToken = default)
    {
        TcpClient client = await _listener.AcceptTcpClientAsync(cancellationToken);
        return new TcpClientConnection(client);
    }

    public void Stop() => _listener.Stop();
}
