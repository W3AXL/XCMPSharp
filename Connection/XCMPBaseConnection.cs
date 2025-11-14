using xcmp;

namespace xcmp.connection
{

    public interface XCMPBaseConnection : IDisposable
    {
        /// <summary>
        /// Whether the XCMP connection is currently established
        /// </summary>
        public bool Connected { get; }
        public void Connect();

        public void Disconnect();
        public void Send(byte[] data);

        public byte[] Receive();
    }
}