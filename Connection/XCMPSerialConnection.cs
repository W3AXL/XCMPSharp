using System.Diagnostics;
using System.Runtime.InteropServices;
using DirectShowLib;
using Serilog;
using System.IO.Ports;
using System.Text;

namespace xcmp.connection
{
    public class XCMPPPPConnection : XCMPBaseConnection
    {
        /// <summary>
        /// The serial port we're connecting to for the PPP session
        /// </summary>
        private string serialPort;
        /// <summary>
        /// 
        /// </summary>
        private int serialBaud;
        /// <summary>
        /// 
        /// </summary>
        private string pppdPath;
        /// <summary>
        /// The wvdial process
        /// </summary>
        private Process pppd;

        public bool Connected { get { return pppdConnected; } }
        
        private bool pppdConnected;
        /// <summary>
        /// Remote radio IP address obtained during ppp connection
        /// </summary>
        private string pppdRemoteIp;
        /// <summary>
        /// The Type of connection to use over the PPP link (TCP or UDP)
        /// </summary>
        private IPConnectionType connType;
        /// <summary>
        /// The port to use for the XCMP connection once connected
        /// </summary>
        private int connPort;
        /// <summary>
        /// Once we have our PPP connection started, we use the XCMP IP classes for everything else (can be TCP or UDP)
        /// </summary>
        private XCMPBaseConnection xcmpConn;

        // Create a new XCMP connection using the specified serial port and a PPP connection
        public XCMPPPPConnection(string serialPort, int serialBaud, string pppdPath, IPConnectionType type, int remotePort)
        {
            this.serialPort = serialPort;
            this.serialBaud = serialBaud;
            this.pppdPath = pppdPath;
            this.connType = type;
            this.connPort = remotePort;
            
            Log.Debug("Created new XCMP PPP connection to {0} at {1} baud with pppd {2} using {3}/{4}", this.serialPort, this.serialBaud, this.pppdPath, connPort, Enum.GetName(connType));
        }

        /// <summary>
        /// We manually dial the PPP modem in the radio using commands found from pppconfig on linux
        /// </summary>
        private void dialModem()
        {
            // Create a new serial port
            SerialPort port = new SerialPort(serialPort);
            port.BaudRate = serialBaud;
            port.NewLine = "\r";
            port.ReadTimeout = 500;
            port.WriteTimeout = 500;
            // Open it
            Log.Debug("Opening XCMP serial port {0} for PPP dialing", serialPort);
            port.Open();
            // Verify connection
            port.WriteLine("ATZ");
            Log.Verbose("[{0}] >> {1}", serialPort, "ATZ");
            string resp = port.ReadLine().Trim();
            // Read again if string blank
            if (string.IsNullOrWhiteSpace(resp)) { resp = port.ReadLine().Trim(); }
            Log.Verbose("[{0}] << {1}", serialPort, resp);
            if (resp != "OK")
            {
                Log.Error("Expected OK from ATZ but got {0}", resp);
                throw new Exception("Failed to connect to XCMP modem!");
            }
            // Dial
            port.WriteLine("ATDT8002");
            Log.Verbose("[{0}] >> {1}", serialPort, "ATDT8002");
            resp = port.ReadLine().Trim();
            // Read again if string blank
            if (string.IsNullOrWhiteSpace(resp)) { resp = port.ReadLine().Trim(); }
            Log.Verbose("[{0}] << {1}", serialPort, resp);
            if (resp != "CONNECT")
            {
                Log.Error("Failed to CONNECT after dial, got {0}", resp);
                throw new Exception("Failed to connect to XCMP modem!");
            }
            // Send an additional carriage return
            port.WriteLine("");
            // Close port
            port.Close();
            Log.Debug("XCMP modem dialed, port closed");
        }

        /// <summary>
        /// Start pppd as a subprocess
        /// </summary>
        private void startPppd()
        {
            // Verify that pppd has the noauth option set in /etc/ppp/options and alert the user if it doesn't
            IEnumerable<string> pppdOpts = File.ReadLines("/etc/ppp/options");
            if (!pppdOpts.Any(line => line == "noauth"))
            {
                Log.Error("To use XCMP serial control as a non-root user, 'noauth' must be present in /etc/ppp/options!");
                throw new Exception("'noauth' missing from /etc/ppp/options");
            }
            else if (pppdOpts.Any(line => line == "auth"))
            {
                Log.Error("To use XCMP serial control as a non-root user, 'auth' must not be present in /etc/ppp/options!");
                throw new Exception("'auth' in /etc/ppp/options");
            }
            // Start the process
            Log.Debug("Starting PPPD {0} for port {1} at {2} baud", pppdPath, serialPort, serialBaud);
            pppd = new Process();
            pppd.StartInfo.FileName = pppdPath;
            pppd.StartInfo.Arguments = $"{serialPort} {serialBaud} nodetach debug noipdefault user \"192.168.128.1\"";
            pppd.StartInfo.CreateNoWindow = true;
            pppd.StartInfo.RedirectStandardOutput = true;
            pppd.StartInfo.RedirectStandardInput = true;
            pppd.StartInfo.RedirectStandardError = true;
            pppd.Exited += pppdExit;
            pppd.OutputDataReceived += pppdData;
            Log.Verbose("{0} {1}", pppd.StartInfo.FileName, pppd.StartInfo.Arguments);
            pppd.Start();
            pppd.BeginOutputReadLine();
            pppd.BeginErrorReadLine();
            // Wait for connection
            Stopwatch sw = Stopwatch.StartNew();
            Log.Information("Connecting to XCMP radio at {0}", serialPort);
            while (!pppdConnected && sw.Elapsed < TimeSpan.FromSeconds(5))
            {
                Thread.Sleep(10);
            }
            if (!pppdConnected)
            {
                Log.Error("Timed out waiting for pppd connection!");
                throw new TimeoutException("Timed out waiting for ppp connection");
            }
        }

        public void Dispose()
        {
            Disconnect();
        }

        public void Connect()
        {
            // On Linux, we use wvdial/pppd
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            {
                // Dial the PPP modem
                dialModem();
                // Start PPPD process
                startPppd();
                // Create a new XCMP IP connection depending on connection type
                xcmpConn = new XCMPIPConnection(pppdRemoteIp, connPort, connType);
                // Connect
                xcmpConn.Connect();
            }
            // Windows, not yet supported (sorry)
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                throw new NotImplementedException($"Windows XTL connection not yet supported, sorry!");
            }
        }

        public void Disconnect()
        {
            Log.Information("Disconnecting from XCMP");
            // Stop xcmp connection
            if (xcmpConn != null)
            {
                // Disconnect from XCMP
                xcmpConn.Disconnect();
                xcmpConn.Dispose();
                xcmpConn = null;
            }
            // Stop ppp process if running
            if (pppd != null)
            {
                
                Log.Debug("Stopping pppd process");
                pppd.Kill();
                pppd.WaitForExit();
                pppd.Dispose();
                pppd = null;
                pppdConnected = false;
                pppdRemoteIp = null;
            }
        }

        private void pppdData(object sender, DataReceivedEventArgs e)
        {
            if (e.Data != null)
            {
                Log.Verbose("[pppd] << {0}", e.Data);
                if (e.Data.Contains("remote IP address"))
                {
                    // Extract remote IP
                    pppdRemoteIp = e.Data.Trim().Replace("remote IP address ", "");
                }
                else if (e.Data.Contains("ip-up finished") && e.Data.Contains("status = 0x0"))
                {
                    if (pppdRemoteIp == null)
                        throw new Exception("PPPD connected without obtaining remote IP address!");

                    Log.Information("XCMP connected to radio at {0}", pppdRemoteIp);
                    pppdConnected = true;
                }
            }
        }

        private void pppdExit(object sender, System.EventArgs e)
        {
            Log.Error("pppd process exited unexpectedly with exit code {0}!", pppd.ExitCode);
            pppd.Dispose();
            pppd = null;
            pppdConnected = false;
            pppdRemoteIp = null;
        }

        public byte[] Receive()
        {
            if (xcmpConn == null)
            {
                throw new InvalidOperationException("XCMP not connected!");
            }
            return xcmpConn.Receive();
        }

        public void Send(byte[] data)
        {
            if (xcmpConn == null)
            {
                throw new InvalidOperationException("XCMP not connected!");
            }
            xcmpConn.Send(data);
        }
    }
}