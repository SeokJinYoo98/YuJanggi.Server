namespace YuJanggi.Server.Transport;

public interface IConnectionListener
{
    void Start();
    Task<IClientConnection> AcceptAsync(CancellationToken cancellationToken = default);
    void Stop();
}
