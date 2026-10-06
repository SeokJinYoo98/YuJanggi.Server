namespace YuJanggi.Server.Features.Login
{
    /// <summary>인증 과정에서 확인된 사용자 정보입니다. ClientId와 별도로 관리합니다.</summary>
    internal sealed record AuthenticatedUser
    {
        public string UserId { get; }
        public string DisplayName { get; }

        public AuthenticatedUser(string userId, string displayName)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(userId);
            ArgumentException.ThrowIfNullOrWhiteSpace(displayName);
            UserId = userId;
            DisplayName = displayName;
        }
    }
}
