namespace YuJanggi.Server
{
    using Server;
    public static class Program
    {
        private static readonly YuJanggiServer _server = new YuJanggiServer();

        public static async Task Main()
        {
            try
            {

                View.NetworkView.ShowCommands();
                Task serverTask = _server.RunAsync();
                await ReadCommandsAsync(serverTask);
            }
            catch (Exception exception)
            {
                View.NetworkView.Write(View.NetworkMessageType.Error, exception.ToString());
                Environment.ExitCode = 1;
            }
        }

        private static async Task ReadCommandsAsync(Task serverTask)
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
                    _server.ShowRooms();
            }

            await serverTask;
        }
    }
}
