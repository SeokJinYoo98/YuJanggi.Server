using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace YuJanggi.Server.Tests.Game
{
    using Protocol.InGame;
    using Protocol.Matching;
    using Features.Game;
    using Features.Game.State;
    using Tests.TestSupport;
    using static Tests.TestSupport.TestSupport;

    [TestClass]
    [TestCategory("Game")]
    [DoNotParallelize]
    public sealed class GameServiceTests
    {
        [TestMethod]
        public async Task ConcurrentReadyStartsRoomExactlyOnce()
        {
            using var cho = await Peer.Create();
            using var han = await Peer.Create();
            await using var rooms = CreateRoomManager(cho.Session, han.Session);
            var room = rooms.CreateGameRoom("ready", cho.Session, han.Session);
            var service = new GameService(rooms);

            Assert.AreEqual(GameRoomState.WaitingForReady, room.State);
            Assert.IsFalse(room.AllReady);
            var results = await Task.WhenAll(Enumerable.Range(0, 32).Select(index =>
                Task.Run(() => service.MarkPlayerReady(index % 2 == 0 ? cho.Session : han.Session))));

            Assert.AreEqual(1, results.Count(result => result is not null));
            var started = results.Single(result => result is not null)!;
            Assert.HasCount(2, started.Targets);
            Assert.AreSame(cho.Session, started.Targets[0]);
            Assert.AreSame(han.Session, started.Targets[1]);
            Assert.IsTrue(room.ChoReady);
            Assert.IsTrue(room.HanReady);
            Assert.IsTrue(room.AllReady);
            Assert.AreEqual(GameRoomState.Playing, room.State);
            Assert.IsNull(service.MarkPlayerReady(cho.Session));
            Assert.IsNull(service.MarkPlayerReady(han.Session));
        }

        [TestMethod]
        public async Task MatchingEndSubmissionsEndAndCloseRoom()
        {
            using var cho = await Peer.Create();
            using var han = await Peer.Create();
            await using var rooms = CreateRoomManager(cho.Session, han.Session);
            var room = rooms.CreateGameRoom("end", cho.Session, han.Session);
            var service = new GameService(rooms);
            service.MarkPlayerReady(cho.Session);
            service.MarkPlayerReady(han.Session);

            service.CloseGame(room.MatchId);
            Assert.IsTrue(rooms.TryGetRoom(room.MatchId, out _));
            Assert.AreEqual(GameRoomState.Playing, room.State);

            var first = service.ProcessGameEnd(cho.Session, End(ProtocolPlayerTeam.Cho, 12));
            Assert.AreEqual(GameRequestResult.Accepted, first.Result);
            Assert.IsFalse(first.IsGameEnded);
            Assert.IsEmpty(first.Targets);
            Assert.AreEqual(GameRoomState.Playing, room.State);
            Assert.AreEqual(GameRequestResult.Failed,
                service.ProcessGameEnd(cho.Session, End(ProtocolPlayerTeam.Cho, 12)).Result);

            var second = service.ProcessGameEnd(han.Session, End(ProtocolPlayerTeam.Cho, 12));
            Assert.AreEqual(GameRequestResult.Accepted, second.Result);
            Assert.IsTrue(second.IsGameEnded);
            Assert.AreEqual(room.MatchId, second.MatchId);
            Assert.AreEqual(ProtocolPlayerTeam.Cho, second.Winner);
            Assert.AreEqual(12, second.TotalMoves);
            Assert.HasCount(2, second.Targets);
            Assert.IsTrue(room.AllGameEndSubmitted);
            Assert.AreEqual(GameRoomState.Ended, room.State);
            Assert.AreEqual(GameRequestResult.Failed,
                service.ProcessGameEnd(han.Session, End(ProtocolPlayerTeam.Cho, 12)).Result);

            service.CloseGame(room.MatchId);
            Assert.AreEqual(GameRoomState.Closed, room.State);
            Assert.IsFalse(rooms.TryGetRoom(room.MatchId, out _));
            service.CloseGame(room.MatchId);
        }

        [TestMethod]
        public async Task MismatchedEndSubmissionCanBeCorrected()
        {
            using var cho = await Peer.Create();
            using var han = await Peer.Create();
            await using var rooms = CreateRoomManager(cho.Session, han.Session);
            var room = rooms.CreateGameRoom("mismatch", cho.Session, han.Session);
            var service = new GameService(rooms);
            service.MarkPlayerReady(cho.Session);
            service.MarkPlayerReady(han.Session);
            service.ProcessGameEnd(cho.Session, End(ProtocolPlayerTeam.Cho, 12));

            Assert.AreEqual(GameRequestResult.Mismatch,
                service.ProcessGameEnd(han.Session, End(ProtocolPlayerTeam.Han, 12)).Result);
            Assert.AreEqual(GameRequestResult.Mismatch,
                service.ProcessGameEnd(han.Session, End(ProtocolPlayerTeam.Cho, 13)).Result);
            Assert.IsFalse(room.HasSubmittedGameEnd(han.Session));
            Assert.AreEqual(GameRoomState.Playing, room.State);
            Assert.IsTrue(service.ProcessGameEnd(han.Session, End(ProtocolPlayerTeam.Cho, 12)).IsGameEnded);
        }

        [TestMethod]
        public async Task InvalidEndRequestsDoNotRecordSubmission()
        {
            using var cho = await Peer.Create();
            using var han = await Peer.Create();
            using var outsider = await Peer.Create();
            await using var rooms = CreateRoomManager(cho.Session, han.Session, outsider.Session);
            var room = rooms.CreateGameRoom("invalid", cho.Session, han.Session);
            var service = new GameService(rooms);
            Assert.AreEqual(GameRequestResult.Failed,
                service.ProcessGameEnd(cho.Session, End(ProtocolPlayerTeam.Cho, 1)).Result);
            service.MarkPlayerReady(cho.Session);
            service.MarkPlayerReady(han.Session);
            Assert.AreEqual(GameRequestResult.Failed,
                service.ProcessGameEnd(outsider.Session, End(ProtocolPlayerTeam.Cho, 1)).Result);
            Assert.AreEqual(GameRequestResult.Failed,
                service.ProcessGameEnd(cho.Session, End(ProtocolPlayerTeam.Cho, -1)).Result);
            Assert.AreEqual(GameRequestResult.Failed,
                service.ProcessGameEnd(cho.Session, End((ProtocolPlayerTeam)255, 1)).Result);
            Assert.IsFalse(room.HasSubmittedGameEnd(cho.Session));
            Assert.IsFalse(room.HasSubmittedGameEnd(han.Session));
            Assert.AreEqual(GameRoomState.Playing, room.State);
        }

        [TestMethod]
        public async Task RematchRequiresBothPlayersAndResetsPreviousGameState()
        {
            using var cho = await Peer.Create();
            using var han = await Peer.Create();
            await using var rooms = CreateRoomManager(cho.Session, han.Session);
            var room = rooms.CreateGameRoom("rematch", cho.Session, han.Session);
            var service = new GameService(rooms);
            service.MarkPlayerReady(cho.Session);
            service.MarkPlayerReady(han.Session);
            service.ProcessGameEnd(cho.Session, End(ProtocolPlayerTeam.Cho, 12));
            service.ProcessGameEnd(han.Session, End(ProtocolPlayerTeam.Cho, 12));

            var first = service.ProcessRematch(cho.Session, ProtocolPlayerTeam.Cho);
            Assert.AreEqual(GameRequestResult.Accepted, first.Result);
            Assert.IsFalse(first.IsConfirmed);
            Assert.IsEmpty(first.Targets);
            Assert.IsFalse(service.ProcessRematch(cho.Session, ProtocolPlayerTeam.Cho).IsConfirmed);
            Assert.AreEqual(GameRoomState.Ended, room.State);

            var second = service.ProcessRematch(han.Session, ProtocolPlayerTeam.Han);
            Assert.AreEqual(GameRequestResult.Accepted, second.Result);
            Assert.IsTrue(second.IsConfirmed);
            Assert.HasCount(2, second.Targets);
            Assert.AreEqual(GameRoomState.WaitingForReady, room.State);
            Assert.IsFalse(room.ChoReady);
            Assert.IsFalse(room.HanReady);
            Assert.IsFalse(room.HasSubmittedGameEnd(cho.Session));
            Assert.IsFalse(room.HasSubmittedGameEnd(han.Session));
            Assert.IsFalse(service.ProcessRematch(han.Session, ProtocolPlayerTeam.Han).IsConfirmed);

            Assert.IsNull(service.MarkPlayerReady(cho.Session));
            Assert.IsNotNull(service.MarkPlayerReady(han.Session));
            Assert.AreEqual(GameRoomState.Playing, room.State);
        }

        internal static GameEndRequest End(ProtocolPlayerTeam winner, int moves)
            => new() { Winner = winner, TotalMoves = moves };
    }
}
