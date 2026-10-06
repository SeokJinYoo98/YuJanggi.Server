

namespace YuJanggi.Server.Lobby
{
    using Engine.Domain;

    using ClientSession;
    using GameRoom;
    internal sealed record MatchPair(IClientSession First, IClientSession Second);
    internal sealed record ConfirmedMatch(string MatchId, MatchPair Players);
    internal enum MatchRequestStatus
    {
        Accepted, AlreadyMatching, AlreadyMatched, HandshakeRequired
    }
    internal enum MatchCancelStatus
    {
        Cancelled, AlreadyMatched
    }
    internal enum FormationSubmissionStatus
    {
        Accepted, AlreadySubmitted, NotMatched, InvalidFormation, HandshakeRequired, ServerError
    }

    /// <summary>포진 접수 결과와 이번 접수로 생성된 룸의 확정 정보를 반환합니다.</summary>
    internal readonly record struct FormationSubmission(
        FormationSubmissionStatus Result,
        bool RoomCreated = false,
        string? MatchId = null,
        Formation? ChoFormation = null,
        Formation? HanFormation = null,
        MatchPair? Players = null)
    {
        // 이미 생성된 룸에 대한 중복 제출은 준비 이벤트를 다시 발생시키지 않습니다.
        public bool IsReady => Result == FormationSubmissionStatus.Accepted && RoomCreated &&
            MatchId is not null && ChoFormation.HasValue && HanFormation.HasValue && Players is not null;
    }

    /// <summary>매칭 예약·확정 및 포진 접수를 관리합니다. 메시지 생성이나 네트워크 전송은 하지 않습니다.</summary>
    internal sealed class LobbyService
    {
        private readonly LobbyManager _lobbyManager;
        private readonly GameRoomManager _gameRoomManager;

        public LobbyService(LobbyManager lobbyManager, GameRoomManager gameRoomManager)
        {
            _lobbyManager = lobbyManager;
            _gameRoomManager = gameRoomManager;
        }

        public MatchRequestStatus RequestMatch(IClientSession session, out MatchPair? matchPair)
        {
            matchPair = null;
            lock (_lobbyManager.SyncRoot)
            {
                if (!session.IsHandshakeCompleted)
                    return MatchRequestStatus.HandshakeRequired;
                if (FindMatch(session.ClientId) is { } match)
                    return match.MatchId is null ? MatchRequestStatus.AlreadyMatching : MatchRequestStatus.AlreadyMatched;
                if (_lobbyManager.ContainsQueued(session))
                    return MatchRequestStatus.AlreadyMatching;

                _lobbyManager.Enqueue(session);
                matchPair = _lobbyManager.TryCreateMatchPair();
                return MatchRequestStatus.Accepted;
            }
        }

        public MatchCancelStatus CancelMatch(IClientSession session)
        {
            lock (_lobbyManager.SyncRoot)
            {
                // 쌍을 확보한 이후에는 큐 취소로 상대 예약을 무효화하지 않습니다.
                if (FindMatch(session.ClientId) is not null)
                    return MatchCancelStatus.AlreadyMatched;
                _lobbyManager.RemoveQueued(session);
                return MatchCancelStatus.Cancelled;
            }
        }

        /// <summary>핸들러가 양쪽 Accepted 전송 성공을 확인한 뒤 호출합니다. 룸은 생성하지 않습니다.</summary>
        public ConfirmedMatch? ConfirmMatch(MatchPair pair)
        {
            lock (_lobbyManager.SyncRoot)
            {
                if (!_lobbyManager.TryGetMatch(pair.First.ClientId, out var state) ||
                    !ReferenceEquals(state.Players, pair) || state.MatchId is not null ||
                    !_lobbyManager.TryGetMatch(pair.Second.ClientId, out var other) ||
                    !ReferenceEquals(state, other))
                    return null;

                state.Confirm(Guid.NewGuid().ToString());
                return new ConfirmedMatch(state.MatchId!, pair);
            }
        }

        /// <summary>응답 전송 실패 시 큐 또는 아직 확정되지 않은 예약을 해제합니다.</summary>
        public void RejectPendingMatch(IClientSession session)
        {
            lock (_lobbyManager.SyncRoot)
            {
                _lobbyManager.RemoveQueued(session);
                if (_lobbyManager.TryGetMatch(session.ClientId, out var state) && state.MatchId is null)
                    _lobbyManager.RemoveMatch(state);
            }
        }

        public void RejectPendingPair(MatchPair pair)
        {
            lock (_lobbyManager.SyncRoot)
            {
                if (_lobbyManager.TryGetMatch(pair.First.ClientId, out var state) &&
                    ReferenceEquals(state.Players, pair) && state.MatchId is null)
                    _lobbyManager.RemoveMatch(state);
            }
        }

        /// <summary>현재 확정 매치의 참가자 포진을 한 번 접수하며, 양쪽 접수 시 한 번만 룸을 생성합니다.</summary>
        public FormationSubmission SubmitFormation(IClientSession session, string matchId, Formation formation)
        {
            lock (_lobbyManager.SyncRoot)
            {
                if (!session.IsHandshakeCompleted)
                    return new(FormationSubmissionStatus.HandshakeRequired);
                if (!Enum.IsDefined(formation))
                    return new(FormationSubmissionStatus.InvalidFormation);

                var state = FindMatch(session.ClientId);
                if (state?.MatchId is null || string.IsNullOrWhiteSpace(matchId) ||
                    state.MatchId != matchId)
                    return new(FormationSubmissionStatus.NotMatched);

                bool isCho = state.Players.First.ClientId == session.ClientId;
                Formation? submitted = isCho ? state.ChoFormation : state.HanFormation;
                if (submitted.HasValue)
                    return new(FormationSubmissionStatus.AlreadySubmitted, state.GameTransitionCompleted, state.MatchId);

                state.SetFormation(session, formation);

                if (state.AllFormationsSubmitted && !state.GameTransitionCompleted)
                    CompleteGameTransition(state, session);

                return new(FormationSubmissionStatus.Accepted, state.GameTransitionCompleted, state.MatchId,
                    state.ChoFormation, state.HanFormation, state.Players);
            }
        }

        /// <summary>연결 종료 시 큐와 상대를 포함한 매칭 상태를 정리합니다.</summary>
        public Task DisconnectPlayerAsync(IClientSession session)
        {
            // TODO:
            // 포진 접수 중 연결이 끊기면 양쪽 매칭 상태는 해제되지만 상대에게 종료 메시지는 없습니다.
            // 상대는 다음 제출 시 NotMatched를 받거나 매칭 화면에서 계속 기다릴 수 있습니다.
            // 매칭 실패 이벤트 프로토콜에서 상대 알림과 재매칭 정책을 연결해야 합니다.
            lock (_lobbyManager.SyncRoot)
            {
                _lobbyManager.RemoveQueued(session);
                if (_lobbyManager.TryGetMatch(session.ClientId, out var state))
                    _lobbyManager.RemoveMatch(state);
                return Task.CompletedTask;
            }
        }

        public Task ClearAsync()
        {
            lock (_lobbyManager.SyncRoot)
            {
                _lobbyManager.Clear();
                return Task.CompletedTask;
            }
        }

        private MatchState? FindMatch(Guid clientId)
        {
            if (!_lobbyManager.TryGetMatch(clientId, out var state))
                return null;
            // 진행 중에는 재매칭·중복 제출을 막기 위해 Lobby 기록을 유지합니다.
            // Game이 Room을 제거한 뒤 조회되면 Lobby 기록만 정리합니다.
            if (state.GameTransitionCompleted && !_gameRoomManager.TryGetRoom(state.MatchId!, out _))
            {
                _lobbyManager.RemoveMatch(state);
                return null;
            }
            return state;
        }

        // LobbyManager.SyncRoot 안에서 호출합니다. 생성 이후 Room 생명주기는 Game이 관리합니다.
        private void CompleteGameTransition(MatchState state, IClientSession submittingSession)
        {
            try
            {
                _gameRoomManager.CreateGameRoom(state.MatchId!,
                    state.Players.First, state.Players.Second);
                state.MarkGameTransitionCompleted();
            }
            catch
            {
                // 생성 실패 시 이번 제출만 되돌리고 이전 상대 포진은 유지합니다.
                state.SetFormation(submittingSession, null);
                throw;
            }
        }

    }
}
