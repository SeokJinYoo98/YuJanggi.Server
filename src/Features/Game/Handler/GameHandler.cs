

namespace YuJanggi.Server.Features.Game
{
    using Protocol.InGame;
    using Protocol.Messages;

    using Core.Messaging;
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
                ClientMessageType.GiveUpRequest
                    => HandleGiveUpRequestAsync(
                        session,
                        message,
                        cancellationToken),
                ClientMessageType.PassTurnRequest
                    => HandlePassTurnRequestAsync(
                        session,
                        message,
                        cancellationToken),
                ClientMessageType.RematchRequest
                    => HandleRematchRequest(
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
        private async Task HandlePassTurnRequestAsync(
            IClientSession session,
            ClientMessage message,
            CancellationToken token)
        {
            var requestId = RequestMessageValidation.RequireRequestId(message, "PassTurn");
            var request = message.GetPayload<GamePlayerRequest>();
            var result = _gameService.ProcessPassTurn(session, request.Requester);

            var response = ServerMessageFactory.CreateResponse(
                ServerMessageType.PassTurnResponse,
                requestId,
                new GameRequestResponse
                {
                    Result = result.Result
                });

            await session.SendAsync(response, token);

            if (result.Result != GameRequestResult.Accepted)
                return;

            var completed = ServerMessageFactory.CreateEvent(
                ServerMessageType.TurnCompletedEvent,
                new TurnCompletedEvent
                {
                    Team = request.Requester,
                    Action = ProtocolTurnAction.Pass
                });

            await BroadcastAsync(result.Targets, completed, token);
        }
        private async Task HandleGiveUpRequestAsync(
            IClientSession session,
            ClientMessage message,
            CancellationToken token)
        {
            var requestId = RequestMessageValidation.RequireRequestId(message, "GiveUp");

            var request = message.GetPayload<GamePlayerRequest>();
            var result = _gameService.ProcessGiveUp(session, request.Requester);

            var response = ServerMessageFactory.CreateResponse(
                ServerMessageType.GiveUpResponse,
                requestId,
                new GameRequestResponse
                {
                    Result = result.Result
                });

            await session.SendAsync(response, token);

            if (!result.IsGameEnded)
                return;

            await BroadcastGameEndedAsync(result, token);

            // 종료된 방은 재대결을 위해 유지하며, 연결 종료 시 기존 경로에서 정리합니다.
            // TODO: 응답 전송 실패 시에도 상대에게 종료 Event를 전달할지 정책을 정합니다.
        }
        private async Task HandleRematchRequest(
            IClientSession session,
            ClientMessage message,
            CancellationToken token)
        {
            var requestId = RequestMessageValidation.RequireRequestId(message, "Rematch");
            var request = message.GetPayload<GamePlayerRequest>();
            var result = _gameService.ProcessRematch(session, request.Requester);

            var response = ServerMessageFactory.CreateResponse(
                ServerMessageType.RematchResponse,
                requestId,
                new GameRequestResponse
                {
                    Result = result.Result
                });

            await session.SendAsync(response, token);

            if (!result.IsConfirmed)
                return;

            var confirmed = ServerMessageFactory.CreateEvent(
                ServerMessageType.RematchConfirmedEvent,
                new RematchConfirmedEvent());

            await BroadcastAsync(result.Targets, confirmed, token);

            // TODO: 상대 연결 종료 또는 확정 Event 전송 실패 시 대기 중인 클라이언트 처리 정책을 정합니다.
        }
        private async Task HandleGameEndRequestAsync(
            IClientSession session,
            ClientMessage message,
            CancellationToken cancellationToken)
        {
            var requestId = RequestMessageValidation.RequireRequestId(message, "게임 종료");

            var request = message.GetPayload<GameEndRequest>();
            var result = _gameService.ProcessGameEnd(session, request);

            var response = ServerMessageFactory.CreateResponse(
                ServerMessageType.GameEndResponse,
                requestId,
                new GameRequestResponse
                {
                    Result = result.Result
                });

            await session.SendAsync(response, cancellationToken);

            if (!result.IsGameEnded)
                return;

            await BroadcastGameEndedAsync(result, cancellationToken);

            // 종료된 방은 재대결을 위해 유지하며, 연결 종료 시 기존 경로에서 정리합니다.
            // TODO: 응답 전송 실패 시에도 상대에게 종료 Event를 전달할지 정책을 정합니다.
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
                    var turnCompletedEvent = ToTurnCompletedEvent(result.ConfirmedMove
                        ?? throw new InvalidOperationException("승인된 이동 데이터가 없습니다."));

                    var moved = ServerMessageFactory.CreateEvent(
                    ServerMessageType.TurnCompletedEvent,
                    turnCompletedEvent);

                    await BroadcastAsync(result.Targets, moved, cancellationToken);

                    if (result.GameEnd is { } gameEnd)
                        await BroadcastGameEndedAsync(gameEnd, cancellationToken);

                    break;

                default:
                    break;
            }


        }
        private static Task BroadcastGameEndedAsync(
            GameEndProcessingResult result,
            CancellationToken cancellationToken)
        {
            var ended = ServerMessageFactory.CreateEvent(
                ServerMessageType.GameEndedEvent,
                new GameEndedEvent
                {
                    EndReason = result.EndReason,
                    Winner = result.Winner,
                    TotalMoves = result.TotalMoves
                });

            return BroadcastAsync(result.Targets, ended, cancellationToken);
        }

        private static TurnCompletedEvent ToTurnCompletedEvent(
            ConfirmedMove move)
            => new()
            {
                Team    = move.Team,
                Action  = ProtocolTurnAction.Move,
                FromX   = move.FromX,
                FromZ   = move.FromZ,
                ToX     = move.ToX,
                ToZ     = move.ToZ
            };

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
            => Task.WhenAll(targets.Select
                (target => target.SendAsync(
                    message,
                    cancellationToken))
                );

        #endregion
    }
}
