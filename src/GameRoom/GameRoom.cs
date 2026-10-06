namespace YuJanggi.Server.V2.GameRoom
{
    using ClientSession;
    using State;


    /// <summary>한 대국의 두 참가자와 네트워크 룸의 준비·시작·종료 상태를 소유합니다.</summary>
    internal sealed class GameRoom
    {
        #region Fields
        private readonly Lock _sync = new();
        private GameEndSubmittedPlayers _gameEndSubmittedPlayers = GameEndSubmittedPlayers.None;
        private GameEndSubmission? _choGameEndSubmission;
        private GameEndSubmission? _hanGameEndSubmission;

        private ReadyPlayers _readyPlayers 
            = ReadyPlayers.None;
        private GameRoomState _state 
            = GameRoomState.WaitingForReady;
        #endregion

        #region Properties
        internal Lock SyncRoot
            => _sync;

        public string MatchId { get; private set; } 
            = string.Empty;

        public IClientSession? ChoPlayer { get; private set; }
        public IClientSession? HanPlayer { get; private set; }

        public GameRoomState State { get { lock (_sync) return _state; } }

        public bool ChoReady { get { lock (_sync) return (_readyPlayers & ReadyPlayers.Cho) != 0; } }
        public bool HanReady { get { lock (_sync) return (_readyPlayers & ReadyPlayers.Han) != 0; } }
        public bool AllReady { get { lock (_sync) return _readyPlayers == ReadyPlayers.All; } }
        public bool AllGameEndSubmitted
        {
            get { lock (_sync) return _gameEndSubmittedPlayers == GameEndSubmittedPlayers.All; }
        }

        public bool Started 
            => State == GameRoomState.Playing;
        public bool Closed 
            => State == GameRoomState.Closed;
        #endregion

        #region Public Methods
        public void Initialize(string matchId, IClientSession choPlayer, IClientSession hanPlayer)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(matchId);
            ArgumentNullException.ThrowIfNull(choPlayer);
            ArgumentNullException.ThrowIfNull(hanPlayer);
            if (choPlayer.ClientId == hanPlayer.ClientId)
                throw new ArgumentException("서로 다른 두 세션이 필요합니다.");

            lock (_sync)
            {
                if (_state != GameRoomState.WaitingForReady || MatchId.Length != 0)
                    throw new InvalidOperationException("초기화할 수 없는 게임룸입니다.");
                MatchId = matchId;
                ChoPlayer = choPlayer;
                HanPlayer = hanPlayer;
            }
        }
        public bool Contains(IClientSession session)
        {
            ArgumentNullException.ThrowIfNull(session);
            return ChoPlayer?.ClientId == session.ClientId || HanPlayer?.ClientId == session.ClientId;
        }
        public IClientSession? GetOpponent(IClientSession session)
        {
            ArgumentNullException.ThrowIfNull(session);
            if (ChoPlayer?.ClientId == session.ClientId)
                return HanPlayer;
            if (HanPlayer?.ClientId == session.ClientId)
                return ChoPlayer;
            return null;
        }

        /// <summary>이후 준비·시작을 차단합니다. 연결의 해제는 서버가 담당합니다.</summary>
        public void Close()
        {
            lock (_sync)
                _state = GameRoomState.Closed;
        }
        #endregion

        #region Private Methods
        internal bool HasSubmittedGameEnd(IClientSession session)
            => GetGameEndSubmission(session) is not null;

        internal GameEndSubmission? GetGameEndSubmission(IClientSession session)
        {
            ArgumentNullException.ThrowIfNull(session);
            lock (_sync)
            {
                if (ChoPlayer?.ClientId == session.ClientId)
                    return _choGameEndSubmission;
                if (HanPlayer?.ClientId == session.ClientId)
                    return _hanGameEndSubmission;
                throw new InvalidOperationException("룸 참가자가 아닙니다.");
            }
        }

        internal void MarkGameEndSubmitted(IClientSession session, GameEndSubmission submission)
        {
            ArgumentNullException.ThrowIfNull(submission);
            lock (_sync)
            {
                if (_state != GameRoomState.Playing || HasSubmittedGameEnd(session))
                    throw new InvalidOperationException("게임 종료 결과를 제출할 수 없습니다.");
                if (ChoPlayer!.ClientId == session.ClientId)
                {
                    _choGameEndSubmission = submission;
                    _gameEndSubmittedPlayers |= GameEndSubmittedPlayers.Cho;
                }
                else
                {
                    _hanGameEndSubmission = submission;
                    _gameEndSubmittedPlayers |= GameEndSubmittedPlayers.Han;
                }
            }
        }

        internal bool TryMarkEnded()
        {
            lock (_sync)
            {
                if (_state != GameRoomState.Playing || !AllGameEndSubmitted ||
                    _choGameEndSubmission != _hanGameEndSubmission)
                    return false;
                _state = GameRoomState.Ended;
                return true;
            }
        }

        /// <summary>참가자의 준비 상태만 기록합니다. 시작 판단은 Service가 담당합니다.</summary>
        internal void SetPlayerReady(IClientSession session)
        {
            ArgumentNullException.ThrowIfNull(session);
            lock (_sync)
            {
                if (!Contains(session))
                    throw new InvalidOperationException("룸 참가자가 아닙니다.");
                if (_state != GameRoomState.WaitingForReady)
                    throw new InvalidOperationException("준비 상태를 변경할 수 없는 게임룸입니다.");

                if (ChoPlayer!.ClientId == session.ClientId)
                    _readyPlayers |= ReadyPlayers.Cho;
                else
                    _readyPlayers |= ReadyPlayers.Han;
            }
        }

        internal void Start()
        {
            lock (_sync)
            {
                if (_state != GameRoomState.WaitingForReady || _readyPlayers != ReadyPlayers.All)
                    throw new InvalidOperationException("시작할 수 없는 게임룸입니다.");
                _state = GameRoomState.Playing;
            }
        }
        #endregion

        #region Events

        #endregion

        #region Event Handlers
        #endregion
    }
}
