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

            return ReadUser(reader);
        }

        public async Task<UserAccount> GetOrCreateByGoogleSubAsync(
            string googleSub,
            string initialNickname,
            CancellationToken cancellationToken = default)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(googleSub);
            ArgumentException.ThrowIfNullOrWhiteSpace(initialNickname);

            const string sql = """
                INSERT INTO public.users (google_sub, nickname)
                VALUES ($1, $2)
                ON CONFLICT (google_sub) DO NOTHING
                RETURNING id, google_sub, nickname, created_at;
                """;

            await using (var command = _dataSource.CreateCommand(sql))
            {
                command.Parameters.AddWithValue(googleSub);
                command.Parameters.AddWithValue(initialNickname);

                await using var reader = await command.ExecuteReaderAsync(cancellationToken);
                if (await reader.ReadAsync(cancellationToken))
                    return ReadUser(reader);
            }

            return await FindByGoogleSubAsync(googleSub, cancellationToken)
                ?? throw new InvalidOperationException("생성 또는 조회할 사용자를 찾을 수 없습니다.");
        }

        private static UserAccount ReadUser(NpgsqlDataReader reader)
        {
            return new UserAccount(
                reader.GetGuid(0),
                reader.GetString(1),
                reader.GetString(2),
                reader.GetFieldValue<DateTimeOffset>(3));
        }
    }
}
