using ServerHost = YuJanggi.Server.YuJanggiServer;
public static class Program
{
    public static async Task Main(string[] args)
    {
        ServerHost server = new ServerHost(7777);

        Console.WriteLine("YuJanggi Server Start");
        Console.WriteLine("Port: 7777");
        Console.WriteLine("Commands: clientList, clientClear");

        Task serverTask = server.StartAsync();

        while (true)
        {
            string? command = Console.ReadLine()?.Trim();

            if (command == null)
            {
                server.Stop();
                break;
            }

            if (command.Equals(
                "clientList",
                StringComparison.OrdinalIgnoreCase))
            {
                server.PrintClientList();
                continue;
            }

            if (command.Equals(
                "clientClear",
                StringComparison.OrdinalIgnoreCase))
            {
                server.ClearClients();
                continue;
            }

            if (!string.IsNullOrWhiteSpace(command))
            {
                Console.WriteLine("지원하지 않는 명령어입니다.");
            }
        }

        await serverTask;
    }
}
