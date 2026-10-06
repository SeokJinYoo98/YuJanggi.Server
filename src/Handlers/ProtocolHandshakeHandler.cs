using System.Threading;
using System.Threading.Tasks;
using YuJanggi.Engine;
namespace YuJanggi.Server.V2.Handlers
{
    using Protocol.Connection;
    using Protocol.Messages;

    using Transport;
    using View;
    using YuJanggi.Server.V2.ClientSession;

    /// <summary>
    /// 클라이언트의 핸드셰이크 요청을 처리합니다.
    /// </summary>
    internal sealed class ProtocolHandshakeHandler : IMessageHandler
    {
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
                ValidateVersion(request);

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

            if (result == ProtocolHandshakeResult.Success)
                session.CompleteHandshake();
        }

        private static ProtocolHandshakeResult ValidateVersion(
            ProtocolHandshakeRequest request)
        {
            ProtocolHandshakeResult result =
                ProtocolHandshakeResult.Success;

            if (request.YuJanggiProtocolVersion !=
                Protocol.Version.Version.Current)
            {
                result |=
                    ProtocolHandshakeResult.ProtocolVersionMismatch;
            }

            if (request.YuJanggiCoreVersion != Engine.Version.Version.Current)
            {
                result |=
                    ProtocolHandshakeResult.CoreVersionMismatch;
            }

            return result;
        }
    }
}
