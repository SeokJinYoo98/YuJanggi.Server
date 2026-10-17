using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace YuJanggi.Server.Tests.Game
{
    using Engine.Domain;
    using Engine.JanggiEngine;
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
            Assert.AreEqual(60f, started.TurnTimeSeconds);
            Assert.AreEqual(1, started.Turn.TurnNumber);
            Assert.AreEqual(ProtocolPlayerTeam.Cho, started.Turn.Team);
            Assert.AreEqual(started.StartedAt.AddSeconds(60), started.Turn.DeadlineAt);
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
        public async Task GiveUpStopsEngineAndPreservesProgressForClose()
        {
            using var cho = await Peer.Create();
            using var han = await Peer.Create();
            await using var rooms = CreateRoomManager(cho.Session, han.Session);
            var room = rooms.CreateGameRoom("end", cho.Session, han.Session);
            var service = new GameService(rooms);
            service.MarkPlayerReady(cho.Session);
            service.MarkPlayerReady(han.Session);

            Assert.AreEqual(MovePieceResult.Accepted, service.ProcessMove(cho.Session,
                new MovePieceRequest
                {
                    Team = ProtocolPlayerTeam.Cho, TurnNumber = 1,
                    FromX = 0, FromZ = 3, ToX = 0, ToZ = 4
                }).Result);
            Assert.AreEqual(GameRequestResult.Accepted,
                service.ProcessPassTurn(han.Session,
                    new GamePlayerRequest { Requester = ProtocolPlayerTeam.Han, TurnNumber = 2 }).Result);

            var engine = (IJanggiEngine)Field(room, "_engine");
            var piece = engine.References.Board.GetPiece(new Pos(0, 4));
            var turn = engine.References.Turn.CurrentTeam;
            var score = engine.References.Score.Score;
            int turnsCompleted = 0;
            engine.References.GameEvents.OnTurnCompleted += _ => turnsCompleted++;

            service.CloseGame(room.MatchId);
            Assert.AreEqual(GameRoomState.Playing, room.State);

            var result = service.ProcessGiveUp(cho.Session, ProtocolPlayerTeam.Cho);
            Assert.AreEqual(GameRequestResult.Accepted, result.Result);
            Assert.IsTrue(result.IsGameEnded);
            Assert.AreEqual(ProtocolPlayerTeam.Han, result.Winner);
            Assert.AreEqual(ProtocolGameEndReason.GiveUp, result.EndReason);
            Assert.AreEqual(2, result.TotalMoves);
            Assert.HasCount(2, result.Targets);
            Assert.AreEqual(GameRoomState.Ended, room.State);
            Assert.IsTrue(engine.References.Turn.IsEnd);
            Assert.AreEqual(2, engine.References.ReadOnlyRecord.Count);
            Assert.AreEqual(piece, engine.References.Board.GetPiece(new Pos(0, 4)));
            Assert.AreEqual(turn, engine.References.Turn.CurrentTeam);
            Assert.AreEqual(score, engine.References.Score.Score);
            Assert.AreEqual(0, turnsCompleted);
            Assert.IsFalse(room.HasEngineGameResult);

            Assert.AreEqual(GameRequestResult.Failed,
                service.ProcessGiveUp(han.Session, ProtocolPlayerTeam.Han).Result);
            Assert.AreEqual(GameRequestResult.Failed,
                service.ProcessGameEnd(han.Session, End(ProtocolPlayerTeam.Cho, 999)).Result);
            Assert.AreEqual(GameRequestResult.Failed,
                service.ProcessPassTurn(han.Session,
                    new GamePlayerRequest { Requester = ProtocolPlayerTeam.Han, TurnNumber = 3 }).Result);
            Assert.AreEqual(MovePieceResult.IllegalMove,
                service.ProcessMove(han.Session, new MovePieceRequest { Team = ProtocolPlayerTeam.Han }).Result);

            service.CloseGame(room.MatchId);
            Assert.AreEqual(GameRoomState.Closed, room.State);
            Assert.IsFalse(rooms.TryGetRoom(room.MatchId, out _));
            service.CloseGame(room.MatchId);
        }

        [TestMethod]
        public async Task GameEndGiveUpUsesSessionAndIgnoresClaimedResult()
        {
            using var cho = await Peer.Create();
            using var han = await Peer.Create();
            await using var rooms = CreateRoomManager(cho.Session, han.Session);
            var room = rooms.CreateGameRoom("external", cho.Session, han.Session);
            var service = new GameService(rooms);
            service.MarkPlayerReady(cho.Session);
            service.MarkPlayerReady(han.Session);

            var result = service.ProcessGameEnd(cho.Session, End(ProtocolPlayerTeam.Cho, 999));
            Assert.AreEqual(GameRequestResult.Accepted, result.Result);
            Assert.IsTrue(result.IsGameEnded);
            Assert.AreEqual(ProtocolPlayerTeam.Han, result.Winner);
            Assert.AreEqual(0, result.TotalMoves);
            Assert.AreEqual(ProtocolGameEndReason.GiveUp, result.EndReason);
            Assert.AreEqual(GameRoomState.Ended, room.State);
            Assert.HasCount(2, result.Targets);
        }

        [TestMethod]
        public async Task InvalidExternalRequestsDoNotEndRoom()
        {
            using var cho = await Peer.Create();
            using var han = await Peer.Create();
            using var outsider = await Peer.Create();
            using var unverified = await Peer.Create(handshake: false);
            await using var rooms = CreateRoomManager(
                cho.Session, han.Session, outsider.Session, unverified.Session);
            var room = rooms.CreateGameRoom("invalid", cho.Session, han.Session);
            var service = new GameService(rooms);
            Assert.AreEqual(GameRequestResult.Failed,
                service.ProcessGameEnd(cho.Session, End(ProtocolPlayerTeam.Han, 0)).Result);
            service.MarkPlayerReady(cho.Session);
            service.MarkPlayerReady(han.Session);

            Assert.AreEqual(GameRequestResult.Failed,
                service.ProcessGiveUp(outsider.Session, ProtocolPlayerTeam.Cho).Result);
            Assert.AreEqual(GameRequestResult.Failed,
                service.ProcessGiveUp(unverified.Session, ProtocolPlayerTeam.Cho).Result);
            Assert.AreEqual(GameRequestResult.Mismatch,
                service.ProcessGiveUp(cho.Session, ProtocolPlayerTeam.Han).Result);

            foreach (var reason in new[]
            {
                ProtocolGameEndReason.Draw, ProtocolGameEndReason.CheckMate,
                ProtocolGameEndReason.Score, (ProtocolGameEndReason)255
            })
            {
                var request = End(ProtocolPlayerTeam.Cho, 999) with { EndReason = reason };
                Assert.AreEqual(GameRequestResult.Failed,
                    service.ProcessGameEnd(cho.Session, request).Result);
            }

            Assert.AreEqual(GameRoomState.Playing, room.State);
            var engine = (IJanggiEngine)Field(room, "_engine");
            Assert.IsFalse(engine.References.Turn.IsEnd);
            Assert.AreEqual(0, engine.References.ReadOnlyRecord.Count);
        }

        [TestMethod]
        public async Task ConcurrentExternalEndsAreAcceptedOnce()
        {
            using var cho = await Peer.Create();
            using var han = await Peer.Create();
            await using var rooms = CreateRoomManager(cho.Session, han.Session);
            var room = rooms.CreateGameRoom("concurrent-end", cho.Session, han.Session);
            var service = new GameService(rooms);
            service.MarkPlayerReady(cho.Session);
            service.MarkPlayerReady(han.Session);

            var results = await Task.WhenAll(
                Task.Run(() => service.ProcessGiveUp(cho.Session, ProtocolPlayerTeam.Cho)),
                Task.Run(() => service.ProcessGameEnd(han.Session, End(ProtocolPlayerTeam.Han, 999))));

            Assert.AreEqual(1, results.Count(result => result.Result == GameRequestResult.Accepted));
            Assert.AreEqual(1, results.Count(result => result.IsGameEnded));
            Assert.AreEqual(GameRoomState.Ended, room.State);
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
            service.ProcessPassTurn(cho.Session,
                new GamePlayerRequest { Requester = ProtocolPlayerTeam.Cho, TurnNumber = 1 });
            Assert.IsTrue(service.ProcessGiveUp(han.Session, ProtocolPlayerTeam.Han).IsGameEnded);

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
            Assert.IsFalse(service.ProcessRematch(han.Session, ProtocolPlayerTeam.Han).IsConfirmed);

            Assert.IsNull(service.MarkPlayerReady(cho.Session));
            Assert.IsNotNull(service.MarkPlayerReady(han.Session));
            Assert.AreEqual(GameRoomState.Playing, room.State);
            var engine = (IJanggiEngine)Field(room, "_engine");
            Assert.IsFalse(engine.References.Turn.IsEnd);
            Assert.AreEqual(0, engine.References.ReadOnlyRecord.Count);
            Assert.AreEqual(PlayerTeam.Cho, engine.ControllerQuery.CurrentTurn);
            var turn = room.GetTurnState();
            Assert.AreEqual(1, turn.TurnNumber);
            Assert.AreEqual(ProtocolPlayerTeam.Cho, turn.Team);
            Assert.AreEqual(turn.UpdatedAt.AddSeconds(60), turn.DeadlineAt);
        }

        [TestMethod]
        public async Task StaleMoveAndPassAreRejectedEvenWhenTheSameTeamHasTheTurnAgain()
        {
            using var cho = await Peer.Create();
            using var han = await Peer.Create();
            await using var rooms = CreateRoomManager(cho.Session, han.Session);
            var room = rooms.CreateGameRoom("stale-turn", cho.Session, han.Session);
            var service = new GameService(rooms);
            service.MarkPlayerReady(cho.Session);
            service.MarkPlayerReady(han.Session);

            Assert.AreEqual(MovePieceResult.Accepted, service.ProcessMove(cho.Session,
                new MovePieceRequest
                {
                    Team = ProtocolPlayerTeam.Cho, TurnNumber = 1,
                    FromX = 0, FromZ = 3, ToX = 0, ToZ = 4
                }).Result);
            var passed = service.ProcessPassTurn(han.Session,
                new GamePlayerRequest { Requester = ProtocolPlayerTeam.Han, TurnNumber = 2 });
            Assert.AreEqual(GameRequestResult.Accepted, passed.Result);
            Assert.IsNotNull(passed.NextTurn);
            Assert.AreEqual(3, passed.NextTurn.TurnNumber);
            Assert.AreEqual(ProtocolPlayerTeam.Cho, passed.NextTurn.Team);
            Assert.AreEqual(passed.NextTurn.UpdatedAt.AddSeconds(60), passed.NextTurn.DeadlineAt);

            var before = room.GetTurnState();
            var engine = (IJanggiEngine)Field(room, "_engine");
            foreach (int turnNumber in new[] { 0, 1, 5 })
            {
                var moved = service.ProcessMove(cho.Session, new MovePieceRequest
                {
                    Team = ProtocolPlayerTeam.Cho, TurnNumber = turnNumber,
                    FromX = 0, FromZ = 4, ToX = 0, ToZ = 5
                });
                Assert.AreEqual(MovePieceResult.StaleTurn, moved.Result);
                Assert.IsNull(moved.NextTurn);
                Assert.IsEmpty(moved.Targets);

                var stalePass = service.ProcessPassTurn(cho.Session,
                    new GamePlayerRequest { Requester = ProtocolPlayerTeam.Cho, TurnNumber = turnNumber });
                Assert.AreEqual(GameRequestResult.StaleTurn, stalePass.Result);
                Assert.IsNull(stalePass.NextTurn);
                Assert.IsEmpty(stalePass.Targets);
                Assert.AreEqual(before, room.GetTurnState());
                Assert.AreEqual(2, engine.References.ReadOnlyRecord.Count);
            }

            var accepted = service.ProcessMove(cho.Session, new MovePieceRequest
            {
                Team = ProtocolPlayerTeam.Cho, TurnNumber = 3,
                FromX = 0, FromZ = 4, ToX = 0, ToZ = 5
            });
            Assert.AreEqual(MovePieceResult.Accepted, accepted.Result);
            Assert.IsNotNull(accepted.NextTurn);
            Assert.AreEqual(4, accepted.NextTurn.TurnNumber);
            Assert.AreEqual(ProtocolPlayerTeam.Han, accepted.NextTurn.Team);
        }

        internal static GameEndRequest End(ProtocolPlayerTeam winner, int moves)
            => new() { EndReason = ProtocolGameEndReason.GiveUp, Winner = winner, TotalMoves = moves };
    }
}
