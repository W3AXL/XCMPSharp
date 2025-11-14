using System.Net;
using System.Net.Sockets;
using Org.BouncyCastle.Crypto;

namespace xcmp.connection
{
    public enum IPConnectionType
    {
        TCP,
        UDP,
    }
    
    public class XCMPIPConnection : XCMPBaseConnection
    {
        private string remoteAddress;
        private int remotePort;
        private TcpClient tcpClient;
        private NetworkStream tcpStream;
        private UdpClient udpClient;
        private IPEndPoint udpEndpoint;
        private IPConnectionType ipType;
        public bool Connected { get; private set; }
        
        public XCMPIPConnection(string remoteAddress, int remotePort, IPConnectionType connectionType)
        {
            this.remoteAddress = remoteAddress;
            this.remotePort = remotePort;
            this.ipType = connectionType;
            Connected = false;
        }

        public void Dispose()
        {
            Disconnect();
            tcpStream?.Dispose();
            tcpClient?.Dispose();
            udpClient?.Dispose();
        }

        public void Connect()
        {
            if (ipType == IPConnectionType.TCP)
            {
                tcpClient = new TcpClient(remoteAddress, remotePort);
                tcpClient.ReceiveTimeout = 1000;
                tcpStream = tcpClient.GetStream();
            }
            else
            {
                udpClient = new UdpClient(remotePort);
                udpClient.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReceiveTimeout, 1000);
                udpClient.Connect(remoteAddress, remotePort);
                udpEndpoint = new IPEndPoint(IPAddress.Parse(remoteAddress), remotePort);
            }
            Connected = true;
        }

        public void Disconnect()
        {
            tcpStream?.Close();
            tcpClient?.Close();
            udpClient?.Close();
            Connected = false;
        }

        public byte[] Receive()
        {
            if (ipType == IPConnectionType.TCP)
            {
                byte[] data = new byte[1024];
                int bytesRead = tcpStream.Read(data);
                return data.Take(bytesRead).ToArray();
            }
            else
            {
                return udpClient.Receive(ref udpEndpoint);
            }
        }

        public void Send(byte[] data)
        {
            if (ipType == IPConnectionType.TCP)
            {
                tcpStream.Write(data, 0, data.Length);
            }
            else
            {
                udpClient.Send(data, data.Length);
            }
        }
    }
}