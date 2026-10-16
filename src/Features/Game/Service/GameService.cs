namespace YuJanggi.Server.Features.Game
{
    using Protocol.InGame;
    using Protocol.Matching;

    using Core.Sessions;
    using State;


    internal sealed record ConfirmedMove(
        ProtocolPlayerTeam Team, byte FromX, byte FromZ, byte ToX, byte ToZ);
    internal sealed record MoveProcessingResult(
        MovePieceResult Result, ConfirmedMove? ConfirmedMove,
        IReadOnlyList<IClientSession> Targets, GameEndProcessingResult? GameEnd = null)
    {
        public bool HasGameResult => GameEnd is not null;
    }
    internal sealed record GameStartProcessingResult(
        DateTimeOffset StartedAt, IReadOnlyList<IClientSession> Targets);
    internal sealed record GameEndProcessingResult(
        GameRequestResult Result, bool IsGameEnded, string MatchId, ProtocolPlayerTeam Winner,
        int TotalMoves, IReadOnlyList<IClientSession> Targets,
        ProtocolGameEndReason EndReason = ProtocolGameEndReason.CheckMate);

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
            ArgumentNullException.ThrowIfNull(session);
            if (!session.IsHandshakeCompleted)
                return (GameRequestResult.Failed, Array.Empty<IClientSession>());

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

                var result = room.ProcessPassTurn(requester);

                return (
                    result,
                    result == GameRequestResult.Accepted
                        ? GetTargets(room) : Array.Empty<IClientSession>());
            }
        }
        public GameEndProcessingResult ProcessGiveUp(
            IClientSession session,
            ProtocolPlayerTeam requester)
            => ProcessExternalGameEnd(session, requester);

        public GameEndProcessingResult ProcessGameEnd(
            IClientSession session, GameEndRequest request)
        {
            ArgumentNullException.ThrowIfNull(session);
            ArgumentNullException.ThrowIfNull(request);

            // 정상 결과 보고는 받지 않습니다. Winner / TotalMoves는 서버에서 결정합니다.
            if (request.EndReason != ProtocolGameEndReason.GiveUp)
                return GameEndFailure(GameRequestResult.Failed);

            return ProcessExternalGameEnd(session, requester: null);
        }

        private GameEndProcessingResult ProcessExternalGameEnd(
            IClientSession session,
            ProtocolPlayerTeam? requester)
        {
            ArgumentNullException.ThrowIfNull(session);
            if (!session.IsHandshakeCompleted)
                return GameEndFailure(GameRequestResult.Failed);

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

                if (requester.HasValue && requester.Value != actualTeam)
                    return GameEndFailure(GameRequestResult.Mismatch);

                var result = room.ProcessGiveUp(actualTeam);
                return result.IsGameEnded
                    ? result with { Targets = GetTargets(room) }
                    : result;
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
            ArgumentNullException.ThrowIfNull(session);
            ArgumentNullException.ThrowIfNull(request);
            if (!session.IsHandshakeCompleted)
                return new(MovePieceResult.IllegalMove, null, Array.Empty<IClientSession>());

            var room = _gameRoomManager.GetRoomBySession(session);
            if (room is null)
                return new(MovePieceResult.IllegalMove, null, Array.Empty<IClientSession>());

            lock (room.SyncRoot)
            {
                if (!room.Contains(session) || room.State != GameRoomState.Playing)
                    return new(MovePieceResult.IllegalMove, null, Array.Empty<IClientSession>());

                var actualTeam = room.ChoPlayer?.ClientId == session.ClientId
                    ? ProtocolPlayerTeam.Cho
                    : ProtocolPlayerTeam.Han;

                if (request.Team != actualTeam)
                    return new(MovePieceResult.IllegalMove, null, Array.Empty<IClientSession>());

                var result = room.ProcessMove(request);

                var confirmedMove = result == MovePieceResult.Accepted
                    ? new ConfirmedMove(request.Team, request.FromX, request.FromZ,
                        request.ToX, request.ToZ)
                    : null;

                var targets = result == MovePieceResult.Accepted
                    ? GetTargets(room) : Array.Empty<IClientSession>();
                var gameEnd = result == MovePieceResult.Accepted && room.EngineGameEnd is { } ended
                    ? ended with { Targets = targets }
                    : null;

                return new MoveProcessingResult(
                    result,
                    confirmedMove,
                    targets,
                    gameEnd);
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
        private static IReadOnlyList<IClientSession> GetTargets(GameRoom room)
            => Array.AsReadOnly(new[]
            {
                room.ChoPlayer ?? throw new InvalidOperationException("초 참가자가 초기화되지 않았습니다."),
                room.HanPlayer ?? throw new InvalidOperationException("한 참가자가 초기화되지 않았습니다.")
            });

        #endregion


    }
}
