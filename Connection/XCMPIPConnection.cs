using System.Net;
using System.Net.Sockets;

namespace xcmp.connection
{
    public class XCMPIPConnection : XCMPBaseConnection
    {
        private string hostname = "";
        private int Port = 8002;
        private TcpClient Client;
        private NetworkStream Stream;
        
        public XCMPIPConnection(string hostname, int port)
        {
            this.hostname = hostname;
        }

        public void Dispose()
        {
            Disconnect();
            Stream.Dispose();
            Client.Dispose();
        }

        public void Connect()
        {
            Client = new TcpClient(hostname, Port);
            Stream = Client.GetStream();
        }

        public void Disconnect()
        {
            Stream?.Close();
            Client?.Close();
        }

        public byte[] Receive()
        {
            byte[] data = new byte[1024];
            Stream.Read(data, 0, data.Length);
            return data;
        }

        public void Send(byte[] data)
        {
            Stream.Write(data, 0, data.Length);
        }
    }
}