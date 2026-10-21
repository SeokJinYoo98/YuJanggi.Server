using Npgsql;

namespace YuJanggi.Server.Diagnostics
{
    using Features.Login.Persistence;

    internal static class DatabaseUserLookup
    {
        private const string ConnectionStringVariable = "YUJANGGI_DATABASE_CONNECTION_STRING";

        public static async Task<int> RunAsync(string[] args)
        {
            if (args.Length != 2 || args[0] != "--find-user" || string.IsNullOrWhiteSpace(args[1]))
            {
                Console.Error.WriteLine("사용법: --find-user <google_sub>");
                return 1;
            }

            string? connectionString = Environment.GetEnvironmentVariable(ConnectionStringVariable);
            if (string.IsNullOrWhiteSpace(connectionString))
            {
                Console.Error.WriteLine($"{ConnectionStringVariable} 환경 변수를 설정해 주세요.");
                return 1;
            }

            await using var dataSource = NpgsqlDataSource.Create(connectionString);
            var repository = new PostgresUserRepository(dataSource);
            UserAccount? user = await repository.FindByGoogleSubAsync(args[1]);

            if (user is null)
            {
                Console.WriteLine("사용자를 찾을 수 없습니다.");
                return 2;
            }

            Console.WriteLine($"id: {user.Id}");
            Console.WriteLine($"google_sub: {user.GoogleSub}");
            Console.WriteLine($"nickname: {user.Nickname}");
            Console.WriteLine($"created_at: {user.CreatedAt:O}");
            return 0;
        }
    }
}
