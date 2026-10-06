using System.Diagnostics.CodeAnalysis;

namespace YuJanggi.Server.Lobby
{
    using ClientSession;
    using Core.Sessions;

    /// <summary>매칭 대기열과 참가자별 매치 인덱스를 관리합니다.</summary>
    internal sealed class LobbyManager
    {
        private readonly Lock _sync = new();
        private readonly MatchMakingQueue _queue = new();
        private readonly Dictionary<Guid, MatchState> _playerMatches = new();
        internal Lock SyncRoot => _sync;

        public bool ContainsQueued(IClientSession session)
        {
            lock (_sync) return _queue.Contains(session);
        }

        public void Enqueue(IClientSession session)
        {
            lock (_sync) _queue.Enqueue(session);
        }

        public void RemoveQueued(IClientSession session)
        {
            lock (_sync) _queue.Remove(session);
        }

        public bool TryGetMatch(Guid clientId, [NotNullWhen(true)] out MatchState? state)
        {
            lock (_sync) return _playerMatches.TryGetValue(clientId, out state);
        }

        public MatchPair? TryCreateMatchPair()
        {
            lock (_sync)
            {
                if (_queue.Count < 2)
                    return null;
                if (!_queue.TryDequeue(out var first, out var second))
                    throw new InvalidOperationException("매칭 대기열의 개수와 인출 결과가 일치하지 않습니다.");
                var pair = new MatchPair(first!, second!);
                var state = new MatchState(pair);
                _playerMatches.Add(pair.First.ClientId, state);
                _playerMatches.Add(pair.Second.ClientId, state);
                return pair;
            }
        }

        public void RemoveMatch(MatchState state)
        {
            lock (_sync)
            {
                _playerMatches.Remove(state.Players.First.ClientId);
                _playerMatches.Remove(state.Players.Second.ClientId);
            }
        }

        public void Clear()
        {
            lock (_sync)
            {
                _playerMatches.Clear();
                _queue.Clear();
            }
        }
    }
}
