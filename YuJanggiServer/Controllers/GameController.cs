using System.Text.Json;
using YuJanggiCommon;

namespace YuJanggi.Server.Controllers;

using Game;
using Models;
using Views;

public sealed class GameController : IMessageController
{
    public IReadOnlyCollection<MessageType> SupportedTypes { get; } =
        new[] { MessageType.LegalMovesRequest, MessageType.MoveRequest };

    private readonly IGameSessionRegistry _games;
    private readonly IServerView _view;

    public GameController(IGameSessionRegistry games, IServerView view)
    {
        _games = games;
        _view = view;
    }

    public Task HandleAsync(
        PlayerSession player,
        ChatMessage message,
        CancellationToken cancellationToken = default)
    {
        return message.Type == MessageType.LegalMovesRequest
            ? GetLegalMovesAsync(player, message, cancellationToken)
            : MoveAsync(player, message, cancellationToken);
    }

    private async Task GetLegalMovesAsync(
        PlayerSession player,
        ChatMessage message,
        CancellationToken cancellationToken)
    {
        LegalMovesRequest request;

        try
        {
            request = message.GetPayload<LegalMovesRequest>();
        }
        catch (Exception exception) when (
            exception is InvalidDataException or JsonException)
        {
            await InvalidRequestAsync(player, message.RequestId,
                "LegalMovesRequest Payload 형식이 올바르지 않습니다.", cancellationToken);
            return;
        }

        if (request.From is null)
        {
            await InvalidRequestAsync(player, message.RequestId,
                "선택 좌표가 필요합니다.", cancellationToken);
            return;
        }

        ErrorCode? sessionError = _games.TryGet(player, out GameSession? game);
        if (sessionError.HasValue || game is null)
        {
            await SendMoveErrorAsync(player, message.RequestId,
                sessionError ?? ErrorCode.GameSessionNotFound, cancellationToken);
            return;
        }

        ErrorCode? moveError = game.TryGetLegalMoves(player, request.From,
            out LegalMovesResult? result);
        if (moveError.HasValue || result is null)
        {
            await SendMoveErrorAsync(player, message.RequestId,
                moveError ?? ErrorCode.InvalidRequest, cancellationToken);
            return;
        }

        await _view.SendAsync(
            player,
            MessageType.LegalMovesResult,
            message.RequestId,
            result,
            cancellationToken);
    }

    private async Task MoveAsync(
        PlayerSession player,
        ChatMessage message,
        CancellationToken cancellationToken)
    {
        MoveRequest request;

        try
        {
            request = message.GetPayload<MoveRequest>();
        }
        catch (Exception exception) when (
            exception is InvalidDataException or JsonException)
        {
            await InvalidRequestAsync(player, message.RequestId,
                "MoveRequest Payload 형식이 올바르지 않습니다.", cancellationToken);
            return;
        }

        if (request.From is null || request.To is null)
        {
            await InvalidRequestAsync(player, message.RequestId,
                "시작 좌표와 도착 좌표가 필요합니다.", cancellationToken);
            return;
        }

        ErrorCode? sessionError = _games.TryGet(player, out GameSession? game);
        if (sessionError.HasValue || game is null)
        {
            await SendMoveErrorAsync(player, message.RequestId,
                sessionError ?? ErrorCode.GameSessionNotFound, cancellationToken);
            return;
        }

        ErrorCode? moveError = game.TryMove(player, request, out MoveResultEvent? result);
        if (moveError.HasValue || result is null)
        {
            await SendMoveErrorAsync(player, message.RequestId,
                moveError ?? ErrorCode.InvalidRequest, cancellationToken);
            return;
        }

        await _view.SendMoveResultAsync(
            game,
            player,
            message.RequestId,
            result,
            cancellationToken);
    }

    private Task InvalidRequestAsync(
        PlayerSession player,
        string? requestId,
        string message,
        CancellationToken cancellationToken)
    {
        return _view.SendErrorAsync(
            player,
            requestId,
            ErrorCode.InvalidRequest,
            message,
            cancellationToken);
    }

    private Task SendMoveErrorAsync(
        PlayerSession player,
        string? requestId,
        ErrorCode errorCode,
        CancellationToken cancellationToken)
    {
        return _view.SendErrorAsync(
            player,
            requestId,
            errorCode,
            GetMoveErrorMessage(errorCode),
            cancellationToken);
    }

    private static string GetMoveErrorMessage(ErrorCode errorCode)
    {
        return errorCode switch
        {
            ErrorCode.NotMatched => "매칭 완료 후 기물을 선택할 수 있습니다.",
            ErrorCode.GameSessionNotFound => "게임 세션을 찾을 수 없습니다.",
            ErrorCode.NotYourTurn => "현재 플레이어의 턴이 아닙니다.",
            ErrorCode.GameNotStarted => "두 플레이어의 포진 선택이 완료되지 않았습니다.",
            ErrorCode.InvalidPosition => "보드 좌표가 올바르지 않습니다.",
            ErrorCode.PieceNotFound => "선택한 위치에 기물이 없습니다.",
            ErrorCode.NotYourPiece => "상대 기물은 선택할 수 없습니다.",
            ErrorCode.IllegalMove => "이동할 수 없는 위치입니다.",
            _ => "이동 요청을 처리할 수 없습니다."
        };
    }
}
