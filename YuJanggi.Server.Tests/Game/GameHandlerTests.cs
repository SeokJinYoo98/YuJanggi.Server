using System.Text.Json;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace YuJanggi.Server.Tests.Game
{
    using Protocol.InGame;
    using Protocol.Matching;
    using Protocol.Messages;
    using Features.Game;
    using Features.Game.State;
    using Tests.TestSupport;
    using static Tests.TestSupport.TestSupport;

    [TestClass]
    [TestCategory("Game")]
    [DoNotParallelize]
    public sealed class GameHandlerTests
    {
        [TestMethod]
        public async Task TemporaryMoveResponsePrecedesEventAndPreservesPayload()
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
            using var cho = await Peer.Create();
            using var han = await Peer.Create();
            await using var rooms = CreateRoomManager(cho.Session, han.Session);
            rooms.CreateGameRoom("move", cho.Session, han.Session);
            var service = new GameService(rooms);
            service.MarkPlayerReady(cho.Session);
            service.MarkPlayerReady(han.Session);
            var handler = new GameHandler(service);
            var request = new MovePieceRequest
                { Team = ProtocolPlayerTeam.Cho, TurnNumber = 1, FromX = 0, FromZ = 3, ToX = 0, ToZ = 4 };

            await handler.HandleAsync(cho.Session,
                Message(ClientMessageType.MovePieceRequest, "move-id", request), timeout.Token);
            var response = await cho.Read(timeout.Token);
            Assert.AreEqual(ServerMessageType.MovePieceResponse, response.Type);
            Assert.AreEqual("move-id", response.RequestId);
            Assert.AreEqual(MovePieceResult.Accepted, response.GetPayload<MovePieceResponse>().Result);
            foreach (var peer in new[] { cho, han })
            {
                var message = await peer.Read(timeout.Token);
                Assert.AreEqual(ServerMessageType.TurnCompletedEvent, message.Type);
                Assert.IsNull(message.RequestId);
                var move = message.GetPayload<TurnCompletedEvent>();
                Assert.AreEqual(request.Team, move.Team);
                Assert.AreEqual(ProtocolTurnAction.Move, move.Action);
                Assert.AreEqual(1, move.TurnNumber);
                Assert.AreEqual(ProtocolPlayerTeam.Han, move.NextTeam);
                Assert.AreEqual(ProtocolPassReason.None, move.PassReason);
                Assert.AreEqual(move.CompletedAt.AddSeconds(60), move.NextTurnDeadlineAt);
                Assert.AreEqual(request.FromX, move.FromX);
                Assert.AreEqual(request.FromZ, move.FromZ);
                Assert.AreEqual(request.ToX, move.ToX);
                Assert.AreEqual(request.ToZ, move.ToZ);
            }
        }

        [TestMethod]
        [DataRow(false)]
        [DataRow(true)]
        public async Task ExternalEndResponsePrecedesOneEventAndRoomIsRetained(bool useGameEndRequest)
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
            using var cho = await Peer.Create();
            using var han = await Peer.Create();
            await using var rooms = CreateRoomManager(cho.Session, han.Session);
            var room = rooms.CreateGameRoom("end-event", cho.Session, han.Session);
            var service = new GameService(rooms);
            service.MarkPlayerReady(cho.Session);
            service.MarkPlayerReady(han.Session);
            var handler = new GameHandler(service);
            var request = useGameEndRequest
                ? Message(ClientMessageType.GameEndRequest, "external-end",
                    GameServiceTests.End(ProtocolPlayerTeam.Cho, 999))
                : Message(ClientMessageType.GiveUpRequest, "external-end",
                    new GamePlayerRequest { Requester = ProtocolPlayerTeam.Cho });
            var responseType = useGameEndRequest
                ? ServerMessageType.GameEndResponse
                : ServerMessageType.GiveUpResponse;

            await handler.HandleAsync(cho.Session, request, timeout.Token);
            var response = await cho.Read(timeout.Token);
            Assert.AreEqual(responseType, response.Type);
            Assert.AreEqual("external-end", response.RequestId);
            Assert.AreEqual(GameRequestResult.Accepted, response.GetPayload<GameRequestResponse>().Result);

            foreach (var peer in new[] { cho, han })
            {
                var message = await peer.Read(timeout.Token);
                Assert.AreEqual(ServerMessageType.GameEndedEvent, message.Type);
                Assert.IsNull(message.RequestId);
                var ended = message.GetPayload<GameEndedEvent>();
                Assert.AreEqual(ProtocolPlayerTeam.Han, ended.Winner);
                Assert.AreEqual(ProtocolGameEndReason.GiveUp, ended.EndReason);
                Assert.AreEqual(0, ended.TotalMoves);
            }
            Assert.AreEqual(GameRoomState.Ended, room.State);
            Assert.IsTrue(rooms.TryGetRoom(room.MatchId, out _));

            await handler.HandleAsync(cho.Session, request, timeout.Token);
            var duplicate = await cho.Read(timeout.Token);
            Assert.AreEqual(responseType, duplicate.Type);
            Assert.AreEqual(GameRequestResult.Failed, duplicate.GetPayload<GameRequestResponse>().Result);
            Assert.IsFalse(cho.Client.GetStream().DataAvailable);
            Assert.IsFalse(han.Client.GetStream().DataAvailable);
        }

        [TestMethod]
        public async Task EndedRoomIsRetainedWhenResponseSendFails()
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
            using var cho = await Peer.Create();
            using var han = await Peer.Create();
            await using var rooms = CreateRoomManager(cho.Session, han.Session);
            var room = rooms.CreateGameRoom("failed-end", cho.Session, han.Session);
            var service = new GameService(rooms);
            service.MarkPlayerReady(cho.Session);
            service.MarkPlayerReady(han.Session);
            var request = GameServiceTests.End(ProtocolPlayerTeam.Cho, 10);
            han.Session.Dispose();

            await ExpectFailure(() => new GameHandler(service).HandleAsync(han.Session,
                Message(ClientMessageType.GameEndRequest, "failed-end", request), timeout.Token));
            Assert.AreEqual(GameRoomState.Ended, room.State);
            Assert.IsTrue(rooms.TryGetRoom(room.MatchId, out _));
        }

        [TestMethod]
        public async Task ReadyPublishesTheServerLimitAndFirstTurnDeadline()
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
            using var cho = await Peer.Create();
            using var han = await Peer.Create();
            await using var rooms = CreateRoomManager(cho.Session, han.Session);
            rooms.CreateGameRoom("start-timing", cho.Session, han.Session);
            var handler = new GameHandler(new GameService(rooms));

            await handler.HandleAsync(cho.Session,
                ClientMessageFactory.Create(ClientMessageType.GameSceneReady, new GameSceneReady()), timeout.Token);
            await handler.HandleAsync(han.Session,
                ClientMessageFactory.Create(ClientMessageType.GameSceneReady, new GameSceneReady()), timeout.Token);

            foreach (var peer in new[] { cho, han })
            {
                var message = await peer.Read(timeout.Token);
                Assert.AreEqual(ServerMessageType.GameStartEvent, message.Type);
                var started = message.GetPayload<GameStartEvent>();
                Assert.AreEqual(60f, started.TurnTimeSeconds);
                Assert.AreEqual(1, started.TurnNumber);
                Assert.AreEqual(ProtocolPlayerTeam.Cho, started.CurrentTeam);
                Assert.AreEqual(started.StartedAt.AddSeconds(60), started.TurnDeadlineAt);
            }
        }

        [TestMethod]
        public async Task UserPassPublishesItsReasonAndAStalePassHasNoEvent()
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
            using var cho = await Peer.Create();
            using var han = await Peer.Create();
            await using var rooms = CreateRoomManager(cho.Session, han.Session);
            rooms.CreateGameRoom("pass-timing", cho.Session, han.Session);
            var service = new GameService(rooms);
            service.MarkPlayerReady(cho.Session);
            service.MarkPlayerReady(han.Session);
            var handler = new GameHandler(service);
            var request = new GamePlayerRequest { Requester = ProtocolPlayerTeam.Cho, TurnNumber = 1 };

            await handler.HandleAsync(cho.Session,
                Message(ClientMessageType.PassTurnRequest, "pass-id", request), timeout.Token);
            var response = await cho.Read(timeout.Token);
            Assert.AreEqual(ServerMessageType.PassTurnResponse, response.Type);
            Assert.AreEqual(GameRequestResult.Accepted, response.GetPayload<GameRequestResponse>().Result);
            foreach (var peer in new[] { cho, han })
            {
                var message = await peer.Read(timeout.Token);
                Assert.AreEqual(ServerMessageType.TurnCompletedEvent, message.Type);
                var completed = message.GetPayload<TurnCompletedEvent>();
                Assert.AreEqual(ProtocolPlayerTeam.Cho, completed.Team);
                Assert.AreEqual(ProtocolTurnAction.Pass, completed.Action);
                Assert.AreEqual(ProtocolPassReason.UserRequest, completed.PassReason);
                Assert.AreEqual(1, completed.TurnNumber);
                Assert.AreEqual(ProtocolPlayerTeam.Han, completed.NextTeam);
                Assert.AreEqual(completed.CompletedAt.AddSeconds(60), completed.NextTurnDeadlineAt);
            }

            await handler.HandleAsync(cho.Session,
                Message(ClientMessageType.PassTurnRequest, "stale-pass", request), timeout.Token);
            var rejected = await cho.Read(timeout.Token);
            Assert.AreEqual("stale-pass", rejected.RequestId);
            Assert.AreEqual(GameRequestResult.StaleTurn, rejected.GetPayload<GameRequestResponse>().Result);
            Assert.IsFalse(cho.Client.GetStream().DataAvailable);
            Assert.IsFalse(han.Client.GetStream().DataAvailable);
        }

        private static ClientMessage Message<T>(ClientMessageType type, string id, T payload)
            => new() { Type = type, RequestId = id, Payload = JsonSerializer.SerializeToElement(payload) };
    }
}
