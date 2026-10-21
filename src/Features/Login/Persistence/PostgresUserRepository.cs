using Npgsql;

namespace YuJanggi.Server.Features.Login.Persistence
{
    internal sealed class PostgresUserRepository
    {
        private readonly NpgsqlDataSource _dataSource;

        public PostgresUserRepository(NpgsqlDataSource dataSource)
        {
            ArgumentNullException.ThrowIfNull(dataSource);
            _dataSource = dataSource;
        }

        public async Task<UserAccount?> FindByGoogleSubAsync(
            string googleSub,
            CancellationToken cancellationToken = default)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(googleSub);

            const string sql = """
                SELECT id, google_sub, nickname, created_at
                FROM public.users
                WHERE google_sub = $1;
                """;

            await using var command = _dataSource.CreateCommand(sql);
            command.Parameters.AddWithValue(googleSub);

            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            if (!await reader.ReadAsync(cancellationToken))
                return null;

            return new UserAccount(
                reader.GetGuid(0),
                reader.GetString(1),
                reader.GetString(2),
                reader.GetFieldValue<DateTimeOffset>(3));
        }
    }
}
