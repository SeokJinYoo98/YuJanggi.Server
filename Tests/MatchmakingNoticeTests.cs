using System.Net;
using System.Net.Sockets;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using YuJanggiCommon;

namespace YuJanggiServer.Tests;

[TestClass]
public sealed class MatchmakingNoticeTests
{
    [DataTestMethod]
    [DataRow(0)]
    [DataRow(1)]
    public async Task MatchedClientsReceiveSameTextWithActualSides(int draw)
    {
        string choName = draw == 0 ? "클라B" : "클라A";
        string hanName = draw == 0 ? "클라A" : "클라B";
        using var reservation = new TcpListener(IPAddress.Loopback, 0);
        reservation.Start();
        int port = ((IPEndPoint)reservation.LocalEndpoint).Port;
        reservation.Stop();
        var random = new FixedRandom(draw);
        var server = new YuJanggiServer(port, random);
        Task running = server.StartAsync();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        CancellationToken token = timeout.Token;
        using var choClient = new TcpClient();
        using var hanClient = new TcpClient();
        try
        {
            await choClient.ConnectAsync(IPAddress.Loopback, port, token);
            await hanClient.ConnectAsync(IPAddress.Loopback, port, token);
            NetworkStream cho = choClient.GetStream();
            NetworkStream han = hanClient.GetStream();
            await Send(cho, MessageType.Join, new JoinRequest(choName), token);
            await Receive<JoinResponse>(cho, MessageType.Join, token);
            await Send(han, MessageType.Join, new JoinRequest(hanName), token);
            await Receive<JoinResponse>(han, MessageType.Join, token);

            // 추첨이 뒤집혀도 클라B가 항상 먼저 대기하도록 합니다.
            NetworkStream first = draw == 0 ? cho : han;
            NetworkStream second = draw == 0 ? han : cho;
            await Send(first, MessageType.MatchmakingStart, new MatchmakingStartRequest(), token);
            var waiting = await Receive<MatchmakingStatusResponse>(first, MessageType.MatchmakingStatus, token);
            Assert.AreEqual(MatchmakingState.Waiting, waiting.State);
            // 대기를 취소한 연결도 다시 매칭에 참가할 수 있어야 합니다.
            await Send(first, MessageType.MatchmakingCancel, new MatchmakingCancelRequest(), token);
            var cancelled = await Receive<MatchmakingStatusResponse>(first, MessageType.MatchmakingStatus, token);
            Assert.AreEqual(MatchmakingState.Cancelled, cancelled.State);
            await Send(first, MessageType.MatchmakingStart, new MatchmakingStartRequest(), token);
            await Receive<MatchmakingStatusResponse>(first, MessageType.MatchmakingStatus, token);
            Assert.AreEqual(0, random.CallCount);

            await Send(second, MessageType.MatchmakingStart, new MatchmakingStartRequest(), token);
            var choMatch = await Receive<MatchFoundResponse>(cho, MessageType.MatchFound, token);
            var hanMatch = await Receive<MatchFoundResponse>(han, MessageType.MatchFound, token);
            Assert.AreEqual($"초: {choName}\n한: {hanName}", choMatch.Message);
            Assert.AreEqual(choMatch.Message, hanMatch.Message);
            Assert.AreEqual(choMatch.GameId, hanMatch.GameId);
            Assert.AreEqual(PlayerSide.Cho, choMatch.Side);
            Assert.AreEqual(PlayerSide.Han, hanMatch.Side);
            Assert.AreEqual(hanName, choMatch.Opponent.PlayerName);
            Assert.AreEqual(choName, hanMatch.Opponent.PlayerName);
            var choStart = await Receive<GameStartEvent>(cho, MessageType.GameStart, token);
            var hanStart = await Receive<GameStartEvent>(han, MessageType.GameStart, token);
            Assert.AreEqual(choMatch.GameId, choStart.GameId);
            Assert.AreEqual(hanMatch.GameId, hanStart.GameId);
            Assert.AreEqual(choMatch.Side, choStart.Side);
            Assert.AreEqual(hanMatch.Side, hanStart.Side);
            Assert.AreEqual(PlayerSide.Cho, choStart.CurrentTurn);
            Assert.AreEqual(PlayerSide.Cho, hanStart.CurrentTurn);
            Assert.AreEqual(1, random.CallCount);

            // 안내뿐 아니라 실제 서버의 턴 권한도 추첨 결과를 따라야 합니다.
            var legalMovesRequest = new LegalMovesRequest(new BoardPosition(0, 3));
            await Send(cho, MessageType.LegalMovesRequest, legalMovesRequest, token);
            await Receive<LegalMovesResult>(cho, MessageType.LegalMovesResult, token);
            await Send(han, MessageType.LegalMovesRequest, legalMovesRequest, token);
            var error = await Receive<ErrorResponse>(han, MessageType.Error, token);
            Assert.AreEqual(ErrorCode.NotYourTurn, error.Code);
        }
        finally
        {
            server.Stop();
            await running.WaitAsync(TimeSpan.FromSeconds(5));
        }
    }

    [TestMethod]
    public void NewClientCanReadMatchFromOldServerWithoutText()
    {
        var legacy = new LegacyMatchFound(Guid.NewGuid(), new MatchedPlayer(Guid.NewGuid(), "클라A"), PlayerSide.Han);
        var message = ChatMessage.Create(MessageType.MatchFound, null, legacy);
        var match = message.GetPayload<MatchFoundResponse>();
        Assert.AreEqual(string.Empty, match.Message);
        Assert.AreEqual(legacy.GameId, match.GameId);
        Assert.AreEqual(legacy.Side, match.Side);
    }

    [TestMethod]
    public void OldClientCanReadMatchWithAdditionalText()
    {
        var match = new MatchFoundResponse(Guid.NewGuid(), new MatchedPlayer(Guid.NewGuid(), "클라A"), PlayerSide.Cho)
        {
            Message = "초: 클라B\n한: 클라A"
        };
        var legacy = ChatMessage.Create(MessageType.MatchFound, null, match).GetPayload<LegacyMatchFound>();
        Assert.AreEqual(match.GameId, legacy.GameId);
        Assert.AreEqual(match.Side, legacy.Side);
        Assert.AreEqual(match.Opponent, legacy.Opponent);
    }

    [TestMethod]
    public async Task GameStartsAfterBothRequestedFormationsAreSelected()
    {
        using var reservation = new TcpListener(IPAddress.Loopback, 0);
        reservation.Start();
        int port = ((IPEndPoint)reservation.LocalEndpoint).Port;
        reservation.Stop();
        var server = new YuJanggiServer(port, new FixedRandom(0));
        Task running = server.StartAsync();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        CancellationToken token = timeout.Token;
        using var choClient = new TcpClient();
        using var hanClient = new TcpClient();

        try
        {
            await choClient.ConnectAsync(IPAddress.Loopback, port, token);
            await hanClient.ConnectAsync(IPAddress.Loopback, port, token);
            NetworkStream cho = choClient.GetStream();
            NetworkStream han = hanClient.GetStream();

            await Send(cho, MessageType.Join, new JoinRequest("초"), token);
            await Receive<JoinResponse>(cho, MessageType.Join, token);
            await Send(han, MessageType.Join, new JoinRequest("한"), token);
            await Receive<JoinResponse>(han, MessageType.Join, token);

            var request = new MatchmakingStartRequest { SelectFormation = true };
            await Send(cho, MessageType.MatchmakingStart, request, token);
            await Receive<MatchmakingStatusResponse>(cho, MessageType.MatchmakingStatus, token);
            await Send(han, MessageType.MatchmakingStart, request, token);
            MatchFoundResponse choMatch =
                await Receive<MatchFoundResponse>(cho, MessageType.MatchFound, token);
            MatchFoundResponse hanMatch =
                await Receive<MatchFoundResponse>(han, MessageType.MatchFound, token);
            Assert.IsTrue(choMatch.RequiresFormationSelection);
            Assert.IsTrue(hanMatch.RequiresFormationSelection);

            await Send(
                cho,
                MessageType.SelectFormation,
                new SelectFormationRequest(choMatch.GameId, GameFormation.HEHE),
                token);
            await Receive<FormationSelectedResponse>(
                cho,
                MessageType.FormationSelected,
                token);

            await Send(
                cho,
                MessageType.LegalMovesRequest,
                new LegalMovesRequest(new BoardPosition(0, 3)),
                token);
            ErrorResponse notStarted =
                await Receive<ErrorResponse>(cho, MessageType.Error, token);
            Assert.AreEqual(ErrorCode.GameNotStarted, notStarted.Code);

            await Send(
                han,
                MessageType.SelectFormation,
                new SelectFormationRequest(hanMatch.GameId, GameFormation.EHEH),
                token);
            await Receive<FormationSelectedResponse>(
                han,
                MessageType.FormationSelected,
                token);
            GameStartEvent choStart =
                await Receive<GameStartEvent>(cho, MessageType.GameStart, token);
            GameStartEvent hanStart =
                await Receive<GameStartEvent>(han, MessageType.GameStart, token);

            Assert.AreEqual(GameFormation.HEHE, choStart.ChoFormation);
            Assert.AreEqual(GameFormation.EHEH, choStart.HanFormation);
            Assert.AreEqual(choStart.ChoFormation, hanStart.ChoFormation);
            Assert.AreEqual(choStart.HanFormation, hanStart.HanFormation);
            // 배포된 Core/Protocol 조합으로 이동 전파와 종료까지 검증합니다.
            var from = new BoardPosition(0, 3);
            await Send(cho, MessageType.LegalMovesRequest, new LegalMovesRequest(from), token);
            var legal = await Receive<LegalMovesResult>(cho, MessageType.LegalMovesResult, token);
            Assert.IsTrue(legal.LegalMoves.Count > 0);
            var to = legal.LegalMoves[0];
            await Send(cho, MessageType.MoveRequest, new MoveRequest(from, to), token);
            var choMove = await Receive<MoveResultEvent>(cho, MessageType.MoveResult, token);
            var hanMove = await Receive<MoveResultEvent>(han, MessageType.MoveResult, token);
            Assert.AreEqual(choMatch.GameId, choMove.GameId);
            Assert.AreEqual(PlayerSide.Han, choMove.CurrentTurn);
            Assert.AreEqual(to, choMove.To);
            Assert.AreEqual(choMove.To, hanMove.To);
            CollectionAssert.AreEqual(choMove.Pieces.ToArray(), hanMove.Pieces.ToArray());
            choClient.Close();
            var ended = await Receive<GameEndEvent>(han, MessageType.GameEnd, token);
            Assert.AreEqual(hanMatch.GameId, ended.GameId);
            Assert.AreEqual(GameEndReason.OpponentLeft, ended.Reason);
        }
        finally
        {
            server.Stop();
            await running.WaitAsync(TimeSpan.FromSeconds(5));
        }
    }

    private static async Task Send<T>(NetworkStream stream, MessageType type, T payload, CancellationToken token)
    {
        byte[] packet = MessageProtocol.Encode(ChatMessage.Create(type, Guid.NewGuid().ToString("N"), payload));
        await stream.WriteAsync(packet, token);
    }

    private static async Task<T> Receive<T>(NetworkStream stream, MessageType type, CancellationToken token)
    {
        byte[] header = new byte[MessageProtocol.HeaderSize];
        await stream.ReadExactlyAsync(header, token);
        byte[] body = new byte[MessageProtocol.DecodeBodyLength(header)];
        await stream.ReadExactlyAsync(body, token);
        ChatMessage message = MessageProtocol.DecodeBody(body);
        Assert.AreEqual(type, message.Type);
        return message.GetPayload<T>();
    }

    public sealed record LegacyMatchFound(Guid GameId, MatchedPlayer Opponent, PlayerSide Side);

    private sealed class FixedRandom(int result) : Random
    {
        public int CallCount { get; private set; }

        public override int Next(int maxValue)
        {
            Assert.AreEqual(2, maxValue);
            CallCount++;
            return result;
        }
    }
}
