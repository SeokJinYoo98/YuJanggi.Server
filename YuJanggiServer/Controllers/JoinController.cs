using System.Text.Json;
using YuJanggiCommon;


namespace YuJanggi.Server.Controllers;

using Models;
using Views;

public sealed class JoinController : IMessageController
{
    public IReadOnlyCollection<MessageType> SupportedTypes { get; } =
        new[] { MessageType.Join };

    private readonly IPlayerRegistry _players;
    private readonly IServerView _view;

    public JoinController(IPlayerRegistry players, IServerView view)
    {
        _players = players;
        _view = view;
    }

    public async Task HandleAsync(
        PlayerSession player,
        ChatMessage message,
        CancellationToken cancellationToken = default)
    {
        JoinRequest request;

        try
        {
            request = message.GetPayload<JoinRequest>();
        }
        catch (Exception exception) when (
            exception is InvalidDataException or JsonException)
        {
            await _view.SendErrorAsync(
                player,
                message.RequestId,
                ErrorCode.InvalidRequest,
                "Join Payload 형식이 올바르지 않습니다.",
                cancellationToken);
            return;
        }

        string playerName = request.PlayerName?.Trim() ?? string.Empty;

        if (string.IsNullOrWhiteSpace(playerName))
        {
            await _view.SendErrorAsync(
                player,
                message.RequestId,
                ErrorCode.PlayerNameRequired,
                "플레이어 이름은 필수입니다.",
                cancellationToken);
            return;
        }

        if (playerName.Length > JoinRules.MaxPlayerNameLength)
        {
            await _view.SendErrorAsync(
                player,
                message.RequestId,
                ErrorCode.PlayerNameTooLong,
                $"플레이어 이름은 {JoinRules.MaxPlayerNameLength}자 이하여야 합니다.",
                cancellationToken);
            return;
        }

        JoinModelResult result = _players.Join(player, playerName);
        if (result.Error is ErrorCode error)
        {
            string errorMessage = error == ErrorCode.DuplicatePlayerName
                ? "이미 사용 중인 플레이어 이름입니다."
                : "이미 참가한 세션입니다.";
            await _view.SendErrorAsync(
                player,
                message.RequestId,
                error,
                errorMessage,
                cancellationToken);
            return;
        }

        await _view.SendAsync(
            player,
            MessageType.Join,
            message.RequestId,
            new JoinResponse(result.PlayerId, playerName),
            cancellationToken);
    }
}
