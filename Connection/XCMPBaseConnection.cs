namespace xcmp.connection
{
    public interface XCMPBaseConnection : IDisposable
    {
        public void Connect();

        public void Disconnect();
        public void Send(byte[] data);

        public byte[] Receive();
    }
}