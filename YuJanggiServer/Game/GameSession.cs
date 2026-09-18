using YuJanggiCommon;

namespace YuJanggi.Server.Game;

using Models;

public sealed class GameSession
{
    public Guid GameId { get; }
    public PlayerSession ChoPlayer { get; }
    public PlayerSession HanPlayer { get; }

    private readonly IJanggiGameEngine _engine;
    private readonly Lock _gameLock = new();
    private GameFormation? _choFormation;
    private GameFormation? _hanFormation;
    private bool _matchAnnounced;
    private bool _started;
    private bool _closed;

    public GameSession(
        Guid gameId,
        PlayerSession choPlayer,
        PlayerSession hanPlayer,
        IJanggiGameEngine engine,
        bool choSelectsFormation = false,
        bool hanSelectsFormation = false)
    {
        GameId = gameId;
        ChoPlayer = choPlayer;
        HanPlayer = hanPlayer;
        _engine = engine;
        _choFormation = choSelectsFormation ? null : GameFormation.EHHE;
        _hanFormation = hanSelectsFormation ? null : GameFormation.EHHE;
    }

    public bool AnnounceMatch()
    {
        lock (_gameLock)
        {
            _matchAnnounced = true;
            return TryStart();
        }
    }

    public ErrorCode? TrySelectFormation(
        PlayerSession session,
        SelectFormationRequest request,
        out bool started)
    {
        lock (_gameLock)
        {
            started = false;
            if (_closed || !Contains(session) || request.GameId != GameId)
                return ErrorCode.GameSessionNotFound;
            if (_started)
                return ErrorCode.GameAlreadyStarted;
            if (!Enum.IsDefined(typeof(GameFormation), request.Formation))
                return ErrorCode.InvalidFormation;

            if (ReferenceEquals(session, ChoPlayer))
            {
                if (_choFormation.HasValue)
                    return ErrorCode.FormationAlreadySelected;
                _choFormation = request.Formation;
            }
            else
            {
                if (_hanFormation.HasValue)
                    return ErrorCode.FormationAlreadySelected;
                _hanFormation = request.Formation;
            }

            started = TryStart();
            return null;
        }
    }

    public GameStartEvent CreateGameStart(PlayerSide side)
    {
        lock (_gameLock)
        {
            if (!_started)
                throw new InvalidOperationException("포진 선택이 완료되지 않았습니다.");

            return new GameStartEvent(
                GameId,
                side,
                _engine.CurrentTurn,
                _engine.CreateSnapshot())
            {
                ChoFormation = _choFormation!.Value,
                HanFormation = _hanFormation!.Value
            };
        }
    }

    public ErrorCode? TryGetLegalMoves(
        PlayerSession session,
        BoardPosition from,
        out LegalMovesResult? result)
    {
        lock (_gameLock)
        {
            result = null;
            ErrorCode? sessionError = ValidateSession(session);
            if (sessionError.HasValue)
                return sessionError;

            return _engine.TryGetLegalMoves(session.Side!.Value, from, out result);
        }
    }

    public ErrorCode? TryMove(
        PlayerSession session,
        MoveRequest request,
        out MoveResultEvent? result)
    {
        lock (_gameLock)
        {
            result = null;
            ErrorCode? sessionError = ValidateSession(session);
            if (sessionError.HasValue)
                return sessionError;

            ErrorCode? moveError = _engine.TryMove(
                session.Side!.Value,
                request,
                out EngineMoveResult? move);

            if (moveError.HasValue || move is null)
                return moveError ?? ErrorCode.InvalidRequest;

            result = new MoveResultEvent(
                GameId,
                move.From,
                move.To,
                move.MovedBy,
                move.CurrentTurn,
                move.Pieces);
            return null;
        }
    }

    public bool Contains(PlayerSession session)
    {
        return ReferenceEquals(ChoPlayer, session) || ReferenceEquals(HanPlayer, session);
    }

    public PlayerSession GetOpponent(PlayerSession session)
    {
        if (ReferenceEquals(ChoPlayer, session))
            return HanPlayer;
        if (ReferenceEquals(HanPlayer, session))
            return ChoPlayer;

        throw new InvalidOperationException("게임에 참가하지 않은 세션입니다.");
    }

    public void ClearPlayers()
    {
        lock (_gameLock)
        {
            if (_closed)
                return;

            _closed = true;
            _engine.Close();
            ChoPlayer.ClearMatch();
            HanPlayer.ClearMatch();
        }
    }

    private bool TryStart()
    {
        if (_closed || _started || !_matchAnnounced ||
            !_choFormation.HasValue || !_hanFormation.HasValue)
            return false;

        _engine.Initialize(_choFormation.Value, _hanFormation.Value);
        _engine.Start();
        _started = true;
        return true;
    }

    private ErrorCode? ValidateSession(PlayerSession session)
    {
        if (!Contains(session) || session.Side is null)
            return ErrorCode.GameSessionNotFound;
        if (_closed)
            return ErrorCode.GameSessionNotFound;
        if (!_started)
            return ErrorCode.GameNotStarted;

        return null;
    }
}
