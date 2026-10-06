namespace YuJanggi.Server.Features.Login
{
    using Core.Sessions;
    using Protocol.Connection;

    /// <summary>Handshake 검증과 인증된 사용자 연결·정리를 조정합니다.</summary>
    internal sealed class LoginService
    {
        private readonly LoginManager _manager;

        public LoginService(LoginManager manager) => _manager = manager;

        public ProtocolHandshakeResult ValidateHandshake(ProtocolHandshakeRequest request)
        {
            var result = ProtocolHandshakeResult.Success;
            if (request.YuJanggiProtocolVersion != Protocol.Version.Version.Current)
                result |= ProtocolHandshakeResult.ProtocolVersionMismatch;
            if (request.YuJanggiCoreVersion != Engine.Version.Version.Current)
                result |= ProtocolHandshakeResult.CoreVersionMismatch;
            return result;
        }

        public void CompleteHandshake(IClientSession session, ProtocolHandshakeResult result)
        {
            if (result == ProtocolHandshakeResult.Success)
                session.CompleteHandshake();
        }

        // 자격 증명 검증이 끝난 뒤에만 호출합니다. 이 메서드는 인증을 수행하지 않습니다.
        public bool RegisterAuthenticatedUser(IClientSession session, AuthenticatedUser user)
        {
            ArgumentNullException.ThrowIfNull(session);
            ArgumentNullException.ThrowIfNull(user);
            if (!session.IsHandshakeCompleted)
                throw new InvalidOperationException("Handshake가 완료되지 않았습니다.");
            return _manager.TryAdd(session.ClientId, user);
        }

        public bool TryGetUser(Guid clientId, out AuthenticatedUser? user) => _manager.TryGet(clientId, out user);
        public void DisconnectPlayer(IClientSession session) => _manager.Remove(session.ClientId);
        public void Clear() => _manager.Clear();
    }
}
