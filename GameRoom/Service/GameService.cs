namespace YuJanggi.Server.V2.GameRoom
{
    using ClientSession;

    /// <summary>인게임 유스케이스를 조정하며 상태 전환은 게임룸에 위임합니다.</summary>
    internal sealed class GameService
    {
        private readonly GameRoomManager _gameRoomManager;

        public GameService(GameRoomManager gameRoomManager)
        {
            _gameRoomManager = gameRoomManager;
        }

        public GameRoom GetRoomBySession(IClientSession session)
            => _gameRoomManager.GetRoomBySession(session)
                ?? throw new InvalidOperationException("참가 중인 게임룸이 없습니다.");

        /// <summary>이번 준비 요청으로 최초 시작된 룸을 반환하며, 시작 전환이 없으면 null을 반환합니다.</summary>
        public GameRoom? MarkPlayerReady(IClientSession session)
        {
            var room =
                _gameRoomManager.GetRoomBySession(session)
                ?? throw new InvalidOperationException(
                    "참가 중인 게임룸이 없습니다.");

            return room.MarkPlayerReady(session) ? room : null;
        }
    }
}
