namespace YuJanggi.Server
{
    using Diagnostics;
    using Server;
    public static class Program
    {
        public static async Task Main(string[] args)
        {
            try
            { 
                if (args.Length != 0)
                {
                    Environment.ExitCode = args[0] == "--google-login"
                        ? await GoogleLoginDiagnostic.RunAsync(args)
                        : await DatabaseUserLookup.RunAsync(args);
                    return;
                }

                var server = new YuJanggiServer();
                View.NetworkView.ShowCommands();
                Task serverTask = server.RunAsync();
                await ReadCommandsAsync(server, serverTask);
            }
            catch (Exception exception)
            {
                View.NetworkView.Write(View.NetworkMessageType.Error, exception.ToString());
                Environment.ExitCode = 1;
            }
        }

        private static async Task ReadCommandsAsync(YuJanggiServer server, Task serverTask)
        {
            while (!serverTask.IsCompleted)
            {
                Task<string?> inputTask = Task.Run(() => Console.ReadLine());
                if (await Task.WhenAny(serverTask, inputTask) == serverTask)
                    break;

                string? input = await inputTask;
                if (input is null)
                    break;

                if (string.Equals(input.Trim(), "Clear", StringComparison.OrdinalIgnoreCase))
                    View.NetworkView.ClearAndShowCommands();
                else if (string.Equals(input.Trim(), "Rooms", StringComparison.OrdinalIgnoreCase))
                    server.ShowRooms();
            }

            await serverTask;
        }
    }
}
