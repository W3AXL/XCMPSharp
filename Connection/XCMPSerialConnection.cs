namespace xcmp.connection
{
    public class XCMPPPPConnection : XCMPBaseConnection
    {
        public XCMPPPPConnection(string serialPort)
        {
        }

        public void Dispose()
        {
            Disconnect();
        }

        public void Connect()
        {
            //
        }

        public void Disconnect()
        {
            //
        }

        public byte[] Receive()
        {
            return null;
        }

        public void Send(byte[] data)
        {
            //
        }
    }
}