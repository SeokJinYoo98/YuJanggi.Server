using System.Text.Json;
using YuJanggiCommon;


namespace YuJanggi.Server.Controllers;

using Game;
using Models;
using Views;
public sealed class GameChatController : IMessageController
{
    public IReadOnlyCollection<MessageType> SupportedTypes { get; } =
        new[] { MessageType.GameChatSend };

    private readonly IGameSessionRegistry _games;
    private readonly IServerView _view;

    public GameChatController(IGameSessionRegistry games, IServerView view)
    {
        _games = games;
        _view = view;
    }

    public async Task HandleAsync(
        PlayerSession player,
        ChatMessage message,
        CancellationToken cancellationToken = default)
    {
        GameChatSendRequest request;

        try
        {
            request = message.GetPayload<GameChatSendRequest>();
        }
        catch (Exception exception) when (
            exception is InvalidDataException or JsonException)
        {
            await _view.SendErrorAsync(
                player,
                message.RequestId,
                ErrorCode.InvalidRequest,
                "GameChatSend Payload 형식이 올바르지 않습니다.",
                cancellationToken);
            return;
        }

        string chatMessage = request.Message?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(chatMessage))
        {
            await _view.SendErrorAsync(
                player,
                message.RequestId,
                ErrorCode.ChatMessageRequired,
                "채팅 메시지는 필수입니다.",
                cancellationToken);
            return;
        }

        if (chatMessage.Length > GameChatRules.MaxMessageLength)
        {
            await _view.SendErrorAsync(
                player,
                message.RequestId,
                ErrorCode.ChatMessageTooLong,
                $"채팅 메시지는 {GameChatRules.MaxMessageLength}자 이하여야 합니다.",
                cancellationToken);
            return;
        }

        ErrorCode? sessionError = _games.TryGet(player, out GameSession? game);
        if (sessionError.HasValue || game is null)
        {
            ErrorCode error = sessionError ?? ErrorCode.GameSessionNotFound;
            await _view.SendErrorAsync(
                player,
                message.RequestId,
                error,
                error == ErrorCode.NotMatched
                    ? "매칭 완료 후 채팅을 보낼 수 있습니다."
                    : "게임 세션을 찾을 수 없습니다.",
                cancellationToken);
            return;
        }

        await _view.SendChatAsync(
            game,
            player,
            message.RequestId,
            chatMessage,
            cancellationToken);
    }
}
