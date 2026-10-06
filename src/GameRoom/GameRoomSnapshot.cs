using YuJanggi.Server.V2.GameRoom.State;

namespace YuJanggi.Server.V2.GameRoom
{
    internal sealed record GameRoomPlayerSnapshot(
        Guid ClientId,
        string? Nickname,
        string ConnectionInfo,
        bool Ready);

    internal sealed record GameRoomSnapshot(
        string MatchId,
        GameRoomState State,
        GameRoomPlayerSnapshot? Cho,
        GameRoomPlayerSnapshot? Han);
}
