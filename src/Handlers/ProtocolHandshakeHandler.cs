namespace YuJanggi.Server.Handlers
{
    using Protocol.Connection;
    using Protocol.Messages;

    using ClientSession;

    /// <summary>
    /// 클라이언트의 핸드셰이크 요청을 처리합니다.
    /// </summary>
    internal sealed class ProtocolHandshakeHandler : IMessageHandler
    {
        private readonly ConnectionService _connectionService;

        public ProtocolHandshakeHandler(ConnectionService connectionService)
        {
            _connectionService = connectionService;
        }

        public async Task HandleAsync(
            IClientSession session,
            ClientMessage message,
            CancellationToken cancellationToken)
        {
            if (message.Type != ClientMessageType.HandshakeRequest)
            {
                throw new InvalidOperationException(
                    $"핸드셰이크 메시지가 아닙니다: {message.Type}");
            }
            if (message.RequestId is null)
            {
                throw new InvalidOperationException(
                    "핸드셰이크 요청 메시지에는 RequestId가 필요합니다.");
            }
            ProtocolHandshakeRequest request =
                message.GetPayload<ProtocolHandshakeRequest>();

            ProtocolHandshakeResult result =
                _connectionService.ValidateHandshake(request);

            var response = new ProtocolHandshakeResponse
            {
                Result = result
            };

            ServerMessage responseMessage =
                ServerMessageFactory.CreateResponse(
                    ServerMessageType.HandshakeResponse,
                    message.RequestId,
                    response);

            await session.SendAsync(
                responseMessage,
                cancellationToken);

            _connectionService.CompleteHandshake(session, result);
        }
    }
}
