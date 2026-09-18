using System.Net.Sockets;
using System.Text.Json;
using YuJanggiCommon;

namespace YuJanggi.Server.Hosting;

using Controllers;
using Game;
using Models;
using Transport;
using Views;
public sealed class ServerHost
{
    private readonly IConnectionListener _listener;
    private readonly ServerModel _model;
    private readonly MessageDispatcher _dispatcher;
    private readonly IServerView _view;
    private readonly CancellationTokenSource _shutdown = new();
    private readonly Dictionary<PlayerSession, Task> _clientTasks = new();
    private readonly Lock _clientTasksLock = new();
    private readonly Lock _clearLock = new();
    private int _isRunning;

    public ServerHost(int port) : this(port, Random.Shared)
    {
    }

    internal ServerHost(int port, Random matchmakingRandom)
    {
        var model = new ServerModel(new GameSessionFactory(), matchmakingRandom);
        var view = new ServerView();
        _listener = new TcpConnectionListener(port);
        _model = model;
        _view = view;
        _dispatcher = new MessageDispatcher(
            new IMessageController[]
            {
                new JoinController(model, view),
                new MatchmakingController(model, view),
                new FormationController(model, view),
                new GameController(model, view),
                new GameChatController(model, view)
            },
            view);
    }

    public ServerHost(
        IConnectionListener listener,
        ServerModel model,
        MessageDispatcher dispatcher,
        IServerView view)
    {
        _listener = listener;
        _model = model;
        _dispatcher = dispatcher;
        _view = view;
    }

    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
        if (Interlocked.Exchange(ref _isRunning, 1) != 0)
            throw new InvalidOperationException("서버가 이미 실행 중입니다.");

        using CancellationTokenSource linkedCancellation =
            CancellationTokenSource.CreateLinkedTokenSource(
                _shutdown.Token,
                cancellationToken);
        CancellationToken token = linkedCancellation.Token;
        _listener.Start();

        try
        {
            while (!token.IsCancellationRequested)
            {
                IClientConnection connection = await _listener.AcceptAsync(token);
                PlayerSession player = new(connection);

                if (!_model.TryRegister(player))
                {
                    player.Dispose();
                    continue;
                }

                Console.WriteLine($"[Connect] {player.ClientInfo}");
                Track(player, HandleClientAsync(player, token));
            }
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            // 정상 종료 요청으로 접속 대기가 취소되었습니다.
        }
        catch (SocketException) when (token.IsCancellationRequested)
        {
            // 리스너 종료로 접속 대기가 중단되었습니다.
        }
        catch (ObjectDisposedException) when (token.IsCancellationRequested)
        {
            // 리스너 종료 과정에서 소켓이 정리되었습니다.
        }
        finally
        {
            Interlocked.Exchange(ref _isRunning, 0);
            _listener.Stop();
            ClearClients();
        }
    }

    public void PrintClientList()
    {
        IReadOnlyList<string> clients = _model.GetClientInfoSnapshot();
        Console.WriteLine($"[ClientList] 접속 인원: {clients.Count}");

        foreach (string client in clients)
            Console.WriteLine(client);
    }

    public void ClearClients()
    {
        lock (_clearLock)
        {
            ClearModelResult clear = _model.BeginClear();

            try
            {
                foreach (GameSession game in clear.Games)
                    game.ClearPlayers();
                foreach (PlayerSession player in clear.Players)
                    player.Dispose();

                if (clear.Players.Count > 0)
                {
                    Console.WriteLine(
                        $"[ClientClear] 클라이언트 {clear.Players.Count}명의 연결을 종료했습니다.");
                }
            }
            finally
            {
                _model.EndClear();
            }
        }
    }

    public void Stop()
    {
        _shutdown.Cancel();
        _listener.Stop();
        ClearClients();
    }

    public async Task StopAsync()
    {
        Stop();
        Task[] tasks;

        lock (_clientTasksLock)
        {
            tasks = _clientTasks.Values.ToArray();
        }

        await Task.WhenAll(tasks);
    }

    private async Task HandleClientAsync(
        PlayerSession player,
        CancellationToken cancellationToken)
    {
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                ChatMessage message = await player.Connection.ReceiveAsync(cancellationToken);
                Console.WriteLine(
                    $"[Receive] {player.ClientInfo} | Type={message.Type}");
                await _dispatcher.DispatchAsync(player, message, cancellationToken);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // 서버 종료 과정에서 연결 처리가 취소되었습니다.
        }
        catch (Exception exception) when (
            exception is IOException or ObjectDisposedException or SocketException)
        {
            // 연결 종료 또는 통신 오류입니다.
        }
        catch (Exception exception) when (
            exception is InvalidDataException or JsonException)
        {
            Console.WriteLine(
                $"[InvalidMessage] {player.ClientInfo} | {exception.Message}");
        }
        catch (Exception exception)
        {
            Console.WriteLine(
                $"[ClientError] {player.ClientInfo} | {exception}");
        }
        finally
        {
            DisconnectModelResult disconnected = _model.Remove(player);
            player.Dispose();
            Console.WriteLine($"[Disconnect] {player.ClientInfo}");

            if (disconnected.Opponent is not null &&
                disconnected.EndedGameId is Guid gameId &&
                !_shutdown.IsCancellationRequested)
            {
                try
                {
                    await _view.SendOpponentLeftAsync(
                        disconnected.Opponent,
                        gameId,
                        CancellationToken.None);
                }
                catch (Exception exception) when (
                    exception is IOException or ObjectDisposedException or SocketException)
                {
                    // 상대 클라이언트도 이미 연결을 종료했습니다.
                }
            }
        }
    }

    private void Track(PlayerSession player, Task task)
    {
        lock (_clientTasksLock)
        {
            _clientTasks[player] = task;
        }

        _ = task.ContinueWith(
            _ =>
            {
                lock (_clientTasksLock)
                {
                    _clientTasks.Remove(player);
                }
            },
            CancellationToken.None,
            TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);
    }
}
