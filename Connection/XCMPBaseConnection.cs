using xcmp;

namespace xcmp.connection
{

    public enum XCMPConnectionStatus
    {
        DISCONNECTED,
        CONNECTING,
        CONNECTED,
        DISCONNECTING,
        ERROR
    }

    public interface XCMPBaseConnection : IDisposable
    {
        /// <summary>
        /// Whether the XCMP connection is currently established
        /// </summary>
        public XCMPConnectionStatus Status { get; }
        public event EventHandler<byte[]> OnReceive;
        public int Timeout { get; }
        
        public Task Connect();
        public Task Disconnect();
        public Task Send(byte[] data);
    }
}