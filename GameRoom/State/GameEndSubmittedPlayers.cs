namespace YuJanggi.Server.V2.GameRoom.State
{
    [Flags]
    internal enum GameEndSubmittedPlayers
    {
        None = 0,
        Cho = 1 << 0,
        Han = 1 << 1,
        All = Cho | Han
    }
}
