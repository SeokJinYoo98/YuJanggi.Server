using YuJanggiCommon;

namespace YuJanggi.Server.Models;

using Transport;

public sealed class PlayerSession : IDisposable
{
    public IClientConnection Connection { get; }
    public string ClientInfo => Connection.ConnectionInfo;
    public Guid? PlayerId { get; private set; }
    public string? PlayerName { get; private set; }
    public bool IsJoined => PlayerId.HasValue;
    public Guid? GameId { get; private set; }
    public PlayerSide? Side { get; private set; }
    public bool IsMatched => GameId.HasValue;

    public PlayerSession(IClientConnection connection)
    {
        Connection = connection;
    }

    internal bool TryJoin(string playerName, out Guid playerId)
    {
        if (IsJoined)
        {
            playerId = default;
            return false;
        }

        playerId = Guid.NewGuid();
        PlayerId = playerId;
        PlayerName = playerName;
        return true;
    }

    internal void SetMatch(Guid gameId, PlayerSide side)
    {
        GameId = gameId;
        Side = side;
    }

    internal void ClearMatch()
    {
        GameId = null;
        Side = null;
    }

    public void Dispose() => Connection.Dispose();
}
