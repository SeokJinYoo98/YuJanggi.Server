namespace YuJanggi.Server.Game;

using Models;

public interface IGameSessionFactory
{
    GameSession Create(Guid gameId, PlayerSession choPlayer, PlayerSession hanPlayer,
        bool choSelectsFormation, bool hanSelectsFormation);
}

public sealed class GameSessionFactory : IGameSessionFactory
{
    private readonly Func<IJanggiGameEngine> _engineFactory;

    public GameSessionFactory() : this(() => new JanggiCoreEngine())
    {
    }

    public GameSessionFactory(Func<IJanggiGameEngine> engineFactory)
    {
        _engineFactory = engineFactory;
    }

    public GameSession Create(Guid gameId, PlayerSession choPlayer, PlayerSession hanPlayer,
        bool choSelectsFormation, bool hanSelectsFormation)
    {
        return new GameSession(gameId, choPlayer, hanPlayer, _engineFactory(),
            choSelectsFormation, hanSelectsFormation);
    }
}
