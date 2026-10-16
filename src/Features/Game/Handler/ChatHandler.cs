using System;
using System.Collections.Generic;
using System.Text;
using YuJanggi.Protocol.Chat;
using YuJanggi.Protocol.Messages;
using YuJanggi.Server.Core.Messaging;
using YuJanggi.Server.Core.Sessions;

namespace YuJanggi.Server.Features.Game
{
    internal class ChatHandler
    {
        private readonly ChatService _chatService;

        public ChatHandler(ChatService chatService)
        {
            _chatService = chatService;
        }

        public async Task HandleAsync(
            IClientSession session,
            ClientMessage message,
            CancellationToken token)
        {
            if (message.Type != ClientMessageType.ChatRequest)
                return;

            var requestId = RequestMessageValidation.RequireRequestId(
                message, "Chat");

            var request = message.GetPayload<ChatRequest>();

            var result = _chatService.ProcessChat(
                session,
                request.Message);

            var response = ServerMessageFactory.CreateResponse(
                ServerMessageType.ChatResponse,
                requestId,
                new ChatResponse { Result = result.Result });

            await session.SendAsync(response, token);

            if (result.Result != ChatResult.Accepted)
                return;

            var chatEvent = ServerMessageFactory.CreateEvent(
                ServerMessageType.ChatEvent,
                new ChatEvent
                {
                    Team = result.Team,
                    Message = request.Message
                });

            await Task.WhenAll(result.Targets.Select(
                target => target.SendAsync(chatEvent, token)));

            // TODO: 일부 참가자에게 Event 전송이 실패한 경우의 처리 정책을 정합니다.
        }
    }
}
