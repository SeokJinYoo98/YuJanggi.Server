using YuJanggiCommon;

namespace YuJanggi.Server.Views;

using Game;
using Models;

public sealed class ServerView : IServerView
{
    public Task SendAsync<TPayload>(
        PlayerSession player,
        MessageType type,
        string? requestId,
        TPayload payload,
        CancellationToken cancellationToken = default)
    {
        return player.Connection.SendAsync(
            ChatMessage.Create(type, requestId, payload),
            cancellationToken);
    }

    public Task SendErrorAsync(
        PlayerSession player,
        string? requestId,
        ErrorCode code,
        string message,
        CancellationToken cancellationToken = default)
    {
        return SendAsync(
            player,
            MessageType.Error,
            requestId,
            new ErrorResponse(code, message),
            cancellationToken);
    }

    public Task SendMatchFoundAsync(
        MatchedGame match,
        CancellationToken cancellationToken = default)
    {
        MatchedPlayer cho = CreateMatchedPlayer(match.Cho.Player);
        MatchedPlayer han = CreateMatchedPlayer(match.Han.Player);
        string message = $"초: {cho.PlayerName}\n한: {han.PlayerName}";

        return Task.WhenAll(
            SendAsync(
                match.Cho.Player,
                MessageType.MatchFound,
                match.Cho.RequestId,
                new MatchFoundResponse(match.Game.GameId, han, PlayerSide.Cho)
                {
                    Message = message,
                    RequiresFormationSelection = match.Cho.SelectFormation
                },
                cancellationToken),
            SendAsync(
                match.Han.Player,
                MessageType.MatchFound,
                match.Han.RequestId,
                new MatchFoundResponse(match.Game.GameId, cho, PlayerSide.Han)
                {
                    Message = message,
                    RequiresFormationSelection = match.Han.SelectFormation
                },
                cancellationToken));
    }

    public Task SendGameStartAsync(
        GameSession game,
        CancellationToken cancellationToken = default)
    {
        return Task.WhenAll(
            SendAsync(
                game.ChoPlayer,
                MessageType.GameStart,
                null,
                game.CreateGameStart(PlayerSide.Cho),
                cancellationToken),
            SendAsync(
                game.HanPlayer,
                MessageType.GameStart,
                null,
                game.CreateGameStart(PlayerSide.Han),
                cancellationToken));
    }

    public Task SendMoveResultAsync(
        GameSession game,
        PlayerSession requester,
        string? requestId,
        MoveResultEvent result,
        CancellationToken cancellationToken = default)
    {
        PlayerSession opponent = game.GetOpponent(requester);
        return Task.WhenAll(
            SendAsync(
                requester,
                MessageType.MoveResult,
                requestId,
                result,
                cancellationToken),
            SendAsync(
                opponent,
                MessageType.MoveResult,
                null,
                result,
                cancellationToken));
    }

    public Task SendChatAsync(
        GameSession game,
        PlayerSession sender,
        string? requestId,
        string message,
        CancellationToken cancellationToken = default)
    {
        MatchedPlayer senderInfo = CreateMatchedPlayer(sender);
        PlayerSession opponent = game.GetOpponent(sender);
        GameChatReceivedEvent chatEvent = new(
            game.GameId,
            senderInfo.PlayerId,
            senderInfo.PlayerName,
            message,
            DateTimeOffset.UtcNow);

        return Task.WhenAll(
            SendAsync(
                sender,
                MessageType.GameChatReceived,
                requestId,
                chatEvent,
                cancellationToken),
            SendAsync(
                opponent,
                MessageType.GameChatReceived,
                null,
                chatEvent,
                cancellationToken));
    }

    public Task SendOpponentLeftAsync(
        PlayerSession opponent,
        Guid gameId,
        CancellationToken cancellationToken = default)
    {
        return SendAsync(
            opponent,
            MessageType.GameEnd,
            null,
            new GameEndEvent(
                gameId,
                GameEndReason.OpponentLeft,
                "상대 플레이어가 채팅방을 나갔습니다."),
            cancellationToken);
    }

    private static MatchedPlayer CreateMatchedPlayer(PlayerSession player)
    {
        if (player.PlayerId is not Guid playerId ||
            player.PlayerName is not string playerName)
        {
            throw new InvalidOperationException(
                "참가하지 않은 세션은 매칭될 수 없습니다.");
        }

        return new MatchedPlayer(playerId, playerName);
    }
}
