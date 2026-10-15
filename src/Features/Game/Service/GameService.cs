namespace YuJanggi.Server.Features.Game
{
    using Protocol.InGame;
    using Protocol.Matching;

    using Connection;
    using Core.Sessions;
    using State;


    internal sealed record ConfirmedMove(
        ProtocolPlayerTeam Team, byte FromX, byte FromZ, byte ToX, byte ToZ);
    internal sealed record MoveProcessingResult(
        MovePieceResult Result, ConfirmedMove? ConfirmedMove,
        IReadOnlyList<IClientSession> Targets);
    internal sealed record GameStartProcessingResult(
        DateTimeOffset StartedAt, IReadOnlyList<IClientSession> Targets);
    internal sealed record GameEndSubmission(ProtocolPlayerTeam Winner, int TotalMoves);
    internal sealed record GameEndProcessingResult(
        GameRequestResult Result, bool IsGameEnded, string MatchId, ProtocolPlayerTeam Winner,
        int TotalMoves, IReadOnlyList<IClientSession> Targets);

    /// <summary>게임룸 조회, 요청 판단 및 대국 상태 전환을 담당합니다.</summary>
    internal sealed class GameService
    {
        #region Fields
        private readonly GameRoomManager _gameRoomManager;
        #endregion

        #region Constructors
        public GameService(GameRoomManager gameRoomManager)
        {
            _gameRoomManager = gameRoomManager;
        }
        #endregion

        #region Public Methods
        public (
            GameRequestResult Result,
            bool IsConfirmed,
            IReadOnlyList<IClientSession> Targets)
            ProcessRematch(
                IClientSession session,
                ProtocolPlayerTeam requester)
        {
            var room = _gameRoomManager.GetRoomBySession(session);
            if (room is null)
                return (GameRequestResult.Failed, false, Array.Empty<IClientSession>());

            lock (room.SyncRoot)
            {
                if (!room.Contains(session) || room.State != GameRoomState.Ended)
                    return (GameRequestResult.Failed, false, Array.Empty<IClientSession>());

                var actualTeam = room.ChoPlayer?.ClientId == session.ClientId
                    ? ProtocolPlayerTeam.Cho
                    : ProtocolPlayerTeam.Han;

                if (requester != actualTeam)
                    return (GameRequestResult.Mismatch, false, Array.Empty<IClientSession>());

                bool confirmed = room.TryConfirmRematch(session);

                return (
                    GameRequestResult.Accepted,
                    confirmed,
                    confirmed ? GetTargets(room) : Array.Empty<IClientSession>());
            }
        }

        public (GameRequestResult Result, IReadOnlyList<IClientSession> Targets)
            ProcessPassTurn(
                IClientSession session,
                ProtocolPlayerTeam requester)
        {
            var room = _gameRoomManager.GetRoomBySession(session);
            if (room is null)
                return (GameRequestResult.Failed, Array.Empty<IClientSession>());

            lock (room.SyncRoot)
            {
                if (!room.Contains(session) || room.State != GameRoomState.Playing)
                    return (GameRequestResult.Failed, Array.Empty<IClientSession>());

                var actualTeam = room.ChoPlayer?.ClientId == session.ClientId
                    ? ProtocolPlayerTeam.Cho
                    : ProtocolPlayerTeam.Han;

                if (requester != actualTeam)
                    return (GameRequestResult.Mismatch, Array.Empty<IClientSession>());

                return (GameRequestResult.Accepted, GetTargets(room));
            }
        }
        public GameEndProcessingResult ProcessGiveUp(
            IClientSession session,
            ProtocolPlayerTeam requester)
        {
            ArgumentNullException.ThrowIfNull(session);

            var room = _gameRoomManager.GetRoomBySession(session);
            if (room is null)
                return GameEndFailure(GameRequestResult.Failed);

            lock (room.SyncRoot)
            {
                if (!room.Contains(session) || room.State != GameRoomState.Playing)
                    return GameEndFailure(GameRequestResult.Failed);

                var actualTeam = room.ChoPlayer?.ClientId == session.ClientId
                    ? ProtocolPlayerTeam.Cho
                    : ProtocolPlayerTeam.Han;

                if (requester != actualTeam)
                    return GameEndFailure(GameRequestResult.Mismatch);

                var winner = actualTeam == ProtocolPlayerTeam.Cho
                    ? ProtocolPlayerTeam.Han
                    : ProtocolPlayerTeam.Cho;

                if (!room.TryEndByGiveUp())
                    return GameEndFailure(GameRequestResult.Failed);

                // TODO: 서버의 실제 진행 수로 대체합니다. 현재 GameRoom에는 수 집계가 없습니다.
                const int totalMoves = 0;

                return new GameEndProcessingResult(
                    GameRequestResult.Accepted,
                    true,
                    room.MatchId,
                    winner,
                    totalMoves,
                    GetTargets(room));
            }
        }
        public GameEndProcessingResult ProcessGameEnd(
            IClientSession session, GameEndRequest request)
        {
            ArgumentNullException.ThrowIfNull(session);
            ArgumentNullException.ThrowIfNull(request);
            var room = _gameRoomManager.GetRoomBySession(session);
            if (room is null)
                return GameEndFailure(GameRequestResult.Failed);

            lock (room.SyncRoot)
            {
                if (!room.Contains(session) || room.State != GameRoomState.Playing ||
                    room.HasSubmittedGameEnd(session) || request.TotalMoves < 0 ||
                    !Enum.IsDefined(request.Winner))
                    return GameEndFailure(GameRequestResult.Failed);

                var opponent = room.GetOpponent(session);
                if (opponent is null)
                    return GameEndFailure(GameRequestResult.Failed);

                var submission = new GameEndSubmission(request.Winner, request.TotalMoves);
                var otherSubmission = room.GetGameEndSubmission(opponent);
                // TODO: Engine 이식 후 서버 최종 결과와 각 제출값을 비교합니다.
                // 현재는 양측 Client의 합의만 확인하며 서버 계산 결과가 아닙니다.
                if (otherSubmission is not null && otherSubmission != submission)
                    return GameEndFailure(GameRequestResult.Mismatch);

                room.MarkGameEndSubmitted(session, submission);
                var isGameEnded = room.AllGameEndSubmitted && room.TryMarkEnded();
                return new GameEndProcessingResult(GameRequestResult.Accepted, isGameEnded,
                    room.MatchId, submission.Winner, submission.TotalMoves,
                    isGameEnded ? GetTargets(room) : Array.Empty<IClientSession>());
            }
        }
        public GameStartProcessingResult? MarkPlayerReady(IClientSession session)
        {
            var room = GetRoomBySession(session);
            lock (room.SyncRoot)
            {
                if (room.State != GameRoomState.WaitingForReady)
                    return null;

                room.SetPlayerReady(session);
                if (!room.AllReady)
                    return null;

                room.Start();

                return new GameStartProcessingResult
                    (DateTimeOffset.UtcNow,
                    GetTargets(room));
            }
        }
        public GameRoom GetRoomBySession(IClientSession session)
            => _gameRoomManager.GetRoomBySession(session)
                ?? throw new InvalidOperationException(
                    "참가 중인 게임룸이 없습니다.");
        public MoveProcessingResult ProcessMove(
            IClientSession session,
            MovePieceRequest request)
        {
            ArgumentNullException.ThrowIfNull(request);
            var room = GetRoomBySession(session);
            lock (room.SyncRoot)
            {
                var result = ValidateMove(request);

                var confirmedMove = result == MovePieceResult.Accepted
                    ? new ConfirmedMove(request.Team, request.FromX, request.FromZ,
                        request.ToX, request.ToZ)
                    : null;

                return new MoveProcessingResult(
                    result,
                    confirmedMove,
                    result == MovePieceResult.Accepted
                        ? GetTargets(room) : Array.Empty<IClientSession>());
            }
        }

        public void CloseGame(string matchId)
        {
            if (!_gameRoomManager.TryGetRoom(matchId, out var room) || room is null)
                return;

            lock (room.SyncRoot)
            {
                if (room.State != GameRoomState.Ended)
                    return;

                room.Close();
            }

            _gameRoomManager.RemoveRoom(matchId, room);
        }
        public Task DisconnectPlayerAsync(IClientSession session)
        {
            ArgumentNullException.ThrowIfNull(session);
            // 연결 종료는 진행 상태와 무관하게 닫습니다. 기존 제거 후 Close 순서를 유지합니다.
            var rooms = _gameRoomManager.RemoveRoomsForPlayer(session.ClientId);
            foreach (var room in rooms)
                room.Close();
            return Task.CompletedTask;
        }

        public Task ClearAsync()
        {
            // 등록을 차단하고 컬렉션을 비운 뒤, Manager 잠금 밖에서 각 룸을 닫습니다.
            var rooms = _gameRoomManager.Clear();
            foreach (var room in rooms)
                room.Close();
            return Task.CompletedTask;
        }
        #endregion

        #region Private Methods
        private static GameEndProcessingResult GameEndFailure(GameRequestResult result)
            => new(result, false, string.Empty, ProtocolPlayerTeam.None, 0, Array.Empty<IClientSession>());
        private static MovePieceResult ValidateMove(
            MovePieceRequest request)
        {
            // 기존 임시 동작을 유지합니다. Engine 기반 규칙 검증은 아직 구현하지 않았습니다.
            return MovePieceResult.Accepted;
        }

        private static IReadOnlyList<IClientSession> GetTargets(GameRoom room)
            => Array.AsReadOnly(new[]
            {
                room.ChoPlayer ?? throw new InvalidOperationException("초 참가자가 초기화되지 않았습니다."),
                room.HanPlayer ?? throw new InvalidOperationException("한 참가자가 초기화되지 않았습니다.")
            });

        #endregion


    }
}
