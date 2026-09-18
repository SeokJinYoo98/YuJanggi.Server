using YuJanggiCommon;

namespace YuJanggi.Server.Game;

using Core.Board;
using Core.Domain;
using Core.Match;
using Core.Rule;

using CorePieceType = Core.Domain.PieceType;
using CorePlayerTeam = Core.Domain.PlayerTeam;

public sealed class JanggiCoreEngine : IJanggiGameEngine
{
    private readonly MatchModel _match;

    public PlayerSide CurrentTurn => ToPlayerSide(_match.PlayerTurn);

    public JanggiCoreEngine()
        : this(new MatchModel(
            new Turn(0),
            new Record(),
            new Score(),
            new BoardModel(),
            new JanggiRule()))
    {
    }

    internal JanggiCoreEngine(MatchModel match)
    {
        _match = match;
    }

    public void Initialize(GameFormation choFormation, GameFormation hanFormation)
    {
        _match.InitGame(ToCoreFormation(choFormation), ToCoreFormation(hanFormation));
    }

    public void Start()
    {
        _match.BindEvents();
        _match.StartGame();
    }

    public ErrorCode? TryGetLegalMoves(
        PlayerSide player,
        BoardPosition from,
        out LegalMovesResult? result)
    {
        result = null;
        Pos fromPosition = new(from.X, from.Z);
        ErrorCode? validationError = ValidateMoveSource(player, fromPosition);

        if (validationError.HasValue)
            return validationError;

        Selection selection = new() { FromPos = fromPosition };
        _match.Rule.FindWays(_match.Board, selection);
        result = new LegalMovesResult(
            from,
            selection.LegalCells
                .Select(position => new BoardPosition(position.X, position.Z))
                .ToList());
        return null;
    }

    public ErrorCode? TryMove(
        PlayerSide player,
        MoveRequest request,
        out EngineMoveResult? result)
    {
        result = null;
        Pos from = new(request.From.X, request.From.Z);
        Pos to = new(request.To.X, request.To.Z);
        ErrorCode? validationError = ValidateMoveSource(player, from);

        if (validationError.HasValue)
            return validationError;
        if (!_match.Board.IsInside(to))
            return ErrorCode.InvalidPosition;
        if (!_match.TryMove(from, to))
            return ErrorCode.IllegalMove;

        result = new EngineMoveResult(
            request.From,
            request.To,
            player,
            CurrentTurn,
            CreateSnapshot());
        return null;
    }

    public IReadOnlyList<BoardPieceState> CreateSnapshot()
    {
        List<BoardPieceState> pieces = new();

        for (int x = 0; x < _match.Board.WIDTH; x++)
        {
            for (int z = 0; z < _match.Board.HEIGHT; z++)
            {
                Pos position = new(x, z);

                if (!_match.Board.HasPiece(position))
                    continue;

                PieceModel piece = _match.Board.GetPiece(position);
                pieces.Add(new BoardPieceState(
                    piece.Id,
                    x,
                    z,
                    ToPlayerSide(piece.Team),
                    ToGamePieceType(piece.Type)));
            }
        }

        return pieces;
    }

    public void Close() => _match.UnBindEvents();

    private ErrorCode? ValidateMoveSource(PlayerSide player, Pos from)
    {
        CorePlayerTeam playerTeam = ToCorePlayerTeam(player);

        if (_match.PlayerTurn != playerTeam)
            return ErrorCode.NotYourTurn;
        if (!_match.Board.IsInside(from))
            return ErrorCode.InvalidPosition;
        if (!_match.Board.HasPiece(from))
            return ErrorCode.PieceNotFound;
        if (_match.Board.GetPiece(from).Team != playerTeam)
            return ErrorCode.NotYourPiece;

        return null;
    }

    private static Formation ToCoreFormation(GameFormation formation)
    {
        return formation switch
        {
            GameFormation.HEHE => Formation.HEHE,
            GameFormation.EHEH => Formation.EHEH,
            GameFormation.EHHE => Formation.EHHE,
            GameFormation.HEEH => Formation.HEEH,
            _ => throw new ArgumentOutOfRangeException(
                nameof(formation), formation, "지원하지 않는 포진입니다.")
        };
    }

    private static PlayerSide ToPlayerSide(CorePlayerTeam team)
    {
        return team switch
        {
            CorePlayerTeam.Cho => PlayerSide.Cho,
            CorePlayerTeam.Han => PlayerSide.Han,
            _ => throw new InvalidOperationException($"지원하지 않는 진영입니다: {team}")
        };
    }

    private static CorePlayerTeam ToCorePlayerTeam(PlayerSide side)
    {
        return side switch
        {
            PlayerSide.Cho => CorePlayerTeam.Cho,
            PlayerSide.Han => CorePlayerTeam.Han,
            _ => throw new InvalidOperationException($"지원하지 않는 진영입니다: {side}")
        };
    }

    private static GamePieceType ToGamePieceType(CorePieceType pieceType)
    {
        return pieceType switch
        {
            CorePieceType.King => GamePieceType.King,
            CorePieceType.Chariot => GamePieceType.Chariot,
            CorePieceType.Cannon => GamePieceType.Cannon,
            CorePieceType.Horse => GamePieceType.Horse,
            CorePieceType.Elephant => GamePieceType.Elephant,
            CorePieceType.Guard => GamePieceType.Guard,
            CorePieceType.Soldier => GamePieceType.Soldier,
            _ => throw new InvalidOperationException($"지원하지 않는 기물입니다: {pieceType}")
        };
    }
}
