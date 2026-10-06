using System.Collections.Concurrent;

namespace YuJanggi.Server.Features.Login
{
    /// <summary>연결 식별자와 인증된 사용자의 연결 정보를 관리합니다.</summary>
    internal sealed class LoginManager
    {
        private readonly ConcurrentDictionary<Guid, AuthenticatedUser> _users = new();

        public bool TryAdd(Guid clientId, AuthenticatedUser user) => _users.TryAdd(clientId, user);
        public bool TryGet(Guid clientId, out AuthenticatedUser? user) => _users.TryGetValue(clientId, out user);
        public void Remove(Guid clientId) => _users.TryRemove(clientId, out _);
        public void Clear() => _users.Clear();
    }
}
