using System.Net;
using System.Net.Sockets;
using Org.BouncyCastle.Crypto;
using Serilog;

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
        private CancellationTokenSource ts;
        private CancellationToken ct;
        public XCMPConnectionStatus Status { get; private set; }
        public event EventHandler<byte[]> OnReceive;
        public int Timeout { get; private set; }
        
        public XCMPIPConnection(string remoteAddress, int remotePort, IPConnectionType connectionType, int timeout = 1000)
        {
            this.remoteAddress = remoteAddress;
            this.remotePort = remotePort;
            this.ipType = connectionType;
            this.Timeout = timeout;
            Status = XCMPConnectionStatus.DISCONNECTED;
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
            Status = XCMPConnectionStatus.CONNECTING;

            if (ipType == IPConnectionType.TCP)
            {
                tcpClient = new TcpClient(remoteAddress, remotePort);
                tcpStream = tcpClient.GetStream();
            }
            else
            {
                udpClient = new UdpClient(remotePort);
                udpClient.Connect(remoteAddress, remotePort);
                udpEndpoint = new IPEndPoint(IPAddress.Parse(remoteAddress), remotePort);
            }
            // Start receiver thread
            ts = new CancellationTokenSource();
            ct = ts.Token;
            Task.Run(() => listen(ct), ct);
            // We are connected
            Status = XCMPConnectionStatus.CONNECTED;
        }

        public void Disconnect()
        {
            Status = XCMPConnectionStatus.DISCONNECTING;
            // Stop listen task
            ts?.Cancel();
            ts?.Dispose();
            ts = null;
            // Close connections
            tcpStream?.Close();
            tcpClient?.Close();
            udpClient?.Close();
            // Done
            Status = XCMPConnectionStatus.DISCONNECTED;
        }

        private async Task listen(CancellationToken token)
        {

            byte[] rxBuffer = new byte[1024];

            while (!token.IsCancellationRequested)
            {
                try
                {
                    if (ipType == IPConnectionType.TCP)
                    {
                        int bytesRead = await tcpStream.ReadAsync(rxBuffer, token);
                        OnReceive?.Invoke(this, rxBuffer.Take(bytesRead).ToArray());
                    }
                    else
                    {
                        UdpReceiveResult result = await udpClient.ReceiveAsync(token);
                        OnReceive?.Invoke(this, result.Buffer);
                    }
                }
                catch (OperationCanceledException)
                {
                    Log.Debug("Stopping XCMP IP listener");
                }
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