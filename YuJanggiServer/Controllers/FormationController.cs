using System.Text.Json;
using YuJanggiCommon;

namespace YuJanggi.Server.Controllers;

using Game;
using Models;
using Views;
public sealed class FormationController : IMessageController
{
    public IReadOnlyCollection<MessageType> SupportedTypes { get; } =
        new[] { MessageType.SelectFormation };

    private readonly IGameSessionRegistry _games;
    private readonly IServerView _view;

    public FormationController(IGameSessionRegistry games, IServerView view)
    {
        _games = games;
        _view = view;
    }

    public async Task HandleAsync(
        PlayerSession player,
        ChatMessage message,
        CancellationToken cancellationToken = default)
    {
        SelectFormationRequest request;

        try
        {
            request = message.GetPayload<SelectFormationRequest>();
        }
        catch (Exception exception) when (
            exception is InvalidDataException or JsonException)
        {
            await _view.SendErrorAsync(
                player,
                message.RequestId,
                ErrorCode.InvalidRequest,
                "포진 선택 요청 형식이 올바르지 않습니다.",
                cancellationToken);
            return;
        }

        ErrorCode? sessionError = _games.TryGet(player, out GameSession? game);
        if (sessionError.HasValue || game is null)
        {
            await _view.SendErrorAsync(
                player,
                message.RequestId,
                sessionError ?? ErrorCode.GameSessionNotFound,
                "매칭된 대국을 찾을 수 없습니다.",
                cancellationToken);
            return;
        }

        ErrorCode? error = game.TrySelectFormation(player, request, out bool started);
        if (error.HasValue)
        {
            await _view.SendErrorAsync(
                player,
                message.RequestId,
                error.Value,
                GetErrorMessage(error.Value),
                cancellationToken);
            return;
        }

        await _view.SendAsync(
            player,
            MessageType.FormationSelected,
            message.RequestId,
            new FormationSelectedResponse(game.GameId, request.Formation),
            cancellationToken);

        if (started)
            await _view.SendGameStartAsync(game, cancellationToken);
    }

    private static string GetErrorMessage(ErrorCode errorCode)
    {
        return errorCode switch
        {
            ErrorCode.InvalidFormation => "선택할 수 없는 포진입니다.",
            ErrorCode.FormationAlreadySelected => "이미 포진을 확정했습니다.",
            ErrorCode.GameAlreadyStarted => "이미 시작된 대국입니다.",
            _ => "매칭된 대국을 찾을 수 없습니다."
        };
    }
}
