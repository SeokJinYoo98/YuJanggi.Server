namespace YuJanggi.Server.Features.Login.Persistence
{
    internal sealed record UserAccount(
        Guid Id,
        string GoogleSub,
        string Nickname,
        DateTimeOffset CreatedAt);
}
