using System.Text.Json;
using YuJanggiCommon;


namespace YuJanggi.Server.Controllers;

using Models;
using Views;
public sealed class MatchmakingController : IMessageController
{
    public IReadOnlyCollection<MessageType> SupportedTypes { get; } =
        new[] { MessageType.MatchmakingStart, MessageType.MatchmakingCancel };

    private readonly IMatchmakingService _matchmaking;
    private readonly IServerView _view;

    public MatchmakingController(IMatchmakingService matchmaking, IServerView view)
    {
        _matchmaking = matchmaking;
        _view = view;
    }

    public Task HandleAsync(
        PlayerSession player,
        ChatMessage message,
        CancellationToken cancellationToken = default)
    {
        return message.Type == MessageType.MatchmakingStart
            ? StartAsync(player, message, cancellationToken)
            : CancelAsync(player, message, cancellationToken);
    }

    private async Task StartAsync(
        PlayerSession player,
        ChatMessage message,
        CancellationToken cancellationToken)
    {
        MatchmakingStartRequest request;

        try
        {
            request = message.GetPayload<MatchmakingStartRequest>();
        }
        catch (Exception exception) when (
            exception is InvalidDataException or JsonException)
        {
            await _view.SendErrorAsync(
                player,
                message.RequestId,
                ErrorCode.InvalidRequest,
                "MatchmakingStart Payload 형식이 올바르지 않습니다.",
                cancellationToken);
            return;
        }

        MatchmakingModelResult result = _matchmaking.Start(
            player,
            message.RequestId,
            request.SelectFormation);

        if (result.Error is ErrorCode error)
        {
            await _view.SendErrorAsync(
                player,
                message.RequestId,
                error,
                GetErrorMessage(error),
                cancellationToken);
            return;
        }

        if (result.IsWaiting)
        {
            await _view.SendAsync(
                player,
                MessageType.MatchmakingStatus,
                message.RequestId,
                new MatchmakingStatusResponse(MatchmakingState.Waiting),
                cancellationToken);
            return;
        }

        MatchedGame match = result.Match
            ?? throw new InvalidOperationException("매칭 결과에 게임 세션이 없습니다.");
        await _view.SendMatchFoundAsync(match, cancellationToken);

        if (match.Game.AnnounceMatch())
            await _view.SendGameStartAsync(match.Game, cancellationToken);
    }

    private async Task CancelAsync(
        PlayerSession player,
        ChatMessage message,
        CancellationToken cancellationToken)
    {
        try
        {
            _ = message.GetPayload<MatchmakingCancelRequest>();
        }
        catch (Exception exception) when (
            exception is InvalidDataException or JsonException)
        {
            await _view.SendErrorAsync(
                player,
                message.RequestId,
                ErrorCode.InvalidRequest,
                "MatchmakingCancel Payload 형식이 올바르지 않습니다.",
                cancellationToken);
            return;
        }

        if (!_matchmaking.Cancel(player))
        {
            await _view.SendErrorAsync(
                player,
                message.RequestId,
                ErrorCode.NotMatchmaking,
                GetErrorMessage(ErrorCode.NotMatchmaking),
                cancellationToken);
            return;
        }

        await _view.SendAsync(
            player,
            MessageType.MatchmakingStatus,
            message.RequestId,
            new MatchmakingStatusResponse(MatchmakingState.Cancelled),
            cancellationToken);
    }

    private static string GetErrorMessage(ErrorCode errorCode)
    {
        return errorCode switch
        {
            ErrorCode.NotJoined => "참가 완료 후 매칭을 시작할 수 있습니다.",
            ErrorCode.AlreadyMatchmaking => "이미 매칭 대기 중입니다.",
            ErrorCode.NotMatchmaking => "매칭 대기 중이 아닙니다.",
            ErrorCode.AlreadyMatched => "이미 매칭이 완료된 세션입니다.",
            _ => "매칭 요청을 처리할 수 없습니다."
        };
    }
}
