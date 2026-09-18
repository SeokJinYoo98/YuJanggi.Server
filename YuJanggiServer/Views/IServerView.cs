using YuJanggiCommon;

namespace YuJanggi.Server.Views;

using Game;
using Models;

public interface IServerView
{
    Task SendAsync<TPayload>(PlayerSession player, MessageType type,
        string? requestId, TPayload payload, CancellationToken cancellationToken = default);

    Task SendErrorAsync(PlayerSession player, string? requestId,
        ErrorCode code, string message, CancellationToken cancellationToken = default);

    Task SendMatchFoundAsync(MatchedGame match, CancellationToken cancellationToken = default);
    Task SendGameStartAsync(GameSession game, CancellationToken cancellationToken = default);
    Task SendMoveResultAsync(GameSession game, PlayerSession requester, string? requestId,
        MoveResultEvent result, CancellationToken cancellationToken = default);
    Task SendChatAsync(GameSession game, PlayerSession sender, string? requestId,
        string message, CancellationToken cancellationToken = default);
    Task SendOpponentLeftAsync(PlayerSession opponent, Guid gameId,
        CancellationToken cancellationToken = default);
}
