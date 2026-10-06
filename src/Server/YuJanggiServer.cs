using System.Net;

namespace YuJanggi.Server
{
    using Core.Sessions;

    using Protocol.Messages;

    using Transport.Tcp;
    using View;
    using Connection;


    using Features.Login;
    using Features.Lobby;
    using Features.Game;

    /// <summary>
    /// 유장기 서버의 실행 및 클라이언트 연결 수락을 관리합니다.
    /// </summary>
    internal sealed class YuJanggiServer
    {
        #region Constants

        private const int Port = 7777;
        private static readonly IPAddress Address = IPAddress.Any;

        #endregion

        #region Fields
        private readonly TcpConnectionListener  _listener;
        private readonly ClientSessionManager   _sessionManager;
        private readonly ConnectionService _connectionService;
        private readonly LoginService _loginService;
        private readonly GameRoomManager _gameRoomManager;
        private readonly LobbyService _lobbyService;
        private readonly GameService _gameService;
        private readonly Lock _roomSync = new();

        private readonly ProtocolHandshakeHandler _handshakeHandler;
        private readonly LobbyHandler _lobbyHandler;
        private readonly GameHandler _gameHandler;

        #endregion

        #region Constructors

        public YuJanggiServer()
        {
            _listener =
                new TcpConnectionListener(
                    new IPEndPoint(Address, Port));

            _sessionManager =
                new ClientSessionManager();

            _gameRoomManager = new GameRoomManager(_sessionManager, _roomSync);
            _connectionService = new ConnectionService(_sessionManager, _roomSync);
            _lobbyService = new LobbyService(new LobbyManager(), _gameRoomManager);
            _gameService = new GameService(_gameRoomManager);
            _loginService = new LoginService(new LoginManager());
            _handshakeHandler = new ProtocolHandshakeHandler(_loginService);
            _lobbyHandler = new LobbyHandler(_lobbyService);
            _gameHandler = new GameHandler(_gameService);
        }

        #endregion

        #region Public Methods

        public void ShowRooms()
        {
            var snapshots = new List<GameRoomSnapshot>();
            foreach (var room in _gameRoomManager.GetRoomsSnapshot())
            {
                lock (room.SyncRoot)
                {
                    var cho = room.ChoPlayer;
                    var han = room.HanPlayer;
                    snapshots.Add(new GameRoomSnapshot(
                        room.MatchId,
                        room.State,
                        cho is null ? null : new GameRoomPlayerSnapshot(
                            cho.ClientId, cho.Nickname, cho.ConnectionInfo, room.ChoReady),
                        han is null ? null : new GameRoomPlayerSnapshot(
                            han.ClientId, han.Nickname, han.ConnectionInfo, room.HanReady)));
                }
            }

            NetworkView.ShowRooms(snapshots);
        }

        public async Task RunAsync(
            CancellationToken cancellationToken = default)
        {
            using var shutdown = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            cancellationToken = shutdown.Token;
            _listener.Start();

            NetworkView.Write(
                NetworkMessageType.Message,
                "YuJanggi Server started.");
            NetworkView.LogPackageVersions();

            try
            {
                while (!cancellationToken.IsCancellationRequested)
                {
                    TcpClientConnection connection =
                        await _listener.AcceptAsync(
                            cancellationToken);

                    var session =
                        ClientSessionFactory.CreateClientSession(
                            connection);

                    if (!_connectionService.RegisterSession(session))
                        continue;

                    Task processingTask =
                        HandleClientAsync(
                            session,
                            cancellationToken);

                    session.AttachProcessingTask(
                        processingTask);
                }
            }
            finally
            {
                // 수락 루프가 오류로 끝나도 송수신 대기를 먼저 취소한 뒤 세션을 정리합니다.
                shutdown.Cancel();
                _listener.Stop();

                Task[] processingTasks = _connectionService.ClearSessions();
                try
                {
                    await Task.WhenAll(processingTasks);
                }
                finally
                {
                    _loginService.Clear();
                    await _lobbyService.ClearAsync();
                    await _gameService.ClearAsync();
                }
            }
        }

        #endregion

        #region Private Methods

        private async Task HandleClientAsync(
            IClientSession session,
            CancellationToken cancellationToken)
        {
            try
            {
                while (!cancellationToken.IsCancellationRequested)
                {
                    ClientMessage message =
                        await session.ReceiveAsync(
                            cancellationToken);

                    switch (message.Type)
                    {
                        case ClientMessageType.HandshakeRequest:
                            await _handshakeHandler.HandleAsync(
                                session, message, cancellationToken);
                            break;

                        case ClientMessageType.MatchingStartRequest:
                        case ClientMessageType.MatchingCancelRequest:
                        case ClientMessageType.FormationSubmit:
                            await _lobbyHandler.HandleAsync(
                                session, message, cancellationToken);
                            break;

                        case ClientMessageType.GameSceneReady:
                        case ClientMessageType.MovePieceRequest:
                        case ClientMessageType.GameEndRequest:
                            await _gameHandler.HandleAsync(
                                session, message, cancellationToken);
                            break;

                        default:
                            throw new InvalidOperationException(
                                $"처리할 수 없는 메시지입니다: {message.Type}");
                    }
                }
            }
            catch (OperationCanceledException)
                when (cancellationToken.IsCancellationRequested)
            {
                // 서버 종료
            }
            catch (EndOfStreamException)
            {
                // 클라이언트 연결 종료
            }
            catch (Exception exception)
            {
                NetworkView.Write(
                    NetworkMessageType.Error,
                    exception.ToString(),
                    session.Nickname);
            }
            finally
            {
                await DisconnectClient(session);
            }
        }

        private async Task DisconnectClient(
            IClientSession session)
        {
            // 서버 종료 시 세션 목록이 먼저 비워졌더라도 대기열은 반드시 정리합니다.

            bool removed = _connectionService.UnregisterSession(session);
            _loginService.DisconnectPlayer(session);

            // 서버 잠금을 해제한 뒤 매칭 상태와 게임룸을 각각 정리합니다.
            await _lobbyService.DisconnectPlayerAsync(session);
            Task roomCleanup = _gameService.DisconnectPlayerAsync(session);

            if (!removed)
            {
                await roomCleanup;
                return;
            }

            _connectionService.CloseConnection(session);

            await roomCleanup;

            NetworkView.Write(
                NetworkMessageType.Message,
                $"Client disconnected: {session.ConnectionInfo}",
                session.Nickname);
        }

        #endregion
    }
}
