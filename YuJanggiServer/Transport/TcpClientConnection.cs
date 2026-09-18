using System.Net.Sockets;
using YuJanggiCommon;
namespace YuJanggi.Server.Transport;

public sealed class TcpClientConnection : IClientConnection
{
    public string ConnectionInfo { get; }

    private readonly TcpClient _client;
    private readonly NetworkStream _stream;
    private readonly SemaphoreSlim _sendLock = new(1, 1);
    private int _disposed;

    public TcpClientConnection(TcpClient client)
    {
        _client = client;
        _stream = client.GetStream();
        ConnectionInfo = client.Client.RemoteEndPoint?.ToString() ?? "Unknown";
    }

    public async Task SendAsync(ChatMessage message, CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
        byte[] packet = MessageProtocol.Encode(message);
        await _sendLock.WaitAsync(cancellationToken);

        try
        {
            await _stream.WriteAsync(packet, cancellationToken);
        }
        finally
        {
            _sendLock.Release();
        }
    }

    public async Task<ChatMessage> ReceiveAsync(CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
        byte[] header = new byte[MessageProtocol.HeaderSize];
        await _stream.ReadExactlyAsync(header, cancellationToken);
        int bodyLength = MessageProtocol.DecodeBodyLength(header);
        byte[] body = new byte[bodyLength];
        await _stream.ReadExactlyAsync(body, cancellationToken);
        return MessageProtocol.DecodeBody(body);
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
            return;

        _stream.Dispose();
        _client.Dispose();
    }
}
