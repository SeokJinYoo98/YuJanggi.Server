namespace YuJanggi.Server.Features.Game
{
    using State;
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
