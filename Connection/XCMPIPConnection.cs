using System.Diagnostics;
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
            Disconnect().GetAwaiter().GetResult();
            tcpStream?.Dispose();
            tcpClient?.Dispose();
            udpClient?.Dispose();
        }

        public Task Connect()
        {
            Status = XCMPConnectionStatus.CONNECTING;

            if (ipType == IPConnectionType.TCP)
            {
                tcpClient = new TcpClient(remoteAddress, remotePort);
                tcpClient.SendTimeout = Timeout;
                tcpClient.ReceiveTimeout = Timeout;
                tcpStream = tcpClient.GetStream();
            }
            else
            {
                udpClient = new UdpClient(remotePort);
                udpClient.Client.ReceiveTimeout = Timeout;
                udpClient.Client.SendTimeout = Timeout;
                udpClient.Connect(remoteAddress, remotePort);
                udpEndpoint = new IPEndPoint(IPAddress.Parse(remoteAddress), remotePort);
            }
            // Start receiver thread
            ts = new CancellationTokenSource();
            ct = ts.Token;
            _ = Task.Run(() => listen(ct), ct);
            // We are connected
            Status = XCMPConnectionStatus.CONNECTED;
            // Return nothing
            return Task.CompletedTask;
        }

        public Task Disconnect()
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
            // Return nothing
            return Task.CompletedTask;
        }

        private async Task listen(CancellationToken token)
        {

            byte[] rxBuffer = new byte[1024];

            Stopwatch sw = Stopwatch.StartNew();
            long lastHb = sw.ElapsedMilliseconds;

            while (!token.IsCancellationRequested)
            {
                try
                {
                    if (ipType == IPConnectionType.TCP)
                    {
                        int bytesRead = await tcpStream.ReadAsync(rxBuffer, ct);
                        //Log.Verbose("XCMP IP TCP RX: {data}", Convert.ToHexString(rxBuffer.Take(bytesRead).ToArray()));
                        _ = Task.Run(() => OnReceive(this, rxBuffer.Take(bytesRead).ToArray()));
                    }
                    else if (ipType == IPConnectionType.UDP)
                    {
                        UdpReceiveResult udpResult = await udpClient.ReceiveAsync(ct);
                        rxBuffer = udpResult.Buffer;
                        //Log.Verbose("XCMP IP UDP RX: {data}", Convert.ToHexString(udpResult.Buffer));
                        _ = Task.Run(() => OnReceive(this, rxBuffer));
                    }
                    else
                    {
                        Log.Error("How did I get here?");
                        throw new Exception("Existential Crisis Detected");
                    }
                }
                catch (OperationCanceledException)
                {
                    Log.Debug("Stopping XCMP IP listener");
                }
                catch (Exception ex)
                {
                    Log.Error(ex, "Caught unhandled exeption in XCMP IP connection!");
                    Log.Error("Exception thrown when processing data:");
                    Log.Error(Convert.ToHexString(rxBuffer));
                    throw;
                }
            }
            Log.Verbose("XCMP IP Listener Thread Stopped");
        }

        public async Task Send(byte[] data)
        {
            if (ipType == IPConnectionType.TCP)
            {
                await tcpStream.WriteAsync(data, 0, data.Length);
            }
            else
            {
                await udpClient.SendAsync(data, data.Length);
            }
        }
    }
}