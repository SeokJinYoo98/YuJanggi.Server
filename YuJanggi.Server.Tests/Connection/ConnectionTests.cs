using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Text.Json;
using YuJanggi.Protocol.Framing;
using YuJanggi.Protocol.Messages;
using YuJanggi.Protocol.Matching;
using YuJanggi.Protocol.Connection;
using YuJanggi.Protocol.Serialization;
using YuJanggi.Server;
using YuJanggi.Server.Connection;
using YuJanggi.Server.Features.Login;
using YuJanggi.Server.Features.Game;
using YuJanggi.Server.Features.Lobby;
using YuJanggi.Server.Transport.Tcp;

using Microsoft.VisualStudio.TestTools.UnitTesting;
using static YuJanggi.Server.Tests.TestSupport.TestSupport;
using Peer = YuJanggi.Server.Tests.TestSupport.Peer;
using ClientSession = YuJanggi.Server.Connection.ClientSession;

namespace YuJanggi.Server.Tests.Connection
{
    [TestClass]
    [DoNotParallelize]
    public sealed class ConnectionTests
    {
        [TestMethod]
        [TestCategory("Connection")]
        public async Task HandshakeGatesMatching()
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
            var token = timeout.Token;
            using var peer = await Peer.Create(false);
            await using var rooms = CreateRoomManager(peer.Session);
            var handler = new LobbyHandler(new LobbyService(new LobbyManager(), rooms));
            await handler.HandleAsync(peer.Session, Request("before"), token);
            var response = await peer.Read(token);
            Assert.AreEqual("before", response.RequestId);
            Assert.AreEqual(MatchingResult.HandshakeRequired, response.GetPayload<MatchingStartResponse>().Result);
            await new ProtocolHandshakeHandler(new LoginService(new LoginManager()))
                .HandleAsync(peer.Session, new ClientMessage
            {
                Type = ClientMessageType.HandshakeRequest, RequestId = "handshake",
                Payload = JsonSerializer.SerializeToElement(new ProtocolHandshakeRequest
                {
                    YuJanggiProtocolVersion = YuJanggi.Protocol.Version.Version.Current,
                    YuJanggiCoreVersion = YuJanggi.Engine.Version.Version.Current
                })
            }, token);
            Assert.AreEqual(ServerMessageType.HandshakeResponse, (await peer.Read(token)).Type);
            Assert.IsTrue(peer.Session.IsHandshakeCompleted);
            await handler.HandleAsync(peer.Session, Request("after"), token);
            Assert.AreEqual(MatchingResult.Accepted, (await peer.Read(token)).GetPayload<MatchingStartResponse>().Result);

        }

        [TestMethod]
        [TestCategory("Connection")]
        public async Task DisconnectCleansSessionAndQueue()
        {
            var token = timeout.Token;
            using var first = await Peer.Create();
            using var second = await Peer.Create();
            var server = new YuJanggiServer();
            var service = (LobbyService)Field(server, "_lobbyService");
            var handler = new LobbyHandler(service);
            await handler.HandleAsync(first.Session, new ClientMessage
            { Type = ClientMessageType.MatchingCancelRequest, RequestId = "cancel" }, token);
            var response = await first.Read(token);
            Assert.AreEqual("cancel", response.RequestId);
            Assert.AreEqual(MatchingCancelResult.Cancelled, response.GetPayload<MatchingCancelResponse>().Result);
            service.RequestMatch(first.Session, out _);
            var sessions = (ClientSessionManager)Field(server, "_sessionManager");
            Assert.IsTrue(sessions.Add(first.Session));
            var processing = (Task)typeof(YuJanggiServer)
                .GetMethod("HandleClientAsync", BindingFlags.Instance | BindingFlags.NonPublic)!
                .Invoke(server, [first.Session, token])!;
            first.Client.Dispose();
            await processing.WaitAsync(token);
            Assert.IsFalse(sessions.Contains(first.Session.ClientId));
            Assert.AreEqual(MatchRequestStatus.Accepted, service.RequestMatch(second.Session, out var pair));
            Assert.IsNull(pair);
            // 세션 목록이 이미 비워진 서버 종료 경로에서도 큐 정리가 실행되어야 합니다.
            await ((Task)typeof(YuJanggiServer).GetMethod("DisconnectClient", BindingFlags.Instance | BindingFlags.NonPublic)!
                .Invoke(server, [second.Session])!).WaitAsync(token);
            using var third = await Peer.Create();
            Assert.AreEqual(MatchRequestStatus.Accepted, service.RequestMatch(third.Session, out pair));
            Assert.IsNull(pair);

        }

    }
}
