using System.Diagnostics;

using YuJanggi.Protocol.Chat;
using YuJanggi.Protocol.Matching;
using YuJanggi.Server.Core.Sessions;

namespace YuJanggi.Server.Features.Game
{
    internal sealed record ChatProcessingResult(
        ChatResult Result,
        ProtocolPlayerTeam Team,
        IReadOnlyList<IClientSession> Targets);
    internal class ChatService
    {
        private readonly GameRoomManager _gameRoomManager;
        private readonly Lock _sync = new();
        private readonly Dictionary<Guid, long> _lastSentAt = new();

        public ChatService(GameRoomManager gameRoomManager)
        {
            _gameRoomManager = gameRoomManager;
        }

        public ChatProcessingResult ProcessChat(
            IClientSession session,
            string message)
        {
            if (string.IsNullOrWhiteSpace(message)
                || message.Length > ChatRules.MaxLen)
                return Failed();

            var room = _gameRoomManager.GetRoomBySession(session);

            if (room is null)
                return Failed();

            lock (room.SyncRoot)
            {
                if (!room.Contains(session) || room.Closed)
                    return Failed();

                if (!TryAcceptSend(session.ClientId))
                    return Failed();

                var team = room.ChoPlayer?.ClientId == session.ClientId
                    ? ProtocolPlayerTeam.Cho
                    : ProtocolPlayerTeam.Han;

                var targets = new List<IClientSession>(2);

                if (room.ChoPlayer is { } cho)
                    targets.Add(cho);

                if (room.HanPlayer is { } han)
                    targets.Add(han);

                return new ChatProcessingResult(
                    ChatResult.Accepted,
                    team,
                    targets);
            }
        }

        private bool TryAcceptSend(Guid clientId)
        {
            lock (_sync)
            {
                long now = Stopwatch.GetTimestamp();

                if (_lastSentAt.TryGetValue(clientId, out long previous)
                    && Stopwatch.GetElapsedTime(previous, now)
                        < TimeSpan.FromSeconds(1))
                    return false;

                _lastSentAt[clientId] = now;
                return true;
            }
        }

        public void DisconnectPlayer(IClientSession session)
        {
            lock (_sync)
                _lastSentAt.Remove(session.ClientId);
        }

        public void Clear()
        {
            lock (_sync)
                _lastSentAt.Clear();
        }

        private static ChatProcessingResult Failed()
            => new(
                ChatResult.Failed,
                ProtocolPlayerTeam.None,
                Array.Empty<IClientSession>());
    }
}
