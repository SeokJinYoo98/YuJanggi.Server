using YuJanggiCommon;

namespace YuJanggi.Server.Controllers;

using Models;

public interface IMessageController
{
    IReadOnlyCollection<MessageType> SupportedTypes { get; }

    Task HandleAsync(
        PlayerSession player,
        ChatMessage message,
        CancellationToken cancellationToken = default);
}
