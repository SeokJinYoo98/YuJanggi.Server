using YuJanggi.Server.V2.Handlers;


namespace YuJanggi.Server.V2.GameRoom
{
    using Protocol.InGame;
    using Protocol.Messages;
    using ClientSession;

    /// <summary>인게임 Protocol 요청을 검증하고 서비스 결과를 메시지로 생성·전송합니다.</summary>
    internal sealed class GameHandler : IMessageHandler
    {
        private readonly GameService _gameService;

        public GameHandler(GameService gameService)
        {
            _gameService = gameService;
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
                ClientMessageType.GameSceneReady
                    => HandleGameSceneReadyAsync(
                        session,
                        message,
                        cancellationToken),
                ClientMessageType.MovePieceRequest
                    => HandleMovePieceRequestAsync(
                        session,
                        message,
                        cancellationToken),
                _
                    => throw new InvalidOperationException(
                        $"처리할 수 없는 인게임 메시지입니다: {message.Type}")
            };
        }
        private async Task HandleMovePieceRequestAsync(
            IClientSession session,
            ClientMessage message,
            CancellationToken cancellationToken)
        {
            if (string.IsNullOrWhiteSpace(message.RequestId))
                throw new InvalidOperationException("이동 요청에 RequestId가 없습니다.");

            var request = message.GetPayload<MovePieceRequest>();
            var room = _gameService.GetRoomBySession(session);
            var result = ValidateMove(request);

            var response = ServerMessageFactory.CreateResponse(
                ServerMessageType.MovePieceResponse,
                message.RequestId,
                new MovePieceResponse { Result = result });

            await session.SendAsync(response, cancellationToken);

            switch (result)
            {
                case MovePieceResult.Accepted:
                    var movePieceEvent = ToMovePieceEvent(request);

                    var moved = ServerMessageFactory.CreateEvent(
                        ServerMessageType.MovePieceEvent,
                        movePieceEvent);

                    await room.BroadcastAsync(moved, cancellationToken);

                    break;

                default:
                    break;
            }


        }
        private static MovePieceResult ValidateMove(
            MovePieceRequest request)
        {
            // 현재는 모든 이동을 승인합니다. 장기 규칙 검증은 추후 구현합니다.
            return MovePieceResult.Accepted;
        }
        private async Task HandleGameSceneReadyAsync(
            IClientSession session,
            ClientMessage message,
            CancellationToken cancellationToken)
        {
            _ = message.GetPayload<GameSceneReady>();

            var room = _gameService.MarkPlayerReady(session);
            if (room is null)
                return;

            var started =
                ServerMessageFactory.CreateEvent(
                    ServerMessageType.GameStartEvent,
                    new GameStartEvent
                    {
                        StartedAt = DateTimeOffset.UtcNow
                    });

            await room.BroadcastAsync(started, cancellationToken);
        }
        private static MovePieceEvent ToMovePieceEvent(MovePieceRequest request)
            => new()
            {
                Team = request.Team,
                FromX = request.FromX,
                FromZ = request.FromZ,
                ToX = request.ToX,
                ToZ = request.ToZ
            };
    }
}
