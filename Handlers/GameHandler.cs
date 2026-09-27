using System.Text.Json;


namespace YuJanggi.Server.V2.Handlers
{
    using Protocol.V2.InGame;
    using Protocol.V2.Messages;
    using Protocol.V2.Messages.MessageFactory;
    using ClientSession;
    using GameRoom;
    /// <summary>인게임 요청을 해석하고 게임룸에 처리를 위임합니다.</summary>
    internal sealed class GameHandler : IMessageHandler
    {
        private readonly GameRoomManager _gameRoomManager;

        public GameHandler(GameRoomManager gameRoomManager)
        {
            _gameRoomManager = gameRoomManager;
        }
        public Task HandleAsync(
            IClientSession session, ClientMessage message,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (!session.IsHandshakeCompleted)
                throw new InvalidOperationException(
                    "Handshake가 완료되지 않았습니다.");

            return message.Type switch
            {
                ClientMessageType.GameSceneReadyRequest
                    => HandleGameSceneReadyAsync(
                        session,
                        message,
                        cancellationToken),

                // 추후 추가
                // ClientMessageType.MovePieceRequest
                //     => HandleMovePieceAsync(session, message, cancellationToken),

                // ClientMessageType.GiveUpRequest
                //     => HandleGiveUpAsync(session, message, cancellationToken),

                _ => throw new InvalidOperationException(
                    $"처리할 수 없는 인게임 메시지입니다: {message.Type}")
            };
        }
        private async Task HandleGameSceneReadyAsync(
            IClientSession session,
            ClientMessage message,
            CancellationToken cancellationToken)
        {
            if (string.IsNullOrWhiteSpace(message.RequestId))
            {
                throw new InvalidDataException(
                    "게임 준비 요청에 RequestId가 없습니다.");
            }

            if (message.Payload is not
                {
                    ValueKind: JsonValueKind.Object
                })
            {
                throw new InvalidDataException(
                    "게임 준비 Payload는 JSON 객체여야 합니다.");
            }

            _ = message.GetPayload<GameSceneReadyRequest>();

            var room =
                _gameRoomManager.GetRoomBySession(session)
                ?? throw new InvalidOperationException(
                    "참가 중인 게임룸이 없습니다.");

            if (!room.MarkPlayerReady(session))
                return;

            var started =
                ServerMessageFactory.CreateEvent(
                    ServerMessageType.GameStartEvent,
                    new GameStartEvent
                    {
                        StartedAt = DateTimeOffset.UtcNow
                    });

            await Task.WhenAll(
                SendStartAsync(
                    room.ChoPlayer!,
                    started,
                    cancellationToken),
                SendStartAsync(
                    room.HanPlayer!,
                    started,
                    cancellationToken));
        }
        private static async Task SendStartAsync(IClientSession player, ServerMessage message,
            CancellationToken cancellationToken)
        {
            await player.SendAsync(message, cancellationToken);
        }
    }
}
