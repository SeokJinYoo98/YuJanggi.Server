namespace YuJanggi.Server.Features.Game
{
    using Core.Sessions;
    using Engine.Domain;
    using Engine.JanggiEngine;
    using Engine.JanggiOption;
    using Protocol.InGame;
    using Protocol.Matching;
    using State;


    /// <summary>한 대국의 두 참가자와 네트워크 룸의 준비·시작·종료 상태를 소유합니다.</summary>
    internal sealed class GameRoom
    {
        #region Fields
        private readonly Lock _sync = new();
        private readonly IJanggiEngine _engine;
        private GameEndProcessingResult? _engineGameEnd;
        private ReadyPlayers _rematchRequestedPlayers = ReadyPlayers.None;

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
        public bool HasEngineGameResult { get { lock (_sync) return _engineGameEnd is not null; } }
        internal GameEndProcessingResult? EngineGameEnd { get { lock (_sync) return _engineGameEnd; } }

        public bool ChoReady { get { lock (_sync) return (_readyPlayers & ReadyPlayers.Cho) != 0; } }
        public bool HanReady { get { lock (_sync) return (_readyPlayers & ReadyPlayers.Han) != 0; } }
        public bool AllReady { get { lock (_sync) return _readyPlayers == ReadyPlayers.All; } }

        public bool Started 
            => State == GameRoomState.Playing;
        public bool Closed 
            => State == GameRoomState.Closed;
        #endregion

        #region Constructors
        public GameRoom(Formation choFormation, Formation hanFormation)
        {
            _engine = JanggiEngineFactory.CreateEngine(new JanggiOptions
            {
                ChoFormation = choFormation,
                HanFormation = hanFormation
            });
            _engine.References.GameEvents.OnTurnCompleted += HandleTurnCompleted;
        }
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

        internal MovePieceResult ProcessMove(MovePieceRequest request)
        {
            lock (_sync)
            {
                if (_state != GameRoomState.Playing)
                    return MovePieceResult.IllegalMove;

                var team = request.Team switch
                {
                    ProtocolPlayerTeam.Cho => PlayerTeam.Cho,
                    ProtocolPlayerTeam.Han => PlayerTeam.Han,
                    _ => PlayerTeam.None
                };

                // Engine 이동 API에는 요청 팀 인자가 없으므로 Engine의 현재 차례와 대조합니다.
                if (team == PlayerTeam.None || _engine.ControllerQuery.CurrentTurn != team)
                    return MovePieceResult.NotYourTurn;

                return _engine.TryProcessTurn(
                    new Pos(request.FromX, request.FromZ),
                    new Pos(request.ToX, request.ToZ))
                    ? MovePieceResult.Accepted
                    : MovePieceResult.IllegalMove;
            }
        }

        internal GameRequestResult ProcessPassTurn(ProtocolPlayerTeam requester)
        {
            lock (_sync)
            {
                if (_state != GameRoomState.Playing)
                    return GameRequestResult.Failed;

                var team = requester switch
                {
                    ProtocolPlayerTeam.Cho => PlayerTeam.Cho,
                    ProtocolPlayerTeam.Han => PlayerTeam.Han,
                    _ => PlayerTeam.None
                };

                if (team == PlayerTeam.None || _engine.ControllerQuery.CurrentTurn != team)
                    return GameRequestResult.Failed;

                _engine.HandleHandicap();
                return GameRequestResult.Accepted;
            }
        }

        /// <summary>이후 준비·시작을 차단합니다. 연결의 해제는 서버가 담당합니다.</summary>
        public void Close()
        {
            lock (_sync)
                _state = GameRoomState.Closed;
        }
        internal GameEndProcessingResult ProcessGiveUp(ProtocolPlayerTeam requester)
        {
            lock (_sync)
            {
                if (_state != GameRoomState.Playing ||
                    requester is not (ProtocolPlayerTeam.Cho or ProtocolPlayerTeam.Han))
                    return new(GameRequestResult.Failed, false, MatchId,
                        ProtocolPlayerTeam.None, 0, Array.Empty<IClientSession>());

                var winner = requester == ProtocolPlayerTeam.Cho
                    ? ProtocolPlayerTeam.Han
                    : ProtocolPlayerTeam.Cho;

                _engine.EndEngine();
                _state = GameRoomState.Ended;
                return new(
                    GameRequestResult.Accepted,
                    true,
                    MatchId,
                    winner,
                    _engine.References.ReadOnlyRecord.Count,
                    Array.Empty<IClientSession>(),
                    ProtocolGameEndReason.GiveUp);
            }
        }
        #endregion

        #region Private Methods
        internal bool TryConfirmRematch(IClientSession session)
        {
            lock (_sync)
            {
                if (_state != GameRoomState.Ended || !Contains(session))
                    return false;

                _rematchRequestedPlayers |= ChoPlayer!.ClientId == session.ClientId
                    ? ReadyPlayers.Cho
                    : ReadyPlayers.Han;

                if (_rematchRequestedPlayers != ReadyPlayers.All)
                    return false;

                _rematchRequestedPlayers = ReadyPlayers.None;
                _readyPlayers = ReadyPlayers.None;
                _state = GameRoomState.WaitingForReady;
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

                if (!_engine.InitEngine() || !_engine.StartEngine())
                    throw new InvalidOperationException("게임 Engine을 시작할 수 없습니다.");

                _engineGameEnd = null;
                _state = GameRoomState.Playing;
            }
        }
        #endregion

        #region Events

        #endregion

        #region Event Handlers
        private void HandleTurnCompleted(TurnData data)
        {
            lock (_sync)
            {
                if (_state != GameRoomState.Playing || _engineGameEnd is not null ||
                    data.GameResult is not { } gameResult)
                    return;

                var winner = gameResult.Winner switch
                {
                    PlayerTeam.Cho => ProtocolPlayerTeam.Cho,
                    PlayerTeam.Han => ProtocolPlayerTeam.Han,
                    PlayerTeam.None => ProtocolPlayerTeam.None,
                    _ => throw new InvalidOperationException("지원하지 않는 Engine 승자입니다.")
                };
                var endReason = gameResult.Type switch
                {
                    GameResult.Draw => ProtocolGameEndReason.Draw,
                    GameResult.CheckMate => ProtocolGameEndReason.CheckMate,
                    GameResult.GiveUp => ProtocolGameEndReason.GiveUp,
                    GameResult.Score => ProtocolGameEndReason.Score,
                    _ => throw new InvalidOperationException("지원하지 않는 Engine 종료 사유입니다.")
                };

                _engineGameEnd = new GameEndProcessingResult(
                    GameRequestResult.Accepted,
                    true,
                    MatchId,
                    winner,
                    _engine.References.ReadOnlyRecord.Count,
                    Array.Empty<IClientSession>(),
                    endReason);
                _state = GameRoomState.Ended;
            }
        }
        #endregion
    }
}
