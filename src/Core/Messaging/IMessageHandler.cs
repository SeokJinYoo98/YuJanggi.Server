namespace YuJanggi.Server.Core.Messaging
{
    using Protocol.Messages;
    using Core.Sessions;

    internal interface IMessageHandler
    {
        Task HandleAsync(
            IClientSession session,
            ClientMessage message,
            CancellationToken cancellationToken);
    }
}
