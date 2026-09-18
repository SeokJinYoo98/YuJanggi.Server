using YuJanggiCommon;

namespace YuJanggi.Server.Game;

public sealed record EngineMoveResult(
    BoardPosition From,
    BoardPosition To,
    PlayerSide MovedBy,
    PlayerSide CurrentTurn,
    IReadOnlyList<BoardPieceState> Pieces);

public interface IJanggiGameEngine
{
    PlayerSide CurrentTurn { get; }
    void Initialize(GameFormation choFormation, GameFormation hanFormation);
    void Start();
    ErrorCode? TryGetLegalMoves(PlayerSide player, BoardPosition from, out LegalMovesResult? result);
    ErrorCode? TryMove(PlayerSide player, MoveRequest request, out EngineMoveResult? result);
    IReadOnlyList<BoardPieceState> CreateSnapshot();
    void Close();
}
