namespace YuJanggi.Server.Lobby
{
    using Engine.Domain;
    using ClientSession;
    using Core.Sessions;

    /// <summary>한 매치의 예약·확정·포진 상태를 소유합니다. LobbyManager.SyncRoot 안에서 사용합니다.</summary>
    internal sealed class MatchState
    {
        public MatchState(MatchPair players) => Players = players;
        public MatchPair Players { get; }
        public string? MatchId { get; private set; }
        public Formation? ChoFormation { get; private set; }
        public Formation? HanFormation { get; private set; }
        // GameRoom의 현재 상태가 아닌 Lobby → Game 전환 성공 기록입니다.
        public bool GameTransitionCompleted { get; private set; }
        public bool AllFormationsSubmitted => ChoFormation.HasValue && HanFormation.HasValue;

        public void Confirm(string matchId)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(matchId);
            if (MatchId is not null)
                throw new InvalidOperationException("이미 확정된 매치입니다.");
            MatchId = matchId;
        }

        public void SetFormation(IClientSession session, Formation? formation)
        {
            if (MatchId is null || GameTransitionCompleted)
                throw new InvalidOperationException("포진을 변경할 수 없는 매치입니다.");
            if (session.ClientId == Players.First.ClientId)
                ChoFormation = formation;
            else if (session.ClientId == Players.Second.ClientId)
                HanFormation = formation;
            else
                throw new InvalidOperationException("매치 참가자가 아닙니다.");
        }

        public void MarkGameTransitionCompleted()
        {
            if (MatchId is null || !AllFormationsSubmitted || GameTransitionCompleted)
                throw new InvalidOperationException("게임룸 생성 상태를 반영할 수 없습니다.");
            GameTransitionCompleted = true;
        }
    }
}
