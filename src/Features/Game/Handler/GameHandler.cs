

namespace YuJanggi.Server.Features.Game
{
    using Protocol.InGame;
    using Protocol.Messages;

    using Core.Messaging;
    using Connection;
    using Core.Sessions;

    /// <summary>인게임 Protocol 요청을 검증하고 서비스 결과를 메시지로 생성·전송합니다.</summary>
    internal sealed class GameHandler : IMessageHandler
    {
        #region Fields
        private readonly GameService _gameService;
        #endregion

        #region Constructors
        public GameHandler(GameService gameService)
        {
            _gameService = gameService;
        }
        #endregion

        #region Pulbic Methods
        public Task HandleAsync(
            IClientSession session, 
            ClientMessage message,
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
                ClientMessageType.GameEndRequest
                    => HandleGameEndRequestAsync(
                        session, 
                        message, 
                        cancellationToken),
                _
                    => throw new InvalidOperationException(
                        $"처리할 수 없는 인게임 메시지입니다: {message.Type}")
            };
        }
        #endregion

        #region Private Methods
        private async Task HandleGameEndRequestAsync(
            IClientSession session,
            ClientMessage message,
            CancellationToken cancellationToken)
        {
            var requestId = RequestMessageValidation.RequireRequestId(message, "게임 종료");

            var request = message.GetPayload<GameEndRequest>();
            var result = _gameService.ProcessGameEnd(session, request);

            try
            {
                var response = ServerMessageFactory.CreateResponse(
                    ServerMessageType.GameEndResponse,
                    requestId,
                    new GameEndResponse
                    {
                        Result = result.Result
                    });
    
                await session.SendAsync(response, cancellationToken);
    
                if (!result.IsGameEnded)
                    return;
    
                var gameEndedEvent = new GameEndedEvent
                {
                    Winner = result.Winner,
                    TotalMoves = result.TotalMoves
                };
    
                var ended = ServerMessageFactory.CreateEvent(
                    ServerMessageType.GameEndedEvent,
                    gameEndedEvent);
    
                await BroadcastAsync(
                    result.Targets, 
                    ended, 
                    cancellationToken);
            }
            finally
            {
                // TODO: GameEnd 확정 이후 Response/Event 전송을 독립적으로 처리하고,
                // 일부 전송 실패와 관계없이 Room 정리를 보장한다.
                if (result.IsGameEnded)
                    _gameService.CloseGame(result.MatchId);
            }
        }
        private async Task HandleMovePieceRequestAsync(
            IClientSession session,
            ClientMessage message,
            CancellationToken cancellationToken)
        {
            var requestId = RequestMessageValidation.RequireRequestId(message, "이동");

            var request = message.GetPayload<MovePieceRequest>();
            var result = _gameService.ProcessMove(session, request);

            var response = ServerMessageFactory.CreateResponse(
                ServerMessageType.MovePieceResponse,
                requestId,
                new MovePieceResponse { Result = result.Result });

            await session.SendAsync(response, cancellationToken);

            switch (result.Result)
            {
                case MovePieceResult.Accepted:
                    var movePieceEvent = ToMovePieceEvent(result.ConfirmedMove
                        ?? throw new InvalidOperationException("승인된 이동 데이터가 없습니다."));

                    var moved = ServerMessageFactory.CreateEvent(
                        ServerMessageType.MovePieceEvent,
                        movePieceEvent);

                    await BroadcastAsync(result.Targets, moved, cancellationToken);

                    break;

                default:
                    break;
            }


        }
        private async Task HandleGameSceneReadyAsync(
            IClientSession session,
            ClientMessage message,
            CancellationToken cancellationToken)
        {
            _ = message.GetPayload<GameSceneReady>();

            var result = _gameService.MarkPlayerReady(session);
            if (result is null)
                return;

            var started =
                ServerMessageFactory.CreateEvent(
                    ServerMessageType.GameStartEvent,
                    new GameStartEvent
                    {
                        StartedAt = result.StartedAt
                    });

            await BroadcastAsync(result.Targets, started, cancellationToken);
        }
        private static Task BroadcastAsync(
            IReadOnlyList<IClientSession> targets,
            ServerMessage message, 
            CancellationToken cancellationToken)
            => Task.WhenAll(targets.Select(target => target.SendAsync(message, cancellationToken)));
        private static MovePieceEvent ToMovePieceEvent(ConfirmedMove move)
            => new()
            {
                Team = move.Team,
                FromX = move.FromX,
                FromZ = move.FromZ,
                ToX = move.ToX,
                ToZ = move.ToZ
            };
        #endregion
    }
}
