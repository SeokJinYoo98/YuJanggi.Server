namespace YuJanggi.Server.ClientSession
{
    using Core.Sessions;
    using Protocol.Messages;

    using Transport;
    using View;

    internal sealed class ClientSession : IClientSession
    {
        #region Field

        #endregion

        #region Properties
        public TcpClientConnection Connection { get; }
        public Guid ClientId =>
            Connection.ClientId;
        public string ConnectionInfo =>
            Connection.ConnectionInfo;
     
        public Task? ProcessingTask { get; private set; }
        public string? Nickname { get; private set; }
        public bool IsHandshakeCompleted { get; private set; }

        #endregion

        #region Constructors

        public ClientSession(
            TcpClientConnection connection,
            string? nickname)
        {
            Connection = connection;
            Nickname = nickname;
        }

        #endregion

        #region Public Methods

        public void AttachProcessingTask(
            Task processingTask)
        {
            if (ProcessingTask is not null)
            {
                throw new InvalidOperationException(
                    "ProcessingTask가 이미 설정되어 있습니다.");
            }

            ProcessingTask = processingTask;
        }

        public void CompleteHandshake()
        {
            IsHandshakeCompleted = true;
        }

        public void Dispose()
        {
            Connection.Dispose();
        }
        public async Task SendAsync(
         ServerMessage message,
         CancellationToken cancellationToken)
        {
            await Connection.SendAsync(
                message,
                cancellationToken);

            NetworkView.ShowSendMessage(
                Nickname,
                message);
        }

        public async Task<ClientMessage> ReceiveAsync(
            CancellationToken cancellationToken)
        {
            ClientMessage message =
                    await Connection.ReceiveAsync(
                        cancellationToken);

            NetworkView.ShowReceiveMessage(
                Nickname,
                message);

            return message;
        }
    }
        #endregion
}

