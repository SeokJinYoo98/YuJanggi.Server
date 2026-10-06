namespace YuJanggi.Server.Core.Messaging
{
    using Protocol.Messages;

    internal static class RequestMessageValidation
    {
        public static string RequireRequestId(ClientMessage message, string requestName)
        {
            if (string.IsNullOrWhiteSpace(message.RequestId))
                throw new InvalidOperationException($"{requestName} 요청에 RequestId가 없습니다.");

            return message.RequestId;
        }
    }
}
