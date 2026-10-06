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
using YuJanggi.Server.Core.Sessions;
using YuJanggi.Server.Features.Game;
using YuJanggi.Server.Features.Lobby;
using YuJanggi.Server.Transport.Tcp;

using Microsoft.VisualStudio.TestTools.UnitTesting;
using ClientSession = YuJanggi.Server.Connection.ClientSession;

namespace YuJanggi.Server.Tests.Connection
{
    internal static class TestSupport
    {
        internal static GameRoomManager CreateRoomManager(params IClientSession[] participants)
        {
            // 실제 서버처럼 등록된 참가자만 룸을 생성할 수 있도록 테스트 세션을 등록합니다.
            var sessions = new ClientSessionManager();
            foreach (var participant in participants)
                Assert.IsTrue(sessions.Add(participant));

            return new GameRoomManager(sessions, new Lock());
        }
        internal static ClientMessage Request(string id) => new() { Type = ClientMessageType.MatchingStartRequest, RequestId = id };
        internal static ClientMessage Formation(string matchId, ProtocolFormation formation) => new()
        {
            Type = ClientMessageType.FormationSubmit,
            Payload = JsonSerializer.SerializeToElement(new FormationSubmit
            {
                MatchId = matchId,
                Formation = formation
            })
        };
        internal static async Task ExpectFailure(Func<Task> action)
        {
            try { await action(); }
            catch (Exception ex) when (ex is OperationCanceledException or ObjectDisposedException or IOException) { return; }
            Assert.Fail("예상한 전송 실패 또는 취소가 발생하지 않았습니다.");
        }
        internal static object Field(object instance, string name) => instance.GetType()
            .GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(instance)!;


    }
    internal sealed class Peer : IDisposable
    {
        public required TcpClient Client { get; init; }
        public required YuJanggi.Server.Connection.ClientSession Session { get; init; }
        public SemaphoreSlim SendLock => (SemaphoreSlim)typeof(TcpClientConnection)
            .GetField("_sendLock", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(Session.Connection)!;
        public static async Task<Peer> Create(bool handshake = true)
        {
            using var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            var client = new TcpClient();
            var connect = client.ConnectAsync((IPEndPoint)listener.LocalEndpoint);
            var accepted = await listener.AcceptTcpClientAsync();
            await connect;
            var session = new YuJanggi.Server.Connection.ClientSession(new TcpClientConnection(accepted), "검증 클라이언트");
            if (handshake) session.CompleteHandshake();
            return new Peer { Client = client, Session = session };
        }
        public async Task<ServerMessage> Read(CancellationToken token)
        {
            var stream = Client.GetStream();
            byte[] header = new byte[MessageFramer.HeaderSize];
            await stream.ReadExactlyAsync(header, token);
            byte[] body = new byte[MessageFramer.DecodeBodyLength(header)];
            await stream.ReadExactlyAsync(body, token);
            return MessageSerializer.Deserialize<ServerMessage>(body);
        }
        public void Dispose() { Session.Dispose(); Client.Dispose(); }
    }

}
