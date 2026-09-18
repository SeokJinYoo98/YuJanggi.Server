namespace YuJanggi.Server;

using Hosting;

public sealed class YuJanggiServer
{
    private readonly ServerHost _host;

    public YuJanggiServer(int port)
        : this(port, Random.Shared)
    {
    }

    internal YuJanggiServer(int port, Random matchmakingRandom)
    {
        _host = new ServerHost(port, matchmakingRandom);
    }

    public Task StartAsync(CancellationToken cancellationToken = default)
    {
        return _host.StartAsync(cancellationToken);
    }

    public void PrintClientList() => _host.PrintClientList();
    public void ClearClients() => _host.ClearClients();
    public void Stop() => _host.Stop();
    public Task StopAsync() => _host.StopAsync();
}
