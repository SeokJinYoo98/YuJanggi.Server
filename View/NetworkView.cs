using System;
using YuJanggi.Protocol.Connection;
using YuJanggi.Protocol.Matching;
using YuJanggi.Protocol.Messages;
namespace YuJanggi.Server.V2.View
{
    using System.Text.Json;
    using YuJanggi.Engine;


    internal enum NetworkMessageType
    {
        Error,
        Message,
        Debug
    }

    internal static class NetworkView
    {
        private static readonly object OutputLock = new();

        public static void ShowCommands()
        {
            lock (OutputLock)
                Console.WriteLine("사용 가능한 명령어: Clear");
        }

        public static void ClearAndShowCommands()
        {
            lock (OutputLock)
            {
                if (!Console.IsOutputRedirected)
                    Console.Clear();

                Console.WriteLine("사용 가능한 명령어: Clear");
            }
        }

        public static void Write(
            NetworkMessageType messageType,
            string message,
            string? nickname = null)
        {
            string source =
                string.IsNullOrWhiteSpace(nickname)
                    ? "[Server]"
                    : $"[{nickname}]";

            string lines =
                message.ReplaceLineEndings(
                    Environment.NewLine +
                    $"[{messageType}]: ");

            lock (OutputLock)
            {
                Console.WriteLine(
                    $"{source}" +
                    Environment.NewLine +
                    $"[{messageType}]: {lines}" +
                    Environment.NewLine);
            }
        }

        public static void ShowReceiveMessage(
            string? nickname,
            ClientMessage message)
        {
            Write(
                NetworkMessageType.Debug,
                $"Receive" +
                Environment.NewLine +
                $"Type: {message.Type}" +
                Environment.NewLine +
                $"RequestId: {message.RequestId ?? "None"}" +
                Environment.NewLine +
                $"Payload: {GetPayloadText(message.Payload)}",
                nickname);
        }

        public static void ShowSendMessage(
            string? nickname,
            ServerMessage message)
        {
            Write(
                NetworkMessageType.Debug,
                $"Send" +
                Environment.NewLine +
                $"Type: {message.Type}" +
                Environment.NewLine +
                $"RequestId: {message.RequestId ?? "None"}" +
                Environment.NewLine +
                $"Payload: {GetPayloadText(message.Payload)}",
                nickname);
        }

        private static string GetPayloadText(JsonElement? payload)
        {
            if (payload is null)
                return "None";

            return payload.Value.GetRawText();
        }
    }
}
