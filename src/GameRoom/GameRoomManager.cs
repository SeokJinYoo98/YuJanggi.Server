namespace YuJanggi.Server.GameRoom
{
    using ClientSession;

    /// <summary>게임룸 컬렉션의 생성, 조회, 제거를 관리합니다.</summary>
    internal sealed class GameRoomManager : IAsyncDisposable
    {
        #region Fields
        private readonly ClientSessionManager _sessionManager;
        private readonly Lock _roomSync;
        private readonly Dictionary<string, GameRoom> _gameRooms = new();
        private bool _stopping;
        #endregion

        #region Constructors
        public GameRoomManager(ClientSessionManager sessionManager, Lock roomSync)
        {
            _sessionManager = sessionManager;
            _roomSync = roomSync;
        }
        #endregion

        #region Public Methods
        public GameRoom[] GetRoomsSnapshot()
        {
            lock (_roomSync)
                return _gameRooms.Values.ToArray();
        }

        public GameRoom CreateGameRoom(string matchId, IClientSession choPlayer, IClientSession hanPlayer)
        {
            var room = new GameRoom();
            room.Initialize(matchId, choPlayer, hanPlayer);
            lock (_roomSync)
            {
                if (_stopping)
                    throw new InvalidOperationException("종료 중인 서버에는 룸을 생성할 수 없습니다.");
                if (!_sessionManager.Contains(choPlayer.ClientId) ||
                    !_sessionManager.Contains(hanPlayer.ClientId))
                    throw new InvalidOperationException("연결이 종료된 참가자의 룸을 생성할 수 없습니다.");
                if (_gameRooms.ContainsKey(matchId))
                    throw new InvalidOperationException("이미 사용 중인 대국 ID입니다.");
                if (_gameRooms.Values.Any(candidate => candidate.Contains(choPlayer) || candidate.Contains(hanPlayer)))
                    throw new InvalidOperationException("이미 게임룸에 참가 중인 세션입니다.");
                _gameRooms.Add(matchId, room);
                return room;
            }
        }

        public bool TryGetRoom(string matchId, out GameRoom? room)
        {
            lock (_roomSync)
                return _gameRooms.TryGetValue(matchId, out room);
        }

        public GameRoom? GetRoomBySession(IClientSession session)
        {
            ArgumentNullException.ThrowIfNull(session);
            lock (_roomSync)
            {
                if (_stopping || !_sessionManager.Contains(session.ClientId))
                    return null;
                return _gameRooms.Values.SingleOrDefault(room => room.Contains(session));
            }
        }

        public bool RemoveRoom(string matchId, GameRoom expectedRoom)
        {
            lock (_roomSync)
            {
                if (!_gameRooms.TryGetValue(matchId, out var room) ||
                    !ReferenceEquals(room, expectedRoom))
                    return false;
                return _gameRooms.Remove(matchId);
            }
        }

        public GameRoom[] RemoveRoomsForPlayer(Guid clientId)
        {
            GameRoom[] rooms;
            lock (_roomSync)
            {
                rooms = _gameRooms.Values.Where(room =>
                    room.ChoPlayer?.ClientId == clientId || room.HanPlayer?.ClientId == clientId).ToArray();
                foreach (var room in rooms)
                    _gameRooms.Remove(room.MatchId);
            }
            return rooms;
        }

        public GameRoom[] Clear()
        {
            GameRoom[] rooms;
            lock (_roomSync)
            {
                _stopping = true;
                rooms = _gameRooms.Values.ToArray();
                _gameRooms.Clear();
            }
            return rooms;
        }

        // Dispose는 컬렉션만 정리합니다. 게임 종료는 GameService가 담당합니다.
        public ValueTask DisposeAsync()
        {
            Clear();
            return ValueTask.CompletedTask;
        }

        #endregion
    }
}
