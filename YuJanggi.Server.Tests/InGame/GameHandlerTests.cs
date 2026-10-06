using System.Text.Json;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace YuJanggi.Server.Tests.InGame
{
    using YuJanggi.Protocol.InGame;
    using YuJanggi.Protocol.Matching;
    using YuJanggi.Protocol.Messages;
    using YuJanggi.Server.Features.Game;
    using YuJanggi.Server.Features.Game.State;
    using YuJanggi.Server.Tests.Connection;
    using static YuJanggi.Server.Tests.Connection.TestSupport;

    [TestClass]
    [TestCategory("InGame")]
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
                { Team = ProtocolPlayerTeam.Cho, FromX = 0, FromZ = 3, ToX = 0, ToZ = 4 };

            await handler.HandleAsync(cho.Session,
                Message(ClientMessageType.MovePieceRequest, "move-id", request), timeout.Token);
            var response = await cho.Read(timeout.Token);
            Assert.AreEqual(ServerMessageType.MovePieceResponse, response.Type);
            Assert.AreEqual("move-id", response.RequestId);
            Assert.AreEqual(MovePieceResult.Accepted, response.GetPayload<MovePieceResponse>().Result);
            foreach (var peer in new[] { cho, han })
            {
                var message = await peer.Read(timeout.Token);
                Assert.AreEqual(ServerMessageType.MovePieceEvent, message.Type);
                Assert.IsNull(message.RequestId);
                var move = message.GetPayload<MovePieceEvent>();
                Assert.AreEqual(request.Team, move.Team);
                Assert.AreEqual(request.FromX, move.FromX);
                Assert.AreEqual(request.FromZ, move.FromZ);
                Assert.AreEqual(request.ToX, move.ToX);
                Assert.AreEqual(request.ToZ, move.ToZ);
            }
        }

        [TestMethod]
        public async Task GameEndResponsePrecedesFinalEventAndRoomRemoval()
        {
            await using var rooms = CreateRoomManager(cho.Session, han.Session);
            var room = rooms.CreateGameRoom("end-event", cho.Session, han.Session);
            var service = new GameService(rooms);
            service.MarkPlayerReady(cho.Session);
            service.MarkPlayerReady(han.Session);
            var handler = new GameHandler(service);
            var request = GameServiceTests.End(ProtocolPlayerTeam.Han, 20);

            await handler.HandleAsync(cho.Session,
                Message(ClientMessageType.GameEndRequest, "cho-end", request), timeout.Token);
            var first = await cho.Read(timeout.Token);
            Assert.AreEqual(ServerMessageType.GameEndResponse, first.Type);
            Assert.AreEqual("cho-end", first.RequestId);
            Assert.AreEqual(GameEndResult.Accepted, first.GetPayload<GameEndResponse>().Result);
            Assert.AreEqual(GameRoomState.Playing, room.State);
            Assert.IsFalse(cho.Client.GetStream().DataAvailable);
            Assert.IsFalse(han.Client.GetStream().DataAvailable);

            await handler.HandleAsync(han.Session,
                Message(ClientMessageType.GameEndRequest, "han-end", request), timeout.Token);
            var second = await han.Read(timeout.Token);
            Assert.AreEqual(ServerMessageType.GameEndResponse, second.Type);
            Assert.AreEqual("han-end", second.RequestId);
            Assert.AreEqual(GameEndResult.Accepted, second.GetPayload<GameEndResponse>().Result);
            foreach (var peer in new[] { cho, han })
            {
                var message = await peer.Read(timeout.Token);
                Assert.AreEqual(ServerMessageType.GameEndedEvent, message.Type);
                Assert.IsNull(message.RequestId);
                var ended = message.GetPayload<GameEndedEvent>();
                Assert.AreEqual(request.Winner, ended.Winner);
                Assert.AreEqual(request.TotalMoves, ended.TotalMoves);
            }
            Assert.AreEqual(GameRoomState.Closed, room.State);
            Assert.IsFalse(rooms.TryGetRoom(room.MatchId, out _));
        }

        [TestMethod]
        public async Task EndedRoomIsRemovedWhenResponseSendFails()
        {
            await using var rooms = CreateRoomManager(cho.Session, han.Session);
            var room = rooms.CreateGameRoom("failed-end", cho.Session, han.Session);
            var service = new GameService(rooms);
            service.MarkPlayerReady(cho.Session);
            service.MarkPlayerReady(han.Session);
            var request = GameServiceTests.End(ProtocolPlayerTeam.Cho, 10);
            service.ProcessGameEnd(cho.Session, request);
            han.Session.Dispose();

            await ExpectFailure(() => new GameHandler(service).HandleAsync(han.Session,
                Message(ClientMessageType.GameEndRequest, "failed-end", request), timeout.Token));
            Assert.AreEqual(GameRoomState.Closed, room.State);
            Assert.IsFalse(rooms.TryGetRoom(room.MatchId, out _));
        }

        private static ClientMessage Message<T>(ClientMessageType type, string id, T payload)
            => new() { Type = type, RequestId = id, Payload = JsonSerializer.SerializeToElement(payload) };
    }
}
