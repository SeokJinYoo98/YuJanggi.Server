using YuJanggiCommon;


namespace YuJanggi.Server.Controllers;

using Models;
using Views;

public sealed class MessageDispatcher
{
    private readonly IReadOnlyDictionary<MessageType, IMessageController> _controllers;
    private readonly IServerView _view;

    public MessageDispatcher(
        IEnumerable<IMessageController> controllers,
        IServerView view)
    {
        Dictionary<MessageType, IMessageController> map = new();

        foreach (IMessageController controller in controllers)
        {
            foreach (MessageType type in controller.SupportedTypes)
            {
                if (!map.TryAdd(type, controller))
                    throw new InvalidOperationException(
                        $"메시지 타입 {type}의 컨트롤러가 중복 등록되었습니다.");
            }
        }

        _controllers = map;
        _view = view;
    }

    public Task DispatchAsync(
        PlayerSession player,
        ChatMessage message,
        CancellationToken cancellationToken = default)
    {
        if (_controllers.TryGetValue(message.Type, out IMessageController? controller))
            return controller.HandleAsync(player, message, cancellationToken);

        return _view.SendErrorAsync(
            player,
            message.RequestId,
            ErrorCode.UnsupportedMessageType,
            $"클라이언트가 보낼 수 없는 메시지 타입입니다: {message.Type}",
            cancellationToken);
    }
}
