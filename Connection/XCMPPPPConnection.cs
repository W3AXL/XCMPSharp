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
        /// The serial port object, used for direct control
        /// </summary>
        private SerialPort port;
        /// <summary>
        /// 
        /// </summary>
        private string pppdPath;
        /// <summary>
        /// The wvdial process
        /// </summary>
        private Process pppd;

        public XCMPConnectionStatus Status { get; private set; }
        public event EventHandler<byte[]> OnReceive;
        public int Timeout { get; private set; }
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
        /// <summary>
        /// List of baudrates for port auto-baudrate detection
        /// </summary>
        public static List<int> BaudRates = [
            9600,
            19200,
            38400,
            57600,
            115200
        ];

        // Create a new XCMP connection using the specified serial port and a PPP connection
        public XCMPPPPConnection(string serialPort, int serialBaud, string pppdPath, IPConnectionType type, int remotePort, int timeout = 1000)
        {
            this.serialPort = serialPort;
            this.serialBaud = serialBaud;
            this.pppdPath = pppdPath;
            this.connType = type;
            this.connPort = remotePort;
            this.Timeout = timeout;

            Status = XCMPConnectionStatus.DISCONNECTED;
            
            Log.Debug("Created new XCMP PPP connection to {0} at {1} baud with pppd {2} using {3}/{4}", this.serialPort, this.serialBaud, this.pppdPath, connPort, Enum.GetName(connType));
        }

        /// <summary>
        /// Read a line from the serial port, ignoring empty lines
        /// </summary>
        /// <param name="port"></param>
        /// <returns></returns>
        private string readLine(SerialPort port)
        {
            string resp = port.ReadLine().Trim();
            if (string.IsNullOrWhiteSpace(resp)) { resp = port.ReadLine().Trim(); }
            Log.Verbose("[{0}] << {1}", port.PortName, resp);
            return resp;
        }

        /// <summary>
        /// Write a line to the serial port
        /// </summary>
        /// <param name="port"></param>
        /// <param name="msg"></param>
        private void writeLine(SerialPort port, string msg)
        {
            port.WriteLine(msg);
            Log.Verbose("[{0}] >> {1}", port.PortName, msg);
        }

        /// <summary>
        /// Check for the correct baudrate using AT
        /// </summary>
        /// <param name="port"></param>
        /// <param name="baudrate"></param>
        /// <returns></returns>
        private bool checkBaud(SerialPort port, int baudrate)
        {
            if (port.IsOpen)
                port.Close();
            port.BaudRate = baudrate;
            port.Open();
            try
            {
                // While we're trying to auto-baud, we can also try to hang up any existing PPP connections
                writeLine(port, "ATH");
                string resp = readLine(port);
                // basically, as long as we didn't time out and got some valid data, it's the right baudrate
                if (resp == "OK" || resp == "ERROR" || resp == "")
                    return true;
            }
            catch (TimeoutException)
            {
                return false;
            }
            return false;
        }

        /// <summary>
        /// We manually dial the PPP modem in the radio using commands found from pppconfig on linux
        /// </summary>
        private void dialModem()
        {
            // Create a new serial port, starting at 9600 baud
            port = new SerialPort(serialPort);
            port.BaudRate = 9600;
            port.NewLine = "\r";
            // Set short timeouts for autobaud routine
            port.ReadTimeout = 250;
            port.WriteTimeout = 250;
            // Open it
            Log.Debug("Opening XCMP serial port {0} for PPP dialing", serialPort);
            string resp;
            // Check if we're at 9600 baud first or another baudrate
            bool established = false;
            foreach (int baud in BaudRates)
            {
                if (checkBaud(port, baud))
                {
                    if (baud != serialBaud)
                    {
                        // Switch to desired timeouts now
                        port.Close();
                        port.ReadTimeout = Timeout;
                        port.WriteTimeout = Timeout;
                        port.Open();
                        // Change baudrate to new desired rate
                        Log.Debug("Switching serial connection to {baud} baud", serialBaud);
                        bool ok = false;
                        try
                        {
                            writeLine(port, $"AT+IPR={serialBaud}");
                            resp = readLine(port);
                            if (resp == "OK")
                                ok = true;
                        }
                        catch (TimeoutException) {}
                        if (!ok)
                        {
                            Log.Warning("Did not receive response to baudrate change", serialBaud);
                            //throw new Exception($"Failed to change baudrate to {serialBaud}");
                        }
                        established = true;
                        break;
                    }
                    else
                        established = true;
                        break;
                }
            };
            // Verify the baudrate was detected
            if (!established)
            {
                Log.Error("Failed to establish connection to radio at any valid baudrate!", serialBaud);
                throw new Exception($"Failed to establish connection to radio at any valid baudrate!");
            }
            // Print success
            Log.Information("Connected to radio at {baud} baud", serialBaud);
            // Dial
            writeLine(port, "ATDT8002");
            resp = readLine(port);
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
        private async Task startPppd()
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
            while (Status != XCMPConnectionStatus.CONNECTED && sw.ElapsedMilliseconds < 15000)  // PPP establishment can take a while sometimes
            {
                await Task.Delay(2);
            }
            if (Status != XCMPConnectionStatus.CONNECTED)
            {
                Log.Error("Timed out waiting for pppd connection!");
                throw new TimeoutException("Timed out waiting for ppp connection");
            }
        }

        public void Dispose()
        {
            Disconnect().GetAwaiter().GetResult();
        }

        public async Task Connect()
        {
            Status = XCMPConnectionStatus.CONNECTING;
            // On Linux, we use wvdial/pppd
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            {
                // Dial the PPP modem
                dialModem();
                // Start PPPD process
                await startPppd();
                Log.Debug("PPPD connected successfully, initializing XCMP IP connection...");
                // Create a new XCMP IP connection depending on connection type
                xcmpConn = new XCMPIPConnection(pppdRemoteIp, connPort, connType);
                // Bind receive event
                xcmpConn.OnReceive += (sender, data) => { OnReceive?.Invoke(this, data); };
                // Wait for base connection to connect
                await xcmpConn.Connect();
            }
            // Windows, not yet supported (sorry)
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                throw new NotImplementedException($"Windows XTL connection not yet supported, sorry!");
            }
        }

        public async Task Disconnect()
        {
            // Update status
            Status = XCMPConnectionStatus.DISCONNECTING;
            Log.Information("Disconnecting from XCMP");
            
            // Stop xcmp connection
            if (xcmpConn != null)
            {
                // Disconnect from XCMP
                await xcmpConn.Disconnect();
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
                pppdRemoteIp = null;
            }
            // Send a disconnect
            if (port != null)
            {
                Log.Debug("Sending Hangup ATH to PPP");
                if (!port.IsOpen)
                    port.Open();
                writeLine(port, "ATH");
                port.Close();
                port = null;
            }

            Status = XCMPConnectionStatus.DISCONNECTED;
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
                    Status = XCMPConnectionStatus.CONNECTED;
                }
            }
        }

        private void pppdExit(object sender, System.EventArgs e)
        {
            Log.Error("pppd process exited unexpectedly with exit code {0}!", pppd.ExitCode);
            pppd.Dispose();
            pppd = null;
            pppdRemoteIp = null;

            Status = XCMPConnectionStatus.ERROR;
        }

        public async Task Send(byte[] data)
        {
            if (xcmpConn == null)
            {
                throw new InvalidOperationException("XCMP not connected!");
            }
            await xcmpConn.Send(data);
        }
    }
}