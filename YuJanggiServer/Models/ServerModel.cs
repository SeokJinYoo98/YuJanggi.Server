using YuJanggiCommon;
namespace YuJanggi.Server.Models;

using Game;

public sealed record JoinModelResult(ErrorCode? Error, Guid PlayerId);

public sealed record MatchmakingEntry(
    PlayerSession Player,
    string? RequestId,
    bool SelectFormation);

public sealed record MatchedGame(
    MatchmakingEntry Cho,
    MatchmakingEntry Han,
    GameSession Game);

public sealed record MatchmakingModelResult(
    ErrorCode? Error,
    bool IsWaiting,
    MatchedGame? Match);

public sealed record DisconnectModelResult(
    PlayerSession? Opponent,
    Guid? EndedGameId);

public sealed record ClearModelResult(
    IReadOnlyList<PlayerSession> Players,
    IReadOnlyList<GameSession> Games);

public interface IPlayerRegistry
{
    JoinModelResult Join(PlayerSession player, string playerName);
}

public interface IMatchmakingService
{
    MatchmakingModelResult Start(
        PlayerSession player,
        string? requestId,
        bool selectFormation);

    bool Cancel(PlayerSession player);
}

public interface IGameSessionRegistry
{
    ErrorCode? TryGet(PlayerSession player, out GameSession? gameSession);
}

public sealed class ServerModel :
    IPlayerRegistry,
    IMatchmakingService,
    IGameSessionRegistry
{
    private readonly List<PlayerSession> _players = new();
    private readonly List<MatchmakingEntry> _matchmakingQueue = new();
    private readonly Dictionary<Guid, GameSession> _gameSessions = new();
    private readonly Lock _sync = new();
    private readonly IGameSessionFactory _gameSessionFactory;
    private readonly Random _matchmakingRandom;
    private bool _isClearing;

    public ServerModel(
        IGameSessionFactory gameSessionFactory,
        Random matchmakingRandom)
    {
        _gameSessionFactory = gameSessionFactory;
        _matchmakingRandom = matchmakingRandom;
    }

    public bool TryRegister(PlayerSession player)
    {
        lock (_sync)
        {
            if (_isClearing)
                return false;

            _players.Add(player);
            return true;
        }
    }

    public IReadOnlyList<string> GetClientInfoSnapshot()
    {
        lock (_sync)
        {
            return _players.Select(player => player.ClientInfo).ToList();
        }
    }

    public JoinModelResult Join(PlayerSession player, string playerName)
    {
        lock (_sync)
        {
            if (_isClearing || !_players.Contains(player))
                return new JoinModelResult(ErrorCode.InvalidRequest, default);
            if (player.IsJoined)
                return new JoinModelResult(ErrorCode.AlreadyJoined, default);

            if (_players.Any(other =>
                !ReferenceEquals(other, player) &&
                string.Equals(other.PlayerName, playerName, StringComparison.OrdinalIgnoreCase)))
            {
                return new JoinModelResult(ErrorCode.DuplicatePlayerName, default);
            }

            if (!player.TryJoin(playerName, out Guid playerId))
                return new JoinModelResult(ErrorCode.AlreadyJoined, default);

            return new JoinModelResult(null, playerId);
        }
    }

    public MatchmakingModelResult Start(
        PlayerSession player,
        string? requestId,
        bool selectFormation)
    {
        lock (_sync)
        {
            if (_isClearing || !_players.Contains(player))
                return Error(ErrorCode.NotJoined);
            if (!player.IsJoined)
                return Error(ErrorCode.NotJoined);
            if (player.IsMatched)
                return Error(ErrorCode.AlreadyMatched);
            if (_matchmakingQueue.Any(entry => ReferenceEquals(entry.Player, player)))
                return Error(ErrorCode.AlreadyMatchmaking);

            _matchmakingQueue.Add(new MatchmakingEntry(
                player,
                requestId,
                selectFormation));

            if (_matchmakingQueue.Count < 2)
                return new MatchmakingModelResult(null, true, null);

            MatchmakingEntry cho = _matchmakingQueue[0];
            MatchmakingEntry han = _matchmakingQueue[1];
            _matchmakingQueue.RemoveRange(0, 2);

            if (_matchmakingRandom.Next(2) == 1)
                (cho, han) = (han, cho);

            Guid gameId = Guid.NewGuid();
            cho.Player.SetMatch(gameId, PlayerSide.Cho);
            han.Player.SetMatch(gameId, PlayerSide.Han);

            GameSession game = _gameSessionFactory.Create(
                gameId,
                cho.Player,
                han.Player,
                cho.SelectFormation,
                han.SelectFormation);
            _gameSessions.Add(gameId, game);

            return new MatchmakingModelResult(
                null,
                false,
                new MatchedGame(cho, han, game));
        }
    }

    public bool Cancel(PlayerSession player)
    {
        lock (_sync)
        {
            if (_isClearing)
                return false;

            int index = _matchmakingQueue.FindIndex(entry =>
                ReferenceEquals(entry.Player, player));

            if (index < 0)
                return false;

            _matchmakingQueue.RemoveAt(index);
            return true;
        }
    }

    public ErrorCode? TryGet(
        PlayerSession player,
        out GameSession? gameSession)
    {
        lock (_sync)
        {
            if (_isClearing || !_players.Contains(player))
            {
                gameSession = null;
                return ErrorCode.GameSessionNotFound;
            }

            if (player.GameId is not Guid gameId)
            {
                gameSession = null;
                return ErrorCode.NotMatched;
            }

            if (!_gameSessions.TryGetValue(gameId, out gameSession) ||
                !gameSession.Contains(player))
            {
                gameSession = null;
                return ErrorCode.GameSessionNotFound;
            }

            return null;
        }
    }

    public DisconnectModelResult Remove(PlayerSession player)
    {
        lock (_sync)
        {
            _players.Remove(player);
            _matchmakingQueue.RemoveAll(entry =>
                ReferenceEquals(entry.Player, player));

            if (player.GameId is not Guid gameId ||
                !_gameSessions.Remove(gameId, out GameSession? game))
            {
                return new DisconnectModelResult(null, null);
            }

            PlayerSession opponent = game.GetOpponent(player);
            game.ClearPlayers();
            return new DisconnectModelResult(opponent, gameId);
        }
    }

    public ClearModelResult BeginClear()
    {
        lock (_sync)
        {
            _isClearing = true;
            List<PlayerSession> players = _players.ToList();
            List<GameSession> games = _gameSessions.Values.ToList();
            _players.Clear();
            _matchmakingQueue.Clear();
            _gameSessions.Clear();
            return new ClearModelResult(players, games);
        }
    }

    public void EndClear()
    {
        lock (_sync)
        {
            _isClearing = false;
        }
    }

    private static MatchmakingModelResult Error(ErrorCode code)
    {
        return new MatchmakingModelResult(code, false, null);
    }
}
