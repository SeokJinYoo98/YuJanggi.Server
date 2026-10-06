



namespace YuJanggi.Server.Handlers
{
    using Protocol.Messages;
    using ClientSession;
    internal interface IMessageHandler
    {
        Task HandleAsync(
            IClientSession session,
            ClientMessage message,
            CancellationToken cancellationToken);
    }
}
