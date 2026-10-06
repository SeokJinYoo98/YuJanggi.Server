
namespace YuJanggi.Server.ClientSession
{
    using Transport;

    internal static class ClientSessionFactory
    {
        private static int _clientNumber;

        public static IClientSession CreateClientSession(
            TcpClientConnection connection)
        {
            int number =
                Interlocked.Increment(
                    ref _clientNumber);

            string nickname =
                $"Client{number:D2}";

            return new ClientSession(
                connection,
                nickname);
        }
    }
}
