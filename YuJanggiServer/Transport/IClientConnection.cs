using YuJanggiCommon;


namespace YuJanggi.Server.Transport;

public interface IClientConnection : IDisposable
{
    string ConnectionInfo { get; }

    Task SendAsync(ChatMessage message, CancellationToken cancellationToken = default);
    Task<ChatMessage> ReceiveAsync(CancellationToken cancellationToken = default);
}
