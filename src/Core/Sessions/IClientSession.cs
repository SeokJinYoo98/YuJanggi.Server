namespace YuJanggi.Server.Core.Sessions
{
    using Protocol.Messages;

    /// <summary>
    /// 서버에 연결된 단일 클라이언트의 세션 정보를 관리합니다.
    /// 클라이언트 연결 객체와 해당 클라이언트를 처리하는 작업을 함께 보관합니다.
    /// </summary>
    internal interface IClientSession : IDisposable
    {
        Task?   ProcessingTask { get; }
        Guid    ClientId { get; }
        string  ConnectionInfo { get; }
        string? Nickname { get; }
        bool    IsHandshakeCompleted { get; }
        // public TcpClientConnection Connection { get; }
        void AttachProcessingTask(Task processingTask);
        void CompleteHandshake();
        Task SendAsync(
             ServerMessage message,
             CancellationToken cancellationToken);
        Task<ClientMessage> ReceiveAsync(
            CancellationToken cancellationToken);
    }
}
