
namespace YuJanggi.Server.GameRoom.State
{
    internal enum GameRoomState
    {
        WaitingForReady,
        Playing,
        Ended,
        Closed
    }

    [Flags]
    internal enum ReadyPlayers
    {
        None = 0, 
        Cho = 1 << 0, 
        Han = 1 << 1,
        All = Cho | Han
    }
}
