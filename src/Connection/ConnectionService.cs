namespace YuJanggi.Server.Connection
{
    using Core.Sessions;
    using Protocol.Connection;
    using View;

    /// <summary>연결 등록, 연결 해제 및 서버 종료 시 Session 정리를 조정합니다.</summary>
    internal sealed class ConnectionService
    {
        private readonly ClientSessionManager _sessionManager;
        private readonly Lock _roomSync;

        public ConnectionService(ClientSessionManager sessionManager, Lock roomSync)
        {
            _sessionManager = sessionManager;
            _roomSync = roomSync;
        }

        public bool RegisterSession(IClientSession session)
        {
            if (!_sessionManager.Add(session))
            {
                session.Dispose();
                return false;
            }

            NetworkView.Write(NetworkMessageType.Message,
                $"Client connected: {session.ConnectionInfo}", session.Nickname);
            return true;
        }

        public bool UnregisterSession(IClientSession session)
        {
            // Room 생성과 Session 제거를 기존 공유 잠금으로 보호합니다.
            lock (_roomSync)
                return _sessionManager.Remove(session.ClientId, out _);
        }

        public void CloseConnection(IClientSession session)
        {
            session.Dispose();
        }

        public Task[] ClearSessions()
        {
            var sessions = _sessionManager.GetSessionsSnapshot();
            var processingTasks = sessions.Where(session => session.ProcessingTask is not null)
                .Select(session => session.ProcessingTask!).ToArray();
            foreach (var session in sessions)
                session.Dispose();
            lock (_roomSync)
                _sessionManager.Clear();
            return processingTasks;
        }
    }
}
