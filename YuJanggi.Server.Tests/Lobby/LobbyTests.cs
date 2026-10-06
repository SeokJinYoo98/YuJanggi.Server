using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace YuJanggi.Server.Tests.Lobby
{
    using Protocol.Matching;
    using Protocol.Messages;
    using Features.Lobby;
    using Tests.TestSupport;
    using static Tests.TestSupport.TestSupport;

    [TestClass]
    [DoNotParallelize]
    public sealed class LobbyTests
    {
        [TestMethod]
        [TestCategory("Lobby")]
        public async Task RequestCancelAndFifo()
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
            var token = timeout.Token;
            using var first = await Peer.Create();
            using var second = await Peer.Create();
            await using var rooms = CreateRoomManager(first.Session);
            var service = new LobbyService(new LobbyManager(), rooms);
            Assert.AreEqual(MatchRequestStatus.Accepted, service.RequestMatch(first.Session, out var pair));
            Assert.IsNull(pair);
            Assert.AreEqual(MatchRequestStatus.AlreadyMatching, service.RequestMatch(first.Session, out pair));
            Assert.IsNull(pair);
            Assert.AreEqual(MatchCancelStatus.Cancelled, service.CancelMatch(first.Session));
            Assert.AreEqual(MatchCancelStatus.Cancelled, service.CancelMatch(first.Session));
            Assert.AreEqual(MatchRequestStatus.Accepted, service.RequestMatch(first.Session, out _));
            Assert.AreEqual(MatchRequestStatus.Accepted, service.RequestMatch(second.Session, out pair));
            Assert.IsNotNull(pair);
            Assert.AreSame(first.Session, pair.First);
            Assert.AreSame(second.Session, pair.Second);
            Assert.AreNotEqual(pair.Second.ClientId, pair.First.ClientId);
            var queue = new MatchMakingQueue();
            queue.Enqueue(first.Session);
            Assert.IsFalse(queue.TryDequeue(out var a, out var b));
            Assert.IsNull(a);
            Assert.IsNull(b);
            Assert.AreEqual(1, queue.Count);
            queue.Enqueue(second.Session);
            Assert.IsTrue(queue.TryDequeue(out a, out b));
            Assert.AreSame(first.Session, a);
            Assert.AreSame(second.Session, b);
            Assert.AreEqual(0, queue.Count);

        }

        [TestMethod]
        [TestCategory("Lobby")]
        public async Task ResponsesPrecedeMatchingFound()
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
            using var first = await Peer.Create();
            using var second = await Peer.Create();
            var token = timeout.Token;
            await using var rooms = CreateRoomManager(first.Session, second.Session);
            var handler = new LobbyHandler(new LobbyService(new LobbyManager(), rooms));
            var sendLock = first.SendLock;
            await sendLock.WaitAsync(token);
            var firstTask = handler.HandleAsync(first.Session, Request("first"), token);
            var secondTask = handler.HandleAsync(second.Session, Request("second"), token);
            Assert.AreEqual(ServerMessageType.MatchingStartResponse, (await second.Read(token)).Type);
            Assert.IsFalse(secondTask.IsCompleted);
            Assert.IsFalse(firstTask.IsCompleted);
            sendLock.Release();
            await Task.WhenAll(firstTask, secondTask);
            var response = await first.Read(token);
            Assert.AreEqual(ServerMessageType.MatchingStartResponse, response.Type);
            Assert.AreEqual("first", response.RequestId);
            var found1 = await first.Read(token);
            var found2 = await second.Read(token);
            Assert.AreEqual(ServerMessageType.MatchingFoundEvent, found1.Type);
            Assert.AreEqual(ServerMessageType.MatchingFoundEvent, found2.Type);
            Assert.IsNull(found1.RequestId);
            Assert.IsNull(found2.RequestId);
            var expectedMatchId = found1.GetPayload<MatchingFoundEvent>().MatchId;
            Assert.AreEqual(expectedMatchId, found2.GetPayload<MatchingFoundEvent>().MatchId);
            Assert.AreEqual(ProtocolPlayerTeam.Cho, found1.GetPayload<MatchingFoundEvent>().MyTeam);
            Assert.AreEqual(ProtocolPlayerTeam.Han, found2.GetPayload<MatchingFoundEvent>().MyTeam);
            Assert.AreEqual(second.Session.ClientId.ToString(), found1.GetPayload<MatchingFoundEvent>().Opponent.PlayerId);
            Assert.AreEqual(first.Session.ClientId.ToString(), found2.GetPayload<MatchingFoundEvent>().Opponent.PlayerId);

        }

        [TestMethod]
        [TestCategory("Lobby")]
        public async Task FormationSubmissionsProduceGameReady()
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
            using var first = await Peer.Create();
            using var second = await Peer.Create();
            var token = timeout.Token;
            await using var rooms = CreateRoomManager(first.Session, second.Session);
            var handler = new LobbyHandler(new LobbyService(new LobbyManager(), rooms));
            await handler.HandleAsync(first.Session, Request("first"), token);
            Assert.AreEqual("first", (await first.Read(token)).RequestId);
            await handler.HandleAsync(second.Session, Request("second"), token);
            Assert.AreEqual("second", (await second.Read(token)).RequestId);
            var foundFirst = await first.Read(token);
            var foundSecond = await second.Read(token);
            string matchId = foundFirst.GetPayload<MatchingFoundEvent>().MatchId;
            Assert.AreEqual(matchId, foundSecond.GetPayload<MatchingFoundEvent>().MatchId);

            await handler.HandleAsync(first.Session, Formation("other-match", ProtocolFormation.HEHE), token);
            await handler.HandleAsync(first.Session, Formation(matchId, ProtocolFormation.HEHE), token);
            Assert.IsFalse(first.Client.GetStream().DataAvailable);
            Assert.IsFalse(second.Client.GetStream().DataAvailable);
            await handler.HandleAsync(second.Session, Formation(matchId, ProtocolFormation.EHEH), token);

            var readyFirst = await first.Read(token);
            var readySecond = await second.Read(token);
            Assert.AreEqual(ServerMessageType.GameReadyEvent, readyFirst.Type);
            Assert.AreEqual(ServerMessageType.GameReadyEvent, readySecond.Type);
            Assert.IsNull(readyFirst.RequestId);
            Assert.IsNull(readySecond.RequestId);
            Assert.AreEqual(matchId, readyFirst.GetPayload<GameReadyEvent>().MatchId);
            Assert.AreEqual(ProtocolFormation.HEHE, readyFirst.GetPayload<GameReadyEvent>().ChoFormation);
            Assert.AreEqual(ProtocolFormation.EHEH, readySecond.GetPayload<GameReadyEvent>().HanFormation);

        }

        [TestMethod]
        [TestCategory("Lobby")]
        public async Task CancelledRequestDoesNotEnqueue()
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
            using var first = await Peer.Create();
            using var second = await Peer.Create();
            var token = timeout.Token;
            await using var rooms = CreateRoomManager(first.Session, second.Session);
            var service = new LobbyService(new LobbyManager(), rooms);
            var handler = new LobbyHandler(service);
            using var cancelled = new CancellationTokenSource();
            cancelled.Cancel();
            await ExpectFailure(() => handler.HandleAsync(first.Session, Request("cancelled"), cancelled.Token));
            service.RequestMatch(second.Session, out var pair);
            Assert.IsNull(pair);

        }

        [TestMethod]
        [TestCategory("Lobby")]
        public async Task SendFailureRemovesQueuedSession()
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
            using var first = await Peer.Create();
            using var second = await Peer.Create();
            var token = timeout.Token;
            await using var rooms = CreateRoomManager(first.Session, second.Session);
            var service = new LobbyService(new LobbyManager(), rooms);
            var handler = new LobbyHandler(service);
            first.Session.Dispose();
            await ExpectFailure(() => handler.HandleAsync(first.Session, Request("failed"), token));
            service.RequestMatch(second.Session, out var pair);
            Assert.IsNull(pair);

        }

        [TestMethod]
        [TestCategory("Lobby")]
        public async Task CancellationSuppressesPairEvents()
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
            using var first = await Peer.Create();
            using var second = await Peer.Create();
            using var cancelled = new CancellationTokenSource();
            var token = timeout.Token;
            await using var rooms = CreateRoomManager(first.Session, second.Session);
            var handler = new LobbyHandler(new LobbyService(new LobbyManager(), rooms));
            await first.SendLock.WaitAsync(token);
            var firstTask = handler.HandleAsync(first.Session, Request("first"), cancelled.Token);
            var secondTask = handler.HandleAsync(second.Session, Request("second"), token);
            Assert.AreEqual(ServerMessageType.MatchingStartResponse, (await second.Read(token)).Type);
            cancelled.Cancel();
            await ExpectFailure(() => firstTask);
            await secondTask;
            Assert.IsFalse(second.Client.GetStream().DataAvailable);
            first.SendLock.Release();

        }

        [TestMethod]
        [TestCategory("Lobby")]
        public async Task DisconnectDuringPairEventDelivery()
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
            using var first = await Peer.Create();
            using var second = await Peer.Create();
            var token = timeout.Token;
            await using var rooms = CreateRoomManager(first.Session, second.Session);
            var handler = new LobbyHandler(new LobbyService(new LobbyManager(), rooms));
            await handler.HandleAsync(first.Session, Request("first"), token);
            await first.Read(token);
            await first.SendLock.WaitAsync(token);
            var matching = handler.HandleAsync(second.Session, Request("second"), token);
            Assert.AreEqual(ServerMessageType.MatchingStartResponse, (await second.Read(token)).Type);
            second.Session.Dispose();
            first.SendLock.Release();
            await ExpectFailure(() => matching);
            Assert.AreEqual(ServerMessageType.MatchingFoundEvent, (await first.Read(token)).Type);
            // 부분 전송 결과는 TODO로 명시한 현재 한계입니다. 복구 성공을 주장하지 않습니다.

        }

        [TestMethod]
        [TestCategory("Lobby")]
        public async Task ConcurrentRequestsAreAtomic()
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
            using var first = await Peer.Create();
            var token = timeout.Token;
            await using var rooms = CreateRoomManager(first.Session);
            var service = new LobbyService(new LobbyManager(), rooms);
            var results = await Task.WhenAll(Enumerable.Range(0, 32).Select(_ => Task.Run(() =>
            {
                var result = service.RequestMatch(first.Session, out var pair);
                Assert.IsNull(pair);
                return result;
            })));
            Assert.AreEqual(1, results.Count(r => r == MatchRequestStatus.Accepted));
            Assert.AreEqual(31, results.Count(r => r == MatchRequestStatus.AlreadyMatching));

        }

    }
}
