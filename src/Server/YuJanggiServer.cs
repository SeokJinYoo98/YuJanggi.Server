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
    using Features.Game.State;

    /// <summary>
    /// 유장기 서버의 실행 및 클라이언트 연결 수락을 관리합니다.
    /// </summary>
    internal sealed class YuJanggiServer
    {
        #region Constants

        private const int Port = 7777;
        private static readonly TimeSpan GameTickInterval = TimeSpan.FromMilliseconds(50);
        private static readonly IPAddress Address = IPAddress.Any;

        #endregion

        #region Fields
        private readonly TcpConnectionListener  _listener;
        private readonly ClientSessionManager   _sessionManager;
        private readonly ConnectionService _connectionService;
        private readonly LoginService _loginService;
        private readonly GameRoomManager _gameRoomManager;

        private readonly Lock _roomSync = new();

        private readonly ProtocolHandshakeHandler _handshakeHandler;
        private readonly LobbyHandler _lobbyHandler;
        private readonly LobbyService _lobbyService;
        private readonly GameHandler _gameHandler;
        private readonly GameService _gameService;
        private readonly ChatService _chatService;
        private readonly ChatHandler _chatHandler;
        #endregion

        #region Constructors

        public YuJanggiServer()
        {
            _listener =
                new TcpConnectionListener(
                    new IPEndPoint(Address, Port));

            _sessionManager =
                new ClientSessionManager();

            _gameRoomManager    = new (_sessionManager, _roomSync);
            _connectionService  = new (_sessionManager, _roomSync);
            _loginService       = new (new());
            _handshakeHandler   = new (_loginService);

            _lobbyService       = new (new(), _gameRoomManager);
            _lobbyHandler       = new (_lobbyService);

            _gameService        = new (_gameRoomManager);
            _gameHandler        = new (_gameService);

            _chatService        = new (_gameRoomManager);
            _chatHandler        = new(_chatService);

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

            Task gameLoopTask = RunGameLoopAsync(shutdown);
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
                    await Task.WhenAll(processingTasks.Append(gameLoopTask));
                }
                finally
                {
                    _chatService.Clear();
                    _loginService.Clear();
                    await _lobbyService.ClearAsync();
                    await _gameService.ClearAsync();
                  
                }
            }
        }

        #endregion

        #region Private Methods

        private async Task RunGameLoopAsync(CancellationTokenSource shutdown)
        {
            var token = shutdown.Token;
            using var timer = new PeriodicTimer(GameTickInterval);
            var pendingTicks = new Dictionary<GameRoom, Task>();
            try
            {
                while (await timer.WaitForNextTickAsync(token))
                {
                    foreach (var completed in pendingTicks.Where(entry => entry.Value.IsCompleted).ToArray())
                    {
                        await completed.Value;
                        pendingTicks.Remove(completed.Key);
                    }

                    // 느린 룸의 송신이 다른 룸의 시간 진행을 막지 않도록 합니다.
                    // 룸마다 아직 끝나지 않은 Tick 작업은 하나만 유지합니다.
                    foreach (var room in _gameRoomManager.GetRoomsSnapshot())
                    {
                        if (room.State != GameRoomState.Playing || pendingTicks.ContainsKey(room))
                            continue;

                        pendingTicks.Add(room, TickRoomAsync(room, token));
                    }
                }
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested)
            {
                // 서버 종료 시 Tick과 전송 대기도 함께 종료합니다.
            }
            catch
            {
                // 시간 진행 루프가 중단된 상태로 연결 수락만 계속하지 않습니다.
                shutdown.Cancel();
                throw;
            }
            finally
            {
                try
                {
                    await Task.WhenAll(pendingTicks.Values);
                }
                catch (OperationCanceledException) when (token.IsCancellationRequested)
                {
                    // 이미 예약한 Tick·송신까지 완료된 후 서버 정리로 돌아갑니다.
                }
            }
        }

        private async Task TickRoomAsync(GameRoom room, CancellationToken token)
        {
            try
            {
                await _gameHandler.TickAsync(room, token);
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception)
            {
                NetworkView.Write(NetworkMessageType.Error, exception.ToString(), room.MatchId);
            }
        }

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
                    int typeNum = (int)message.Type;

                    if (100 <= typeNum && typeNum <= 199)
                        await _handshakeHandler.HandleAsync(
                                session, message, cancellationToken);

                    else if (200 <= typeNum && typeNum <= 299)
                        await _lobbyHandler.HandleAsync(
                            session, message, cancellationToken);

                    else if (300 <= typeNum && typeNum <= 399)
                        await _gameHandler.HandleAsync(
                            session, message, cancellationToken);

                    else if (400 <= typeNum && typeNum <= 499)
                        await _chatHandler.HandleAsync(
                            session, message, cancellationToken);
                    else
                        throw new InvalidOperationException(
                                $"처리할 수 없는 메시지입니다: {message.Type}");               
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
            _chatService.DisconnectPlayer(session);

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
